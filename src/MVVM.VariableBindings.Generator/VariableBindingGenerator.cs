
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MVVM.VariableBindings.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class VariableBindingGenerator : IIncrementalGenerator
{
    private const string VariableBoundAttribute = "MVVM.VariableBindings.VariableBoundAttribute";
    private const string ObservablePropertyAttribute = "CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute";
    private const string OptionsSourceName = "OptionsSource";
    private const string SuggestionsSourceName = "SuggestionsSource";

    private static readonly SymbolDisplayFormat QualifiedTypeNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints | SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
                VariableBoundAttribute,
                static (node, _) => node is VariableDeclaratorSyntax,
                static (syntaxContext, _) => CreateCandidate(syntaxContext))
            .Where(static candidate => candidate is not null)!
            .Select((candidate, _) => candidate!);

        var combined = context.CompilationProvider.Combine(candidates.Collect());
        context.RegisterSourceOutput(combined, static (productionContext, tuple) => Execute(productionContext, tuple.Left, tuple.Right));
    }

    private static Candidate? CreateCandidate(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IFieldSymbol fieldSymbol)
        {
            return null;
        }

        var containingType = fieldSymbol.ContainingType;
        if (containingType is null)
        {
            return null;
        }

        if (!IsPartial(containingType))
        {
            return new Candidate(containingType, fieldSymbol, CandidateFlags.NotPartial);
        }

        if (!HasObservableProperty(fieldSymbol))
        {
            return new Candidate(containingType, fieldSymbol, CandidateFlags.MissingObservableProperty);
        }

        var attribute = context.Attributes.FirstOrDefault(a => string.Equals(a.AttributeClass?.ToDisplayString(), VariableBoundAttribute, StringComparison.Ordinal));
        if (attribute is null)
        {
            return null;
        }

        var optionsSource = GetStringArgument(attribute, 0, OptionsSourceName);
        var suggestionsSource = GetStringArgument(attribute, 1, SuggestionsSourceName);

        return new Candidate(containingType, fieldSymbol, CandidateFlags.None, optionsSource, suggestionsSource);
    }

    private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<Candidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var variableOptionSymbol = compilation.GetTypeByMetadataName("MVVM.VariableBindings.VariableOption");
        if (variableOptionSymbol is null)
        {
            return;
        }

        var enumerableSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
        var readonlyListSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
        var listSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IList`1");
        var taskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var cancellationTokenSymbol = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");

        var groups = new Dictionary<INamedTypeSymbol, List<Candidate>>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidates)
        {
            if (!groups.TryGetValue(candidate.ContainingType, out var list))
            {
                list = new List<Candidate>();
                groups.Add(candidate.ContainingType, list);
            }

            list.Add(candidate);
        }

        foreach (var group in groups)
        {
            var classSymbol = group.Key;
            var diagnostics = new List<Diagnostic>();
            var fields = new List<FieldGenerationInfo>();

            foreach (var candidate in group.Value)
            {
                if (candidate.Flags.HasFlag(CandidateFlags.NotPartial))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.ClassMustBePartial, candidate.Field.Locations.FirstOrDefault()));
                    continue;
                }

                if (candidate.Flags.HasFlag(CandidateFlags.MissingObservableProperty))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.MissingObservableProperty, candidate.Field.Locations.FirstOrDefault()));
                    continue;
                }

                var propertyName = GetPropertyName(candidate.Field);
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.CouldNotResolvePropertyName, candidate.Field.Locations.FirstOrDefault(), candidate.Field.Name));
                    continue;
                }

                var bindingPropertyName = propertyName + "Variable";
                var backingFieldName = "_" + ToCamelCase(bindingPropertyName);
                var variableKey = propertyName;

                string? optionsExpression = null;
                bool optionsNeedsMaterialize = true;

                if (!string.IsNullOrWhiteSpace(candidate.OptionsSource))
                {
                    var (member, memberType) = ResolveOptionsSource(classSymbol, candidate.OptionsSource!, variableOptionSymbol, enumerableSymbol, readonlyListSymbol, listSymbol);
                    if (member is null)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.OptionsSourceNotFound, candidate.Field.Locations.FirstOrDefault(), candidate.OptionsSource));
                        continue;
                    }

                    if (memberType is null)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.OptionsSourceInvalid, member.Locations.FirstOrDefault() ?? candidate.Field.Locations.FirstOrDefault(), candidate.OptionsSource));
                        continue;
                    }

                    optionsExpression = BuildMemberInvocation(member);
                    optionsNeedsMaterialize = !IsListLike(memberType, variableOptionSymbol, readonlyListSymbol, listSymbol);
                }

                string? suggestionsExpression = null;
                bool suggestionsIsStatic = false;

                if (!string.IsNullOrWhiteSpace(candidate.SuggestionsSource))
                {
                    var method = ResolveSuggestionsSource(classSymbol, candidate.SuggestionsSource!, variableOptionSymbol, taskSymbol, enumerableSymbol, cancellationTokenSymbol);
                    if (method is null)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.SuggestionsSourceInvalid, candidate.Field.Locations.FirstOrDefault(), candidate.SuggestionsSource));
                    }
                    else
                    {
                        suggestionsExpression = method.IsStatic ? method.ToDisplayString(QualifiedTypeNameFormat) : method.Name;
                        suggestionsIsStatic = method.IsStatic;
                    }
                }
                else
                {
                    var conventionalName = $"Get{propertyName}SuggestionsAsync";
                    var method = ResolveSuggestionsSource(classSymbol, conventionalName, variableOptionSymbol, taskSymbol, enumerableSymbol, cancellationTokenSymbol);
                    if (method is not null)
                    {
                        suggestionsExpression = method.IsStatic ? method.ToDisplayString(QualifiedTypeNameFormat) : method.Name;
                        suggestionsIsStatic = method.IsStatic;
                    }
                }

                fields.Add(new FieldGenerationInfo(
                    candidate.Field,
                    propertyName!,
                    bindingPropertyName,
                    backingFieldName,
                    variableKey,
                    optionsExpression,
                    optionsNeedsMaterialize,
                    suggestionsExpression,
                    suggestionsIsStatic));
            }

            foreach (var diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            if (fields.Count == 0)
            {
                continue;
            }

            var source = GenerateClassSource(classSymbol, fields);
            if (!string.IsNullOrEmpty(source))
            {
                context.AddSource(GetHintName(classSymbol), source);
            }
        }
    }

    private static string GetHintName(INamedTypeSymbol symbol)
    {
        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('.', '_')
            .Replace('+', '_');
        return $"{name}_VariableBindings.g.cs";
    }

    private static string GenerateClassSource(INamedTypeSymbol classSymbol, List<FieldGenerationInfo> fields)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (!classSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            sb.Append("namespace ").Append(classSymbol.ContainingNamespace.ToDisplayString()).AppendLine(";");
            sb.AppendLine();
        }

        var types = GetContainingTypes(classSymbol);
        int indent = 0;
        foreach (var type in types)
        {
            AppendTypeHeader(sb, type, ref indent);
        }

        foreach (var field in fields)
        {
            AppendBindingProperty(sb, field, ref indent);
            sb.AppendLine();
        }

        AppendHelperMembers(sb, fields, ref indent);

        for (int i = types.Length - 1; i >= 0; i--)
        {
            indent--;
            sb.Append(new string(' ', indent * 4)).AppendLine("}");
        }

        return sb.ToString();
    }

    private static void AppendTypeHeader(StringBuilder sb, INamedTypeSymbol type, ref int indent)
    {
        var indentString = new string(' ', indent * 4);
        sb.Append(indentString);
        var keyword = type.TypeKind switch
        {
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ when type.IsRecord => "record",
            _ => "class"
        };
        sb.Append("partial ").Append(keyword).Append(' ').Append(type.Name);
        if (type.TypeParameters.Length > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", type.TypeParameters.Select(p => p.Name)));
            sb.Append('>');
        }
        sb.AppendLine();
        foreach (var constraint in BuildConstraints(type))
        {
            sb.Append(indentString).AppendLine(constraint);
        }
        sb.Append(indentString).AppendLine("{");
        indent++;
    }

    private static IEnumerable<string> BuildConstraints(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var tp in type.TypeParameters)
        {
            var constraints = new List<string>();
            if (tp.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }
            if (tp.HasReferenceTypeConstraint)
            {
                constraints.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            if (tp.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            if (tp.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            constraints.AddRange(tp.ConstraintTypes.Select(t => t.ToDisplayString(QualifiedTypeNameFormat)));
            if (tp.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }
            if (constraints.Count > 0)
            {
                list.Add($"    where {tp.Name} : {string.Join(", ", constraints)}");
            }
        }

        return list;
    }

    private static void AppendBindingProperty(StringBuilder sb, FieldGenerationInfo field, ref int indent)
    {
        var indentString = new string(' ', indent * 4);
        sb.Append(indentString).Append("private global::MVVM.VariableBindings.VariableBinding? ").Append(field.BackingFieldName).AppendLine(";");
        sb.Append(indentString).Append("public global::MVVM.VariableBindings.VariableBinding ").Append(field.BindingPropertyName).AppendLine()
            .Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("get");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).Append("if (").Append(field.BackingFieldName).AppendLine(" is null)");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).Append(field.BackingFieldName).AppendLine(" = new global::MVVM.VariableBindings.VariableBinding();");
        if (!string.IsNullOrWhiteSpace(field.OptionsInvocation))
        {
            sb.Append(indentString).Append("var options = ").Append(field.OptionsInvocation).AppendLine(";");
            if (field.OptionsNeedsMaterialize)
            {
                sb.Append(indentString).Append(field.BackingFieldName).Append(".Options = options is null ? new global::System.Collections.Generic.List<global::MVVM.VariableBindings.VariableOption>() : global::System.Linq.Enumerable.ToList(options);").AppendLine();
            }
            else
            {
                sb.Append(indentString).Append(field.BackingFieldName).Append(".Options = options ?? new global::System.Collections.Generic.List<global::MVVM.VariableBindings.VariableOption>();").AppendLine();
            }
        }
        if (!string.IsNullOrWhiteSpace(field.SuggestionsInvocation))
        {
            if (field.SuggestionsIsStatic)
            {
                sb.Append(indentString).Append(field.BackingFieldName).Append(".SuggestionsProvider = ").Append(field.SuggestionsInvocation).AppendLine(";");
            }
            else
            {
                sb.Append(indentString).Append(field.BackingFieldName).Append(".SuggestionsProvider = this.").Append(field.SuggestionsInvocation).AppendLine(";");
            }
        }
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        sb.Append(indentString).Append("return ").Append(field.BackingFieldName).AppendLine(";");
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
    }

    private static void AppendHelperMembers(StringBuilder sb, List<FieldGenerationInfo> fields, ref int indent)
    {
        var indentString = new string(' ', indent * 4);

        sb.Append(indentString).AppendLine("private global::System.Collections.Generic.IEnumerable<global::MVVM.VariableBindings.VariableBinding> EnumerateVariableBindings()");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        foreach (var field in fields)
        {
            sb.Append(indentString).Append("yield return ").Append(field.BindingPropertyName).AppendLine(";");
        }
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        sb.AppendLine();

        sb.Append(indentString).AppendLine("public global::System.Collections.Generic.Dictionary<string, string> GetVariables()");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("var result = new global::System.Collections.Generic.Dictionary<string, string>();");
        foreach (var field in fields)
        {
            sb.Append(indentString).AppendLine(string.Format("if (!string.IsNullOrWhiteSpace({0}?.Name)) result[\"{1}\"] = {0}!.Name!;", field.BindingPropertyName, field.VariableKey));
        }
        sb.Append(indentString).AppendLine("return result;");
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        sb.AppendLine();

        sb.Append(indentString).AppendLine("public void SetVariables(global::System.Collections.Generic.IDictionary<string, string>? values)");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("if (values is null) return;");
        foreach (var field in fields)
        {
            sb.Append(indentString).AppendLine(string.Format("if (values.TryGetValue(\"{0}\", out var v{0}) && !string.IsNullOrWhiteSpace(v{0})) {1}.Name = v{0}!;", field.VariableKey, field.BindingPropertyName));
        }
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        sb.AppendLine();

        sb.Append(indentString).AppendLine("public void ClearVariables()");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        foreach (var field in fields)
        {
            sb.Append(indentString).AppendLine(string.Format("if ({0} is not null) {0}.Name = null;", field.BackingFieldName));
        }
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        sb.AppendLine();

        sb.Append(indentString).Append("public bool HasAnyVariables => ");
        sb.AppendLine(string.Join(" || ", fields.Select(f => string.Format("{0}?.HasValue == true", f.BindingPropertyName))) + ";");
        sb.AppendLine();

        sb.Append(indentString).AppendLine("public void LoadVariableOptions(global::System.Collections.Generic.IEnumerable<global::MVVM.VariableBindings.VariableOption> options)");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("var list = options is null ? new global::System.Collections.Generic.List<global::MVVM.VariableBindings.VariableOption>() : global::System.Linq.Enumerable.ToList(options);");
        foreach (var field in fields)
        {
            sb.Append(indentString).Append(field.BindingPropertyName).Append(".Options = list;").AppendLine();
        }
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        sb.AppendLine();

        sb.Append(indentString).AppendLine("public void LoadVariableOptions(global::System.Collections.Generic.IDictionary<string, global::System.Collections.Generic.IEnumerable<global::MVVM.VariableBindings.VariableOption>>? map)");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("if (map is null) return;");
        foreach (var field in fields)
        {
            var mapValueName = "values" + field.VariableKey;
            sb.Append(indentString).AppendLine(string.Format("if (map.TryGetValue(\"{0}\", out var {1}))", field.VariableKey, mapValueName));
            sb.Append(indentString).AppendLine("{");
            indent++;
            indentString = new string(' ', indent * 4);
            sb.Append(indentString).AppendLine(string.Format("var list = {0} is null ? new global::System.Collections.Generic.List<global::MVVM.VariableBindings.VariableOption>() : global::System.Linq.Enumerable.ToList({0});", mapValueName));
            sb.Append(indentString).Append(field.BindingPropertyName).Append(".Options = list;").AppendLine();
            indent--;
            indentString = new string(' ', indent * 4);
            sb.Append(indentString).AppendLine("}");
        }
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        sb.AppendLine();

        sb.Append(indentString).AppendLine("public void SetScopeFilter(global::MVVM.VariableBindings.VariableScope scope, bool enabled)");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("foreach (var binding in EnumerateVariableBindings())");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("switch (scope)");
        sb.Append(indentString).AppendLine("{");
        indent++;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("case global::MVVM.VariableBindings.VariableScope.File: binding.IncludeFile = enabled; break;");
        sb.Append(indentString).AppendLine("case global::MVVM.VariableBindings.VariableScope.ActionGroup: binding.IncludeActionGroup = enabled; break;");
        sb.Append(indentString).AppendLine("case global::MVVM.VariableBindings.VariableScope.Action: binding.IncludeAction = enabled; break;");
        sb.Append(indentString).AppendLine("case global::MVVM.VariableBindings.VariableScope.Task: binding.IncludeTask = enabled; break;");
        sb.Append(indentString).AppendLine("case global::MVVM.VariableBindings.VariableScope.TaskRun: binding.IncludeTaskRun = enabled; break;");
        sb.Append(indentString).AppendLine("default: break;");
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
        indent--;
        indentString = new string(' ', indent * 4);
        sb.Append(indentString).AppendLine("}");
    }

    private static string BuildMemberInvocation(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method when method.IsStatic => $"{method.ContainingType.ToDisplayString(QualifiedTypeNameFormat)}.{method.Name}()",
            IMethodSymbol method => $"this.{method.Name}()",
            IPropertySymbol property when property.IsStatic => $"{property.ContainingType.ToDisplayString(QualifiedTypeNameFormat)}.{property.Name}",
            IPropertySymbol property => $"this.{property.Name}",
            _ => string.Empty
        };
    }

    private static (ISymbol? Member, ITypeSymbol? ReturnType) ResolveOptionsSource(INamedTypeSymbol type, string name, INamedTypeSymbol variableOptionSymbol, INamedTypeSymbol? enumerableSymbol, INamedTypeSymbol? readonlyListSymbol, INamedTypeSymbol? listSymbol)
    {
        foreach (var member in type.GetMembers(name))
        {
            switch (member)
            {
                case IPropertySymbol property when property.GetMethod is not null:
                    if (IsEnumerable(property.Type, variableOptionSymbol, enumerableSymbol))
                    {
                        return (property, property.Type);
                    }
                    break;
                case IMethodSymbol method when method.Parameters.Length == 0:
                    if (IsEnumerable(method.ReturnType, variableOptionSymbol, enumerableSymbol))
                    {
                        return (method, method.ReturnType);
                    }
                    break;
            }
        }

        return (null, null);
    }

    private static IMethodSymbol? ResolveSuggestionsSource(INamedTypeSymbol type, string name, INamedTypeSymbol variableOptionSymbol, INamedTypeSymbol? taskSymbol, INamedTypeSymbol? enumerableSymbol, INamedTypeSymbol? cancellationTokenSymbol)
    {
        foreach (var member in type.GetMembers(name))
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (method.Parameters.Length != 1)
            {
                continue;
            }

            if (cancellationTokenSymbol is null || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, cancellationTokenSymbol))
            {
                continue;
            }

            if (taskSymbol is null)
            {
                continue;
            }

            if (method.ReturnType is not INamedTypeSymbol named || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, taskSymbol))
            {
                continue;
            }

            if (named.TypeArguments.Length != 1)
            {
                continue;
            }

            var inner = named.TypeArguments[0];
            if (!IsEnumerable(inner, variableOptionSymbol, enumerableSymbol))
            {
                continue;
            }

            return method;
        }

        return null;
    }

    private static bool IsEnumerable(ITypeSymbol type, INamedTypeSymbol variableOptionSymbol, INamedTypeSymbol? enumerableSymbol)
    {
        if (type is IArrayTypeSymbol array)
        {
            return SymbolEqualityComparer.Default.Equals(array.ElementType, variableOptionSymbol);
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            if (enumerableSymbol is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, enumerableSymbol))
            {
                return named.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], variableOptionSymbol);
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && enumerableSymbol is not null && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, enumerableSymbol))
            {
                if (iface.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], variableOptionSymbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsListLike(ITypeSymbol type, INamedTypeSymbol variableOptionSymbol, INamedTypeSymbol? readonlyListSymbol, INamedTypeSymbol? listSymbol)
    {
        if (type is IArrayTypeSymbol array)
        {
            return SymbolEqualityComparer.Default.Equals(array.ElementType, variableOptionSymbol);
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            if (readonlyListSymbol is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, readonlyListSymbol))
            {
                return true;
            }
            if (listSymbol is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, listSymbol))
            {
                return true;
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType)
            {
                if (readonlyListSymbol is not null && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, readonlyListSymbol) && SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], variableOptionSymbol))
                {
                    return true;
                }
                if (listSymbol is not null && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, listSymbol) && SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], variableOptionSymbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasObservableProperty(IFieldSymbol field)
    {
        foreach (var attribute in field.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (string.Equals(name, ObservablePropertyAttribute, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetPropertyName(IFieldSymbol field)
    {
        var name = field.Name;
        if (name.StartsWith("m_", StringComparison.Ordinal))
        {
            name = name.Substring(2);
        }
        name = name.TrimStart('_');
        if (name.Length == 0)
        {
            return field.Name;
        }
        if (name.Length == 1)
        {
            return name.ToUpperInvariant();
        }
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string? GetStringArgument(AttributeData attribute, int position, string propertyName)
    {
        if (attribute.ConstructorArguments.Length > position)
        {
            var value = attribute.ConstructorArguments[position];
            if (value.Value is string str && !string.IsNullOrWhiteSpace(str))
            {
                return str;
            }
        }

        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, propertyName, StringComparison.Ordinal) && named.Value.Value is string namedValue && !string.IsNullOrWhiteSpace(namedValue))
            {
                return namedValue;
            }
        }

        return null;
    }

    private static ImmutableArray<INamedTypeSymbol> GetContainingTypes(INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var stack = new Stack<INamedTypeSymbol>();
        var current = type;
        while (current is not null)
        {
            stack.Push(current);
            current = current.ContainingType;
        }
        while (stack.Count > 0)
        {
            builder.Add(stack.Pop());
        }
        return builder.ToImmutable();
    }

    private sealed class Candidate
    {
        public Candidate(INamedTypeSymbol containingType, IFieldSymbol field, CandidateFlags flags, string? optionsSource = null, string? suggestionsSource = null)
        {
            ContainingType = containingType;
            Field = field;
            Flags = flags;
            OptionsSource = optionsSource;
            SuggestionsSource = suggestionsSource;
        }

        public INamedTypeSymbol ContainingType { get; }
        public IFieldSymbol Field { get; }
        public CandidateFlags Flags { get; }
        public string? OptionsSource { get; }
        public string? SuggestionsSource { get; }
    }

    [Flags]
    private enum CandidateFlags
    {
        None = 0,
        NotPartial = 1,
        MissingObservableProperty = 2
    }

    private sealed class FieldGenerationInfo
    {
        public FieldGenerationInfo(IFieldSymbol field, string propertyName, string bindingPropertyName, string backingFieldName, string variableKey, string? optionsInvocation, bool optionsNeedsMaterialize, string? suggestionsInvocation, bool suggestionsIsStatic)
        {
            Field = field;
            PropertyName = propertyName;
            BindingPropertyName = bindingPropertyName;
            BackingFieldName = backingFieldName;
            VariableKey = variableKey;
            OptionsInvocation = optionsInvocation;
            OptionsNeedsMaterialize = optionsNeedsMaterialize;
            SuggestionsInvocation = suggestionsInvocation;
            SuggestionsIsStatic = suggestionsIsStatic;
        }

        public IFieldSymbol Field { get; }
        public string PropertyName { get; }
        public string BindingPropertyName { get; }
        public string BackingFieldName { get; }
        public string VariableKey { get; }
        public string? OptionsInvocation { get; }
        public bool OptionsNeedsMaterialize { get; }
        public string? SuggestionsInvocation { get; }
        public bool SuggestionsIsStatic { get; }
    }
}

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor ClassMustBePartial = new(
        id: "MVB001",
        title: "Containing type must be partial",
        messageFormat: "The containing type must be declared as partial to generate variable bindings",
        category: "VariableBindings",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingObservableProperty = new(
        id: "MVB002",
        title: "ObservableProperty attribute required",
        messageFormat: "VariableBound can only be applied to fields marked with [ObservableProperty]",
        category: "VariableBindings",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CouldNotResolvePropertyName = new(
        id: "MVB003",
        title: "Could not determine property name",
        messageFormat: "The source generator could not determine the generated property name for field '{0}'",
        category: "VariableBindings",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OptionsSourceNotFound = new(
        id: "MVB004",
        title: "Options source not found",
        messageFormat: "Could not find options source member '{0}'",
        category: "VariableBindings",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OptionsSourceInvalid = new(
        id: "MVB005",
        title: "Options source has invalid signature",
        messageFormat: "The specified options source '{0}' must return IEnumerable<VariableOption> and have no parameters",
        category: "VariableBindings",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SuggestionsSourceInvalid = new(
        id: "MVB006",
        title: "Suggestions source has invalid signature",
        messageFormat: "The specified suggestions source '{0}' must be Task<IEnumerable<VariableOption>>(CancellationToken)",
        category: "VariableBindings",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

using System;

namespace MVVM.VariableBindings;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class VariableBoundAttribute : Attribute
{
    public VariableBoundAttribute(string? optionsSource = null, string? suggestionsSource = null)
    {
        OptionsSource = optionsSource;
        SuggestionsSource = suggestionsSource;
    }

    public string? OptionsSource { get; }

    public string? SuggestionsSource { get; }
}

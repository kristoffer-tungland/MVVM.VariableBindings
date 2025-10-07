namespace MVVM.VariableBindings;

public sealed class VariableOption
{
    public VariableOption(string name, VariableScope scope, bool isSuggested = false)
    {
        Name = name;
        Scope = scope;
        IsSuggested = isSuggested;
    }

    public string Name { get; }

    public VariableScope Scope { get; }

    public bool IsSuggested { get; }

    public override string ToString() => Name;
}

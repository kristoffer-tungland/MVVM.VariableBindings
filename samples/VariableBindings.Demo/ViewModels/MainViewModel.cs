using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MVVM.VariableBindings;

namespace VariableBindings.Demo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        LoadVariableOptions(AllOptions());
    }

    [ObservableProperty]
    [VariableBound(optionsSource: nameof(AllOptions), suggestionsSource: nameof(GetIsApprovedSuggestionsAsync))]
    private bool isApproved;

    [ObservableProperty]
    [VariableBound(optionsSource: nameof(AllOptions))]
    private string? comment;

    public IEnumerable<VariableOption> AllOptions()
    {
        yield return new VariableOption("File.Title", VariableScope.File);
        yield return new VariableOption("File.Author", VariableScope.File);
        yield return new VariableOption("Group.Name", VariableScope.ActionGroup);
        yield return new VariableOption("Action.Status", VariableScope.Action);
        yield return new VariableOption("Task.Assignee", VariableScope.Task);
        yield return new VariableOption("Task.DueDate", VariableScope.Task);
        yield return new VariableOption("Run.Duration", VariableScope.TaskRun);
        yield return new VariableOption("Run.StartedAt", VariableScope.TaskRun);
    }

    public async Task<IEnumerable<VariableOption>> GetIsApprovedSuggestionsAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        return new[]
        {
            new VariableOption("Action.IsApproved", VariableScope.Action, isSuggested: true),
            new VariableOption("Task.IsApproved", VariableScope.Task, isSuggested: true)
        };
    }
}

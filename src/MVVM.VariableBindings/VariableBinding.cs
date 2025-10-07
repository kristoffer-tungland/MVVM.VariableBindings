using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace MVVM.VariableBindings;

public sealed class VariableBinding : INotifyPropertyChanged
{
    private readonly ObservableCollection<VariableOption> _options = new();
    private string? _name;
    private IReadOnlyList<VariableOption> _baseOptions = Array.Empty<VariableOption>();
    private IReadOnlyList<VariableOption> _suggested = Array.Empty<VariableOption>();
    private string? _searchText;
    private bool _includeFile = true;
    private bool _includeActionGroup = true;
    private bool _includeAction = true;
    private bool _includeTask = true;
    private bool _includeTaskRun = true;
    private CancellationTokenSource? _cts;
    private bool _isLoading;

    public VariableBinding()
    {
        CombinedView = CollectionViewSource.GetDefaultView(_options);
        CombinedView.SortDescriptions.Add(new SortDescription(nameof(VariableOption.IsSuggested), ListSortDirection.Descending));
        CombinedView.SortDescriptions.Add(new SortDescription(nameof(VariableOption.Scope), ListSortDirection.Ascending));
        CombinedView.SortDescriptions.Add(new SortDescription(nameof(VariableOption.Name), ListSortDirection.Ascending));
        CombinedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VariableOption.IsSuggested)));
        CombinedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VariableOption.Scope)));
        CombinedView.Filter = FilterCore;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValue));
        }
    }

    public bool HasValue => !string.IsNullOrWhiteSpace(Name);

    public IReadOnlyList<VariableOption> Options
    {
        get => _baseOptions;
        set
        {
            _baseOptions = value ?? Array.Empty<VariableOption>();
            Rebuild();
        }
    }

    public IReadOnlyList<VariableOption> SuggestedOptions
    {
        get => _suggested;
        set
        {
            _suggested = value ?? Array.Empty<VariableOption>();
            Rebuild();
        }
    }

    public ICollectionView CombinedView { get; }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            CombinedView.Refresh();
        }
    }

    public bool IncludeFile
    {
        get => _includeFile;
        set
        {
            if (_includeFile == value)
            {
                return;
            }

            _includeFile = value;
            OnPropertyChanged();
            CombinedView.Refresh();
        }
    }

    public bool IncludeActionGroup
    {
        get => _includeActionGroup;
        set
        {
            if (_includeActionGroup == value)
            {
                return;
            }

            _includeActionGroup = value;
            OnPropertyChanged();
            CombinedView.Refresh();
        }
    }

    public bool IncludeAction
    {
        get => _includeAction;
        set
        {
            if (_includeAction == value)
            {
                return;
            }

            _includeAction = value;
            OnPropertyChanged();
            CombinedView.Refresh();
        }
    }

    public bool IncludeTask
    {
        get => _includeTask;
        set
        {
            if (_includeTask == value)
            {
                return;
            }

            _includeTask = value;
            OnPropertyChanged();
            CombinedView.Refresh();
        }
    }

    public bool IncludeTaskRun
    {
        get => _includeTaskRun;
        set
        {
            if (_includeTaskRun == value)
            {
                return;
            }

            _includeTaskRun = value;
            OnPropertyChanged();
            CombinedView.Refresh();
        }
    }

    public Func<CancellationToken, Task<IEnumerable<VariableOption>>>? SuggestionsProvider { get; set; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public async Task EnsureSuggestionsAsync()
    {
        if (SuggestionsProvider is null)
        {
            return;
        }

        var previous = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        previous?.Cancel();

        var local = _cts!;
        var token = local.Token;
        IsLoading = true;

        try
        {
            var items = await SuggestionsProvider(token).ConfigureAwait(false);
            if (!token.IsCancellationRequested)
            {
                SuggestedOptions = items?.ToArray() ?? Array.Empty<VariableOption>();
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void Rebuild()
    {
        var map = new Dictionary<string, VariableOption>(StringComparer.OrdinalIgnoreCase);

        foreach (var suggested in _suggested)
        {
            map[suggested.Name] = new VariableOption(suggested.Name, suggested.Scope, true);
        }

        foreach (var option in _baseOptions)
        {
            if (!map.ContainsKey(option.Name))
            {
                map[option.Name] = option;
            }
        }

        _options.Clear();
        foreach (var option in map.Values)
        {
            _options.Add(option);
        }

        CombinedView.Refresh();
        OnPropertyChanged(nameof(CombinedView));
    }

    private bool FilterCore(object obj)
    {
        if (obj is not VariableOption option)
        {
            return false;
        }

        var scopeAllowed = option.Scope switch
        {
            VariableScope.File => IncludeFile,
            VariableScope.ActionGroup => IncludeActionGroup,
            VariableScope.Action => IncludeAction,
            VariableScope.Task => IncludeTask,
            VariableScope.TaskRun => IncludeTaskRun,
            _ => true
        };

        if (!scopeAllowed)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return option.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

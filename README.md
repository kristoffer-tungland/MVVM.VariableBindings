# MVVM.VariableBindings

Source-generated variable binding framework for MVVM Toolkit and WPF. Provides a `VariableWrapper` control and `[VariableBound]` attribute that automatically create rich `VariableBinding` objects for any `[ObservableProperty]`, enabling variable linking, grouping, and scoped filtering with async suggestions.

## Features

- `VariableBinding` runtime class exposes a single `ICollectionView` that can be grouped, sorted, and filtered by scope or search text.
- Built-in support for async suggestions via `SuggestionsProvider` with a loading flag and cancellation handling.
- `VariableWrapper` WPF control renders a combo box with grouping headers (Suggested/Scope) and hooks into the binding to fetch suggestions when opened.
- Source generator that materializes `VariableBinding` properties, serialization helpers (`GetVariables`, `SetVariables`), bulk loaders, scope filtering, and convenience members such as `HasAnyVariables` and `ClearVariables`.
- Works seamlessly with MVVM Toolkit `[ObservableProperty]` fields decorated with `[VariableBound]`.

## Getting started

1. Reference the `MVVM.VariableBindings` project/package in your WPF application.
2. Mark your `ObservableProperty` fields with `[VariableBound]`, optionally pointing to shared option and suggestion providers.
3. Use the generated `{PropertyName}Variable` properties in XAML via the `VariableWrapper` control.

```csharp
[ObservableProperty]
[VariableBound(optionsSource: nameof(AllOptions), suggestionsSource: nameof(GetIsApprovedSuggestionsAsync))]
private bool isApproved;
```

```xaml
<bindings:VariableWrapper Variable="{Binding IsApprovedVariable}">
    <CheckBox Content="Approved" IsChecked="{Binding IsApproved, Mode=TwoWay}" />
</bindings:VariableWrapper>
```

4. Optionally call `LoadVariableOptions(...)` or `LoadVariableOptions(IDictionary<string, IEnumerable<VariableOption>>)` to bulk update options.

## Demo application

The `samples/VariableBindings.Demo` project demonstrates the generated bindings with grouped and filtered combo boxes. Restore and build the solution, then run the WPF project on Windows to explore the behavior.

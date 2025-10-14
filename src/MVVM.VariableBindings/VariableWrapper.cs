using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MVVM.VariableBindings;

[TemplatePart(Name = PartVariableButton, Type = typeof(Button))]
[TemplatePart(Name = PartVariablePopup, Type = typeof(Popup))]
[TemplatePart(Name = PartVariableList, Type = typeof(ListBox))]
[TemplatePart(Name = PartClearButton, Type = typeof(Button))]
[TemplatePart(Name = PartSearchBox, Type = typeof(TextBox))]
public class VariableWrapper : ContentControl
{
    private const string PartVariableButton = "PART_VariableButton";
    private const string PartVariablePopup = "PART_VariablePopup";
    private const string PartVariableList = "PART_VariableList";
    private const string PartClearButton = "PART_ClearButton";
    private const string PartSearchBox = "PART_SearchBox";

    private Button? _variableButton;
    private Popup? _popup;
    private ListBox? _variableList;
    private Button? _clearButton;
    private TextBox? _searchBox;

    static VariableWrapper()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VariableWrapper), new FrameworkPropertyMetadata(typeof(VariableWrapper)));
    }

    public VariableBinding? Variable
    {
        get => (VariableBinding?)GetValue(VariableProperty);
        set => SetValue(VariableProperty, value);
    }

    public static readonly DependencyProperty VariableProperty = DependencyProperty.Register(
        nameof(Variable),
        typeof(VariableBinding),
        typeof(VariableWrapper),
        new PropertyMetadata(null));

    public override void OnApplyTemplate()
    {
        if (_variableButton != null)
        {
            _variableButton.Click -= VariableButtonOnClick;
        }

        if (_popup != null)
        {
            _popup.Opened -= PopupOnOpened;
        }

        if (_variableList != null)
        {
            _variableList.SelectionChanged -= VariableListOnSelectionChanged;
        }

        if (_clearButton != null)
        {
            _clearButton.Click -= ClearButtonOnClick;
        }

        _searchBox = null;

        base.OnApplyTemplate();

        _variableButton = GetTemplateChild(PartVariableButton) as Button;
        if (_variableButton != null)
        {
            _variableButton.Click += VariableButtonOnClick;
        }

        _popup = GetTemplateChild(PartVariablePopup) as Popup;
        if (_popup != null)
        {
            _popup.Opened += PopupOnOpened;
        }

        _variableList = GetTemplateChild(PartVariableList) as ListBox;
        if (_variableList != null)
        {
            _variableList.SelectionChanged += VariableListOnSelectionChanged;
        }

        _clearButton = GetTemplateChild(PartClearButton) as Button;
        if (_clearButton != null)
        {
            _clearButton.Click += ClearButtonOnClick;
        }

        _searchBox = GetTemplateChild(PartSearchBox) as TextBox;
    }

    private async void PopupOnOpened(object? sender, EventArgs e)
    {
        if (Variable is null)
        {
            return;
        }

        try
        {
            await Variable.EnsureSuggestionsAsync();
        }
        catch
        {
            // Swallow errors from suggestion providers to keep UI responsive.
        }
    }

    private void VariableButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_popup is null)
        {
            return;
        }

        if (_popup.IsOpen)
        {
            _popup.IsOpen = false;
            return;
        }

        _popup.IsOpen = true;
        if (_searchBox != null)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }
        else
        {
            _variableList?.Focus();
        }
    }

    private void VariableListOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_popup?.IsOpen == true && Variable?.HasValue == true)
        {
            _popup.IsOpen = false;
        }
    }

    private void ClearButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (Variable is null)
        {
            return;
        }

        Variable.Name = null;
        Variable.SearchText = null;
        e.Handled = true;
    }
}

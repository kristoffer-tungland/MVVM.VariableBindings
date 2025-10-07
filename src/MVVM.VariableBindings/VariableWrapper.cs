using System;
using System.Windows;
using System.Windows.Controls;

namespace MVVM.VariableBindings;

[TemplatePart(Name = PartComboBox, Type = typeof(ComboBox))]
public class VariableWrapper : ContentControl
{
    private const string PartComboBox = "PART_Combo";
    private ComboBox? _comboBox;

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
        if (_comboBox != null)
        {
            _comboBox.DropDownOpened -= ComboBoxOnDropDownOpened;
        }

        base.OnApplyTemplate();

        _comboBox = GetTemplateChild(PartComboBox) as ComboBox;
        if (_comboBox != null)
        {
            _comboBox.DropDownOpened += ComboBoxOnDropDownOpened;
        }
    }

    private async void ComboBoxOnDropDownOpened(object? sender, EventArgs e)
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
}

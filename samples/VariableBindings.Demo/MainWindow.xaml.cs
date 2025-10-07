using System.Windows;
using VariableBindings.Demo.ViewModels;

namespace VariableBindings.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is null)
        {
            DataContext = new MainViewModel();
        }
    }
}

using Avalonia.Controls;
using LiveLingo.App.ViewModels;

namespace LiveLingo.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        vm.RequestClose += () => Close();
    }
}

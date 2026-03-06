using Avalonia.Controls;
using LiveLingo.App.ViewModels;

namespace LiveLingo.App.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow() => InitializeComponent();

    public SetupWizardWindow(SetupWizardViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        vm.RequestClose += () => Close();
    }
}

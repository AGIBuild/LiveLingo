using Avalonia.Controls;
using LiveLingo.Desktop.ViewModels;

namespace LiveLingo.Desktop.Views;

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

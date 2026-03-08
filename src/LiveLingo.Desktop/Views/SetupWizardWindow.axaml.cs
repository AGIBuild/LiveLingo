using Avalonia.Controls;
using Avalonia.Input;
using LiveLingo.Desktop.ViewModels;

namespace LiveLingo.Desktop.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
        HookDragHandle();
    }

    public SetupWizardWindow(SetupWizardViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        HookDragHandle();
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void HookDragHandle()
    {
        var dragHandle = this.FindControl<Border>("DragHandle");
        if (dragHandle is not null)
            dragHandle.PointerPressed += OnDragHandlePointerPressed;
    }

}

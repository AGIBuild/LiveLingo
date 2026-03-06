using LiveLingo.App.Platform;
using LiveLingo.App.ViewModels;
using LiveLingo.Core.Translation;
using NSubstitute;

namespace LiveLingo.App.Tests.ViewModels;

public class OverlayViewModelTests
{
    private readonly TargetWindowInfo _target = new(1, 2, "slack", "Slack", 0, 0, 1920, 1080);
    private readonly ITranslationPipeline _pipeline;
    private readonly ITextInjector _injector;

    public OverlayViewModelTests()
    {
        _pipeline = Substitute.For<ITranslationPipeline>();
        _injector = Substitute.For<ITextInjector>();
    }

    private OverlayViewModel CreateVm() => new(_target, _pipeline, _injector);

    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        var vm = CreateVm();
        Assert.Equal(string.Empty, vm.SourceText);
        Assert.Equal(string.Empty, vm.TranslatedText);
        Assert.NotEmpty(vm.StatusText);
        Assert.NotEmpty(vm.ModeLabel);
    }

    [Fact]
    public void TargetWindowHandle_ReturnsTargetHandle()
    {
        var vm = CreateVm();
        Assert.Equal((nint)1, vm.TargetWindowHandle);
    }

    [Fact]
    public void TargetInputChild_ReturnsChildHandle()
    {
        var vm = CreateVm();
        Assert.Equal((nint)2, vm.TargetInputChild);
    }

    [Fact]
    public void ToggleMode_SwitchesBetweenModes()
    {
        var vm = CreateVm();
        var initialMode = vm.Mode;
        vm.ToggleModeCommand.Execute(null);
        Assert.NotEqual(initialMode, vm.Mode);

        vm.ToggleModeCommand.Execute(null);
        Assert.Equal(initialMode, vm.Mode);
    }

    [Fact]
    public void ToggleMode_UpdatesModeLabel()
    {
        var vm = CreateVm();
        vm.ToggleModeCommand.Execute(null);
        var label1 = vm.ModeLabel;

        vm.ToggleModeCommand.Execute(null);
        var label2 = vm.ModeLabel;

        Assert.NotEqual(label1, label2);
    }

    [Fact]
    public void AutoSend_ReflectsMode()
    {
        var vm = CreateVm();

        vm.ToggleModeCommand.Execute(null);
        if (vm.Mode == InjectionMode.PasteAndSend)
            Assert.True(vm.AutoSend);
        else
            Assert.False(vm.AutoSend);
    }

    [Fact]
    public void Cancel_RaisesRequestClose()
    {
        var vm = CreateVm();
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public async Task InjectAsync_SkipsWhenTranslatedTextEmpty()
    {
        var vm = CreateVm();
        vm.TranslatedText = string.Empty;

        await vm.InjectAsync();

        await _injector.DidNotReceive()
            .InjectAsync(Arg.Any<TargetWindowInfo>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectAsync_DelegatesToInjector()
    {
        var vm = CreateVm();
        vm.TranslatedText = "Hello";

        await vm.InjectAsync();

        await _injector.Received(1)
            .InjectAsync(_target, "Hello", vm.AutoSend, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnSourceTextChanged_TriggersPipeline()
    {
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("Result", "zh", "Result", TimeSpan.FromMilliseconds(10), null));

        var vm = CreateVm();
        vm.SourceText = "你好";

        await Task.Delay(200);

        await _pipeline.Received()
            .ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OnSourceTextChanged_ClearsTranslated_WhenEmpty()
    {
        var vm = CreateVm();
        vm.SourceText = "something";
        vm.TranslatedText = "translated";
        vm.SourceText = "";
        Assert.Equal(string.Empty, vm.TranslatedText);
    }

    [Fact]
    public void PasteAndSend_ModeLabel()
    {
        var vm = CreateVm();
        while (vm.Mode != InjectionMode.PasteAndSend)
            vm.ToggleModeCommand.Execute(null);

        Assert.Equal("Paste & Send", vm.ModeLabel);
        Assert.Contains("send", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PasteOnly_ModeLabel()
    {
        var vm = CreateVm();
        while (vm.Mode != InjectionMode.PasteOnly)
            vm.ToggleModeCommand.Execute(null);

        Assert.Equal("Paste Only", vm.ModeLabel);
        Assert.Contains("paste", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PropertyChanged_IsRaised()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.TranslatedText = "test";

        Assert.Contains("TranslatedText", raised);
    }
}

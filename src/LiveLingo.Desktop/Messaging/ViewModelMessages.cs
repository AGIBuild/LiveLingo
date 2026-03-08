using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LiveLingo.Desktop.Messaging;

public enum AppUiRequestKind
{
    OpenSettings,
    CloseOverlay,
    CloseSettings,
    ShowSettingsPermissionDialog,
    CloseSetupWizard
}

public sealed record AppUiRequest(object Sender, AppUiRequestKind Kind);

public sealed class AppUiRequestMessage(AppUiRequest value) : ValueChangedMessage<AppUiRequest>(value);

public sealed class SettingsChangedMessage;

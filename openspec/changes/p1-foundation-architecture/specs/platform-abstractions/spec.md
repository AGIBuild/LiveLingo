## ADDED Requirements

### Requirement: Platform services aggregation
The system SHALL define `IPlatformServices` in `LiveLingo.App.Platform` namespace as a `IDisposable` that aggregates all platform service interfaces.

```csharp
public interface IPlatformServices : IDisposable
{
    IHotkeyService Hotkey { get; }
    IWindowTracker WindowTracker { get; }
    ITextInjector TextInjector { get; }
    IClipboardService Clipboard { get; }
}
```

#### Scenario: Resolve platform services via DI
- **WHEN** `IPlatformServices` is resolved from the DI container on Windows
- **THEN** all four sub-services SHALL be non-null and functional

### Requirement: Hotkey service interface
The system SHALL define `IHotkeyService` with event-driven hotkey notification, registration, and unregistration.

```csharp
public interface IHotkeyService : IDisposable
{
    event Action<HotkeyEventArgs>? HotkeyTriggered;
    void Register(HotkeyBinding binding);
    void Unregister(string hotkeyId);
}

public record HotkeyBinding(string Id, KeyModifiers Modifiers, string Key);
public record HotkeyEventArgs(string HotkeyId);

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Meta = 8 }
```

#### Scenario: Register and trigger hotkey
- **WHEN** a hotkey binding for Ctrl+Alt+T is registered and the user presses Ctrl+Alt+T
- **THEN** `HotkeyTriggered` event SHALL fire with `HotkeyId` matching the binding's `Id`

#### Scenario: Unregister hotkey
- **WHEN** `Unregister("overlay")` is called
- **THEN** pressing the previously registered key combination SHALL NOT fire `HotkeyTriggered`

### Requirement: Window tracker interface
The system SHALL define `IWindowTracker` that retrieves foreground window information including the input child handle for Electron/Chromium apps.

```csharp
public interface IWindowTracker
{
    TargetWindowInfo? GetForegroundWindowInfo();
}

public record TargetWindowInfo(nint Handle, nint InputChildHandle, string ProcessName, string Title, int Left, int Top, int Width, int Height);
```

#### Scenario: Get Slack window info
- **WHEN** Slack is the foreground window and `GetForegroundWindowInfo()` is called
- **THEN** the result SHALL contain the Slack process name and the `Chrome_RenderWidgetHostHWND` child handle as `InputChildHandle`

#### Scenario: No foreground window
- **WHEN** the desktop is focused (no app window foreground)
- **THEN** `GetForegroundWindowInfo()` SHALL return null

### Requirement: Text injector interface
The system SHALL define `ITextInjector` as an async interface for injecting text into target application windows.

```csharp
public interface ITextInjector
{
    Task InjectAsync(TargetWindowInfo target, string text, bool autoSend, CancellationToken ct = default);
}
```

#### Scenario: Inject with autoSend=true
- **WHEN** `InjectAsync` is called with `autoSend=true`
- **THEN** the text SHALL be pasted into the target window AND the Enter key SHALL be simulated

#### Scenario: Inject with autoSend=false
- **WHEN** `InjectAsync` is called with `autoSend=false`
- **THEN** the text SHALL be pasted into the target window WITHOUT sending Enter

### Requirement: Clipboard service interface
The system SHALL define `IClipboardService` extracted from the text injector, with async set/get operations.

```csharp
public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken ct = default);
    Task<string?> GetTextAsync(CancellationToken ct = default);
}
```

#### Scenario: Set and get clipboard text
- **WHEN** `SetTextAsync("hello")` is called followed by `GetTextAsync()`
- **THEN** the result SHALL be `"hello"`

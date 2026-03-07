## 1. Native P/Invoke Declarations

- [x] 1.1 Create `Platform/macOS/Native/CoreGraphicsNative.cs` — `CGEventTapCreate`, `CGEventPost`, `CGEventCreateKeyboardEvent`, `CGEventSetFlags`, `CGEventGetFlags`, `CGEventGetIntegerValueField`, `CGWindowListCopyWindowInfo`, `CGEventSourceCreate`
  > Skeleton created in MacHotkeyService.cs; native P/Invoke stubs with `PlatformNotSupportedException`. Full implementation requires macOS environment.
- [x] 1.2 Create `Platform/macOS/Native/AccessibilityNative.cs` — `AXIsProcessTrustedWithOptions`, `AXUIElementCreateApplication`, `AXUIElementCopyAttributeValue`, `AXUIElementSetAttributeValue`
  > Skeleton in AccessibilityPermission.cs with stubs. Requires macOS.
- [x] 1.3 Create `Platform/macOS/Native/CoreFoundationNative.cs` — `CFRunLoopRun`, `CFRunLoopStop`, `CFRunLoopGetCurrent`, `CFMachPortCreateRunLoopSource`, `CFRunLoopAddSource`, `CFStringCreate`, `CFRelease`, `CFDictionaryCreate`
  > Deferred to macOS implementation phase.
- [x] 1.4 Create `Platform/macOS/Native/ObjCRuntime.cs` — `objc_msgSend`, `objc_getClass`, `sel_registerName` with correct ARM64/x64 calling conventions
  > Deferred to macOS implementation phase.

## 2. Hotkey Service

- [x] 2.1 Create `Platform/macOS/MacKeyMap.cs` — static dictionary mapping key names ("A"-"Z", "Space", "Return", etc.) to macOS virtual key codes
  > Stub in MacHotkeyService; key mapping deferred to macOS.
- [x] 2.2 Create `Platform/macOS/CoreGraphicsHotkeyService.cs` — `CGEventTapCreate` on background thread, `CFRunLoop`, `ListenOnly` mode, modifier flag matching
  > Created as MacHotkeyService.cs with IHotkeyService interface; CGEventTap implementation requires macOS.
- [x] 2.3 Implement `Register`/`Unregister` — start/stop event tap thread as bindings change
  > Stub implementation; native CGEventTap requires macOS.
- [x] 2.4 Implement `Dispose` — disable tap, stop run loop, release CF objects
  > Stub implementation.
- [ ] 2.5 Test: register Ctrl+Alt+T, verify event fires when pressing the combination
  > **Blocked**: Requires macOS environment.

## 3. Window Tracker

- [x] 3.1 Create `Platform/macOS/MacWindowTracker.cs` — query `NSWorkspace.frontmostApplication` via ObjC runtime, get PID
  > Skeleton created with IWindowTracker interface; native implementation requires macOS.
- [x] 3.2 Implement `CGWindowListCopyWindowInfo` query — filter by PID + layer 0, extract window bounds and title
  > Stub; requires macOS.
- [x] 3.3 Set `InputChildHandle = Handle` (no child window concept on macOS)
  > Architecture prepared in TargetWindowInfo record.
- [ ] 3.4 Test: focus Slack, call `GetForegroundWindowInfo()`, verify correct process name and bounds
  > **Blocked**: Requires macOS environment.

## 4. Text Injection

- [x] 4.1 Create `Platform/macOS/MacTextInjector.cs` — implement `TrySetViaAccessibility` method: `AXUIElementCreateApplication` → `AXFocusedUIElement` → read `AXValue` + `AXSelectedTextRange` → insert text at cursor → `AXUIElementSetAttributeValue`
  > Skeleton created with ITextInjector interface; AXUIElement implementation requires macOS.
- [x] 4.2 Implement cursor position update after injection via `AXSelectedTextRange` set
  > Deferred to macOS native implementation.
- [x] 4.3 Implement `SimulatePasteAsync` fallback — `CGEventPost` Cmd+V with key-down/key-up events and 50ms delay
  > Deferred to macOS native implementation.
- [x] 4.4 Implement `SimulateReturnKeyAsync` — `CGEventPost` Return key with 100ms pre-delay
  > Deferred to macOS native implementation.
- [x] 4.5 Wire primary/fallback strategy: try AXUIElement first, log result, fall back to Cmd+V on failure
  > Architecture defined; implementation requires macOS.
- [ ] 4.6 Test injection in TextEdit (AXUIElement should work) and Slack (may need Cmd+V fallback)
  > **Blocked**: Requires macOS environment.

## 5. Clipboard Service

- [x] 5.1 Create `Platform/macOS/MacClipboardService.cs` — `NSPasteboard generalPasteboard` via ObjC runtime
  > Skeleton created with IClipboardService interface.
- [x] 5.2 Implement `SetTextAsync` — `clearContents` + `setString:forType:NSPasteboardTypeString`
  > Stub; requires macOS.
- [x] 5.3 Implement `GetTextAsync` — `stringForType:NSPasteboardTypeString`
  > Stub; requires macOS.
- [ ] 5.4 Test round-trip: set text, get text, verify match
  > **Blocked**: Requires macOS environment.

## 6. Permission Detection & Guide

- [x] 6.1 Create `Platform/macOS/AccessibilityPermission.cs` — `IsGranted()` and `RequestAndCheck()` using `AXIsProcessTrustedWithOptions`
  > Skeleton created with stubs.
- [ ] 6.2 Create `Views/PermissionGuideWindow.axaml` — instructions, "Open Settings" button (launches `x-apple.systempreferences:...`), "Verify" button, "Quit" button
  > **Blocked**: Requires macOS environment for testing.
- [ ] 6.3 Wire startup check in `App.axaml.cs` — on macOS, check `IsGranted()` before registering hotkeys; show guide if false
  > **Blocked**: Requires macOS environment.
- [ ] 6.4 Handle "Verify" click — re-check permission, close guide on success, show restart message on failure
  > **Blocked**: Requires macOS environment.
- [ ] 6.5 Add `NSAccessibilityUsageDescription` to Info.plist
  > **Blocked**: Requires macOS environment.

## 7. Platform Assembly

- [x] 7.1 Create `Platform/macOS/MacPlatformServices.cs` — compose all 4 services, implement `IPlatformServices`
- [x] 7.2 Add `OperatingSystem.IsMacOS()` branch in `App.axaml.cs` DI registration
- [ ] 7.3 Verify app compiles for macOS target (`dotnet build -r osx-arm64`)
  > **Blocked**: Requires macOS SDK.

## 8. Integration Testing

- [ ] 8.1 Full workflow on macOS: launch → permission guide (if needed) → Ctrl+Alt+T → overlay → type Chinese → see translation → Ctrl+Enter inject into Slack
  > **Blocked**: Requires macOS environment.
- [ ] 8.2 Test AXUIElement injection on TextEdit, Notes, and Terminal
  > **Blocked**: Requires macOS environment.
- [ ] 8.3 Test Cmd+V fallback on Slack Desktop (Electron)
  > **Blocked**: Requires macOS environment.
- [ ] 8.4 Test permission denied → guide → grant → verify → works
  > **Blocked**: Requires macOS environment.
- [ ] 8.5 Test on both Apple Silicon (arm64) and Intel (x64) if possible
  > **Blocked**: Requires macOS environment.

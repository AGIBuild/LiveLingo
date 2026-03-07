## Context

P1-P3 built the full translation pipeline but only on Windows. The `IPlatformServices` abstraction is ready for a second implementation. macOS is a critical target for developer/international teams. The challenge is mapping Windows concepts (HWND, WM_CHAR, SendInput) to macOS equivalents (AXUIElement, CGEventPost, NSWorkspace) while maintaining the same user experience.

Reference: docs/proposals/specs/P4-macos-platform-spec.md contains full implementation blueprints.

## Goals / Non-Goals

**Goals:**
- All platform services working on macOS 12+
- Global hotkey detection via CGEventTap
- Text injection into Slack Desktop macOS via AXUIElement or Cmd+V fallback
- Accessibility permission detection and user-friendly authorization guide
- Same overlay UX as Windows

**Non-Goals:**
- Sandboxed Mac App Store distribution (CGEventTap requires non-sandboxed)
- Touch Bar support
- macOS-specific UI adaptations (menu bar style, native context menus)
- iOS/iPadOS support

## Decisions

### D1: CGEventTap over NSEvent.addGlobalMonitor

**Decision**: Use `CGEventTapCreate` for global hotkey detection.

**Alternatives**:
- **NSEvent.addGlobalMonitorForEvents**: Simpler API, no Accessibility permission needed. Rejected: cannot consume events (may cause double-action with Slack shortcuts), less reliable.
- **Carbon RegisterEventHotKey**: Traditional hotkey API, purpose-built. Rejected: deprecated by Apple, may be removed in future macOS versions.

CGEventTap provides the most control and is the standard for accessibility tools. The Accessibility permission is already needed for text injection.

### D2: AXUIElement primary, Cmd+V fallback

**Decision**: Try `AXUIElementSetAttributeValue` first (direct text setting), fall back to clipboard + `CGEventPost(Cmd+V)` if it fails.

**Rationale**: AXUIElement is cleaner (no clipboard pollution, precise cursor placement). But Electron apps may not support `SetValue` on their text areas. The fallback ensures reliability across all apps. This mirrors the Windows dual-strategy approach (SendInput primary, WM_CHAR fallback).

### D3: Objective-C runtime interop for AppKit

**Decision**: Use `objc_msgSend` / `sel_registerName` / `objc_getClass` for calling AppKit APIs rather than creating Xamarin/MAUI bindings.

**Rationale**: We only need a handful of AppKit calls (`NSWorkspace`, `NSPasteboard`). Full bindings would add unnecessary dependency weight. P/Invoke to libobjc.dylib is sufficient and well-understood in the .NET ecosystem.

### D4: Non-sandboxed DMG distribution

**Decision**: Distribute macOS version as DMG (or PKG) without App Store sandboxing.

**Rationale**: CGEventTap and AXUIElement require non-sandboxed entitlements. App Store distribution is not feasible for this type of accessibility tool. Most similar tools (Raycast, Karabiner, etc.) also distribute outside the App Store.

### D5: Permission guide over silent failure

**Decision**: Show an explicit permission guide window on first launch rather than silently degrading functionality.

**Rationale**: Without Accessibility permission, the core features (hotkey + injection) don't work at all. Silent degradation would be confusing. A clear guide helps users through the one-time setup.

## DI Registration

```csharp
if (OperatingSystem.IsMacOS())
    services.AddSingleton<IPlatformServices>(sp =>
        new MacPlatformServices(sp.GetRequiredService<ILoggerFactory>()));
```

No additional DI changes needed — `MacPlatformServices` implements the same `IPlatformServices` interface as `WindowsPlatformServices`.

## Native API Dependencies

```
CoreGraphics.framework
  ├── CGEventTapCreate / CGEventPost
  ├── CGWindowListCopyWindowInfo
  └── CGEventCreateKeyboardEvent

ApplicationServices.framework
  ├── AXIsProcessTrustedWithOptions
  ├── AXUIElementCreateApplication
  ├── AXUIElementCopyAttributeValue
  └── AXUIElementSetAttributeValue

CoreFoundation.framework
  ├── CFRunLoopRun / CFRunLoopStop
  ├── CFMachPortCreateRunLoopSource
  ├── CFStringCreate / CFRelease
  └── CFDictionaryCreate

libobjc.dylib
  ├── objc_msgSend / objc_getClass
  └── sel_registerName
```

## Risks / Trade-offs

- **[Risk] Electron AXValue support variability**: Different Electron versions handle AXUIElement differently. Slack may accept or reject SetValue. → **Mitigation**: Automatic fallback to Cmd+V. Test against latest Slack macOS release.
- **[Risk] objc_msgSend calling convention on ARM64**: ARM64 macOS has different calling conventions for objc_msgSend depending on return types. → **Mitigation**: Use `[UnmanagedCallersOnly]` and correct delegate signatures. Test on both Intel and Apple Silicon.
- **[Risk] macOS version Accessibility API differences**: AXUIElement behavior may vary between macOS 12-15. → **Mitigation**: Test on macOS 12 (minimum), 14 (current), 15 (latest). Document known differences.
- **[Risk] Permission dialog UX confusion**: Users may not understand why they need to grant Accessibility access. → **Mitigation**: Clear permission guide with screenshots. `NSAccessibilityUsageDescription` in Info.plist explains the purpose.
- **[Trade-off] No App Store distribution**: Limits discoverability. → Acceptable for developer-focused tool. Can explore notarization for Gatekeeper approval.

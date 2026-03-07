## ADDED Requirements

### Requirement: Screen boundary clamping

After the overlay window is shown, `App.axaml.cs` SHALL clamp the window position to ensure it stays within the current screen's working area. The clamp SHALL run after a short delay (50ms) to allow layout measurement to complete.

```csharp
private static void ClampToScreen(Window overlay);
```

#### Scenario: Window near right edge
- **WHEN** overlay is positioned so that its right edge exceeds the screen working area
- **THEN** overlay X position is adjusted so the right edge aligns with screen right

#### Scenario: Window near bottom edge
- **WHEN** overlay is positioned so that its bottom edge exceeds the screen working area
- **THEN** overlay Y position is adjusted so the bottom edge aligns with screen bottom

#### Scenario: Window fully within screen
- **WHEN** overlay position and size are entirely within the working area
- **THEN** position is not modified

#### Scenario: Window near top-left corner
- **WHEN** overlay X or Y is less than the working area origin
- **THEN** position is clamped to the working area top-left

### Requirement: Estimated height for initial positioning

`PositionOverlay` SHALL use an estimated height (260px) instead of reading `overlay.Height` (which may be 0 or NaN with SizeToContent). The actual clamping is done post-show by `ClampToScreen`.

#### Scenario: Initial position uses estimated height
- **WHEN** overlay is positioned before Show()
- **THEN** position calculation uses 260px as estimated height, not overlay.Height

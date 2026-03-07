## ADDED Requirements

### Requirement: Tray menu text is localized
Tray menu entries SHALL be generated from localization keys, not hardcoded literals.

#### Scenario: Chinese tray menu rendering
- **WHEN** current UI culture is `zh-CN`
- **THEN** tray menu displays localized text for open translator, settings, check updates, about, and quit

### Requirement: About dialog text is localized
About dialog SHALL use localized title/body templates and include product name + version.

#### Scenario: About dialog in English
- **WHEN** current UI culture is `en-US`
- **THEN** about title and body are shown in English and include current app version

### Requirement: Update check dialog text is localized
Update-related dialogs (no update, update available, update error) SHALL use localization keys.

#### Scenario: Update available prompt localized
- **WHEN** a new version is available and current culture is `zh-CN`
- **THEN** the update prompt message and action button labels are in Chinese

### Requirement: Settings and overlay key UI text is localized
The visible labels/hints/status text for Settings and Overlay windows SHALL be resource-driven for supported locales.

#### Scenario: Settings labels localized
- **WHEN** current UI culture is `zh-CN` and user opens settings
- **THEN** tab titles and field labels are shown in Chinese

#### Scenario: Overlay status localized
- **WHEN** overlay updates status text (e.g., translating, translated, error)
- **THEN** status text uses localized templates

### Requirement: Notification toast text is localized
Toast title, message template, and action button label SHALL be localizable.

#### Scenario: Startup warning toast in Chinese
- **WHEN** startup health check raises missing model issue under `zh-CN`
- **THEN** toast title/message/button are displayed in Chinese

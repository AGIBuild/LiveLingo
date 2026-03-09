## ADDED Requirements

### Requirement: Model download with progress reporting
`ModelManager` SHALL download model files from remote URLs (HuggingFace) using streaming HTTP with `HttpCompletionOption.ResponseHeadersRead`. Download progress SHALL be reported via `IProgress<ModelDownloadProgress>` at most every 100ms.

#### Scenario: Download new model with progress
- **WHEN** `EnsureModelAsync` is called for a model not yet installed
- **THEN** the model files SHALL be downloaded to `{ModelStoragePath}/{modelId}/` and progress SHALL be reported throughout

#### Scenario: Skip download for installed model
- **WHEN** `EnsureModelAsync` is called for a model that already has a valid `manifest.json`
- **THEN** the method SHALL return immediately without network requests

### Requirement: Resume interrupted downloads
`ModelManager` SHALL support resuming interrupted downloads using HTTP `Range` header. Partially downloaded files SHALL be stored with a `.part` extension.

#### Scenario: Resume after network interruption
- **WHEN** a download is interrupted and `EnsureModelAsync` is called again for the same model
- **THEN** the download SHALL resume from the last byte position of the `.part` file

#### Scenario: Cancel preserves partial file
- **WHEN** a download is cancelled via `CancellationToken`
- **THEN** the `.part` file SHALL be preserved for future resumption

### Requirement: Model manifest
Each installed model directory SHALL continue to contain a `manifest.json` used by `ListInstalled()` for readiness checks and user-visible model lists.

#### Scenario: Manifest created after successful download
- **WHEN** model download completes successfully
- **THEN** a `manifest.json` SHALL be written to the model directory with accurate metadata

#### Scenario: Installed model discoverable by readiness service
- **WHEN** model download completes and manifest exists
- **THEN** `ListInstalled()` SHALL include the model with correct id/type metadata

### Requirement: Model registry
A static `ModelRegistry` class SHALL define model descriptors for translation and post-processing. FastText MAY remain defined as a descriptor, but SHALL NOT be part of the setup required download set.

#### Scenario: Required set excludes FastText
- **WHEN** `ModelRegistry.RequiredModels` or `GetRequiredModelsForLanguagePair()` is queried
- **THEN** returned required models SHALL include translation baseline only and SHALL NOT include `FastTextLid`

#### Scenario: Lookup model by ID
- **WHEN** `ModelRegistry.All` is queried for `"marian-zh-en"`
- **THEN** the descriptor SHALL contain the correct HuggingFace download URL and size

### Requirement: Disk space validation
`ModelManager` SHALL check available disk space before starting a download. If insufficient space exists, it SHALL throw `InsufficientDiskSpaceException`.

#### Scenario: Insufficient disk space
- **WHEN** available disk space is less than the model's `SizeBytes`
- **THEN** `EnsureModelAsync` SHALL throw `InsufficientDiskSpaceException` without creating any files

### Requirement: Delete model and reclaim space
`DeleteModelAsync` SHALL remove the model directory and all its contents. `GetTotalDiskUsage` SHALL return the sum of all installed model sizes.

#### Scenario: Delete installed model
- **WHEN** `DeleteModelAsync("marian-zh-en")` is called
- **THEN** the `{ModelStoragePath}/marian-zh-en/` directory SHALL be deleted and `ListInstalled()` SHALL no longer include it

### Requirement: Concurrent download protection
`ModelManager` SHALL prevent concurrent downloads of the same model. If a download is already in progress, a second `EnsureModelAsync` call for the same model SHALL await the existing download.

#### Scenario: Duplicate download request
- **WHEN** two concurrent calls to `EnsureModelAsync` are made for the same model
- **THEN** only one HTTP download SHALL occur; both calls SHALL complete when it finishes

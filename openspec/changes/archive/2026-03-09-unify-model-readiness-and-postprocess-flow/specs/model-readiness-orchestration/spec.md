## ADDED Requirements

### Requirement: Unified model readiness service contract

The Core layer SHALL provide a dedicated readiness service for model availability checks, and callers MUST use this service instead of direct filesystem probing.

```csharp
public interface IModelReadinessService
{
    bool IsInstalled(string modelId);
    void EnsureTranslationModelReady(string sourceLanguage, string targetLanguage);
    void EnsurePostProcessingModelReady();
}
```

#### Scenario: Readiness service resolves model availability from ModelManager
- **WHEN** `EnsurePostProcessingModelReady()` is called
- **THEN** the service SHALL determine readiness from `IModelManager.ListInstalled()` using model id matching

### Requirement: Typed not-ready exception

When a required model is missing, the readiness layer SHALL throw a typed `ModelNotReadyException` containing `ModelType`, `ModelId`, and actionable message metadata.

#### Scenario: Missing post-processing model emits typed exception
- **WHEN** post-processing is requested and `qwen25-1.5b` is not installed
- **THEN** `ModelNotReadyException` SHALL be thrown with `ModelType = PostProcessing` and `ModelId = "qwen25-1.5b"`

### Requirement: Dependency injection registration

`AddLiveLingoCore()` SHALL register readiness services as singleton dependencies consumable by Pipeline and related Core services.

#### Scenario: Readiness service resolvable from DI container
- **WHEN** application bootstraps with `AddLiveLingoCore()`
- **THEN** `IModelReadinessService` SHALL be resolvable from DI and shared as a singleton

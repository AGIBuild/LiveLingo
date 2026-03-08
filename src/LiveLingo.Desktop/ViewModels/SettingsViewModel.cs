using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly ILogger? _logger;
    private readonly IMessenger _messenger;
    private string? _originalModelStoragePath;
    private bool _isLoadingWorkingCopy;
    private bool _isSyncingTranslationSelection;

    [ObservableProperty] private SettingsModel _workingCopy = SettingsModel.CreateDefault();
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _migrationError;
    [ObservableProperty] private bool _showPermissionSection;

    public static IReadOnlyList<string> InjectionModes { get; } = ["PasteAndSend", "PasteOnly"];
    public static IReadOnlyList<string> PostProcessModes { get; } = ["Off", "Summarize", "Optimize", "Colloquialize"];
    public static IReadOnlyList<string> LogLevels { get; } = ["Verbose", "Debug", "Information", "Warning", "Error"];
    public static IReadOnlyList<UILanguageOption> UILanguages { get; } =
        [new("en-US", "English"), new("zh-CN", "简体中文")];

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }
    public ObservableCollection<ModelItemViewModel> Models { get; }
    public ObservableCollection<TranslationModelOption> AvailableTranslationModels { get; }

    public SettingsViewModel(
        ISettingsService settings,
        IModelManager modelManager,
        ITranslationEngine? engine = null,
        ILogger<SettingsViewModel>? logger = null,
        IMessenger? messenger = null)
    {
        _settings = settings;
        _modelManager = modelManager;
        _logger = logger;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        AvailableLanguages = engine?.SupportedLanguages ?? [];
        AvailableTranslationModels = new ObservableCollection<TranslationModelOption>();
        Models = ModelItemViewModel.CreateAll(modelManager);
        HookWorkingCopy(WorkingCopy);
        HookModelItemChanges();
        RefreshTranslationModelsInternal();
        LoadFromSettings(_settings.Current);
        InitPermissions();
    }

    public SettingsViewModel(ISettingsService settings, ITranslationEngine? engine = null, IMessenger? messenger = null)
    {
        _settings = settings;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        AvailableLanguages = engine?.SupportedLanguages ?? [];
        AvailableTranslationModels = new ObservableCollection<TranslationModelOption>();
        Models = [];
        HookWorkingCopy(WorkingCopy);
        RefreshTranslationModelsInternal();
        LoadFromSettings(_settings.Current);
        InitPermissions();
    }

    partial void OnWorkingCopyChanged(SettingsModel? oldValue, SettingsModel newValue)
    {
        if (oldValue is not null)
            UnhookWorkingCopy(oldValue);
        HookWorkingCopy(newValue);
    }

    private void InitPermissions()
    {
        ShowPermissionSection = OperatingSystem.IsMacOS();
    }

    private void HookWorkingCopy(SettingsModel model)
    {
        model.PropertyChanged -= OnWorkingCopyRootChanged;
        model.PropertyChanged += OnWorkingCopyRootChanged;

        HookNestedGroups(model);
    }

    private void UnhookWorkingCopy(SettingsModel model)
    {
        model.PropertyChanged -= OnWorkingCopyRootChanged;
        UnhookNestedGroups(model);
    }

    private static void HookGroup(INotifyPropertyChanged? group, PropertyChangedEventHandler handler)
    {
        if (group is null) return;
        group.PropertyChanged -= handler;
        group.PropertyChanged += handler;
    }

    private static void UnhookGroup(INotifyPropertyChanged? group, PropertyChangedEventHandler handler)
    {
        if (group is null) return;
        group.PropertyChanged -= handler;
    }

    private void HookNestedGroups(SettingsModel model)
    {
        HookGroup(model.Hotkeys, OnWorkingCopyNestedChanged);
        HookGroup(model.Translation, OnWorkingCopyNestedChanged);
        HookGroup(model.Processing, OnWorkingCopyNestedChanged);
        HookGroup(model.UI, OnWorkingCopyNestedChanged);
        HookGroup(model.Update, OnWorkingCopyNestedChanged);
        HookGroup(model.Advanced, OnWorkingCopyNestedChanged);
    }

    private void UnhookNestedGroups(SettingsModel model)
    {
        UnhookGroup(model.Hotkeys, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Translation, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Processing, OnWorkingCopyNestedChanged);
        UnhookGroup(model.UI, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Update, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Advanced, OnWorkingCopyNestedChanged);
    }

    private void OnWorkingCopyRootChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsModel.Hotkeys) or nameof(SettingsModel.Translation) or
            nameof(SettingsModel.Processing) or nameof(SettingsModel.UI) or
            nameof(SettingsModel.Update) or nameof(SettingsModel.Advanced))
        {
            UnhookNestedGroups(WorkingCopy);
            HookNestedGroups(WorkingCopy);
        }

        MarkDirtyIfNeeded();
    }

    private void OnWorkingCopyNestedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TranslationSettings translation)
        {
            if (e.PropertyName == nameof(TranslationSettings.ActiveTranslationModelId))
                OnActiveTranslationModelIdChanged(translation.ActiveTranslationModelId);
            else if (e.PropertyName is nameof(TranslationSettings.DefaultSourceLanguage) or nameof(TranslationSettings.DefaultTargetLanguage))
                SyncModelSelectionFromLanguagePair(translation.DefaultSourceLanguage, translation.DefaultTargetLanguage);
        }

        MarkDirtyIfNeeded();
    }

    private void MarkDirtyIfNeeded()
    {
        if (!_isLoadingWorkingCopy && !_isSyncingTranslationSelection)
            IsDirty = true;
    }

    private void LoadFromSettings(SettingsModel source)
    {
        _isLoadingWorkingCopy = true;
        try
        {
            var clone = source.DeepClone();
            var selectedModel = ResolveInitialTranslationModel(clone.Translation);
            if (selectedModel is { Type: ModelType.Translation } &&
                !string.IsNullOrWhiteSpace(selectedModel.SourceLanguage) &&
                !string.IsNullOrWhiteSpace(selectedModel.TargetLanguage))
            {
                clone.Translation.DefaultSourceLanguage = selectedModel.SourceLanguage!;
                clone.Translation.DefaultTargetLanguage = selectedModel.TargetLanguage!;
                clone.Translation.ActiveTranslationModelId = selectedModel.Id;
            }
            else
            {
                clone.Translation.ActiveTranslationModelId ??=
                    ModelRegistry.FindTranslationModel(
                        clone.Translation.DefaultSourceLanguage,
                        clone.Translation.DefaultTargetLanguage)?.Id;
            }

            if (!UILanguages.Any(l => string.Equals(l.Code, clone.UI.Language, StringComparison.OrdinalIgnoreCase)))
                clone.UI.Language = UILanguages[0].Code;

            WorkingCopy = clone;
            _originalModelStoragePath = clone.Advanced.ModelStoragePath;
            IsDirty = false;
        }
        finally
        {
            _isLoadingWorkingCopy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!IsDirty)
        {
            _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
            return;
        }

        MigrationError = null;
        var oldPath = NormalizePathForCompare(_originalModelStoragePath);
        var newPath = NormalizePathForCompare(WorkingCopy.Advanced.ModelStoragePath);
        if (_modelManager is not null && !string.IsNullOrEmpty(newPath) &&
            !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _modelManager.MigrateStoragePathAsync(newPath);
                _originalModelStoragePath = WorkingCopy.Advanced.ModelStoragePath;
            }
            catch (Exception ex)
            {
                MigrationError = $"Migration failed: {ex.Message}";
                _logger?.LogError(ex, "Failed to migrate model storage path");
                return;
            }
        }

        var activeModel = AvailableTranslationModels.FirstOrDefault(m =>
            string.Equals(m.Id, WorkingCopy.Translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase));
        if (activeModel is { Type: ModelType.Translation } &&
            !string.IsNullOrWhiteSpace(activeModel.SourceLanguage) &&
            !string.IsNullOrWhiteSpace(activeModel.TargetLanguage))
        {
            WorkingCopy.Translation.DefaultSourceLanguage = activeModel.SourceLanguage!;
            WorkingCopy.Translation.DefaultTargetLanguage = activeModel.TargetLanguage!;
            WorkingCopy.Translation.ActiveTranslationModelId = activeModel.Id;
        }
        else if (activeModel is not null)
        {
            WorkingCopy.Translation.ActiveTranslationModelId = activeModel.Id;
        }
        else
        {
            WorkingCopy.Translation.ActiveTranslationModelId = ModelRegistry.FindTranslationModel(
                WorkingCopy.Translation.DefaultSourceLanguage,
                WorkingCopy.Translation.DefaultTargetLanguage)?.Id;
        }

        _settings.Replace(WorkingCopy);
        _messenger.Send(new SettingsChangedMessage());
        IsDirty = false;
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
    }

    [RelayCommand]
    private void CheckPermissions() =>
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.ShowSettingsPermissionDialog)));

    [RelayCommand]
    private void RefreshTranslationModels() => RefreshTranslationModelsInternal();

    [RelayCommand]
    private void Reset() => LoadFromSettings(SettingsModel.CreateDefault());

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings(_settings.Current);
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
    }

    private static string NormalizePathForCompare(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private void OnActiveTranslationModelIdChanged(string? modelId)
    {
        if (_isSyncingTranslationSelection || string.IsNullOrWhiteSpace(modelId))
            return;

        var selected = AvailableTranslationModels.FirstOrDefault(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (selected is null ||
            selected.Type != ModelType.Translation ||
            string.IsNullOrWhiteSpace(selected.SourceLanguage) ||
            string.IsNullOrWhiteSpace(selected.TargetLanguage))
        {
            return;
        }

        _isSyncingTranslationSelection = true;
        try
        {
            WorkingCopy.Translation.DefaultSourceLanguage = selected.SourceLanguage!;
            WorkingCopy.Translation.DefaultTargetLanguage = selected.TargetLanguage!;
        }
        finally
        {
            _isSyncingTranslationSelection = false;
        }
    }

    private void SyncModelSelectionFromLanguagePair(string sourceLanguage, string targetLanguage)
    {
        if (_isSyncingTranslationSelection)
            return;

        var matched = AvailableTranslationModels.FirstOrDefault(m =>
            m.Type == ModelType.Translation &&
            string.Equals(m.SourceLanguage, sourceLanguage, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.TargetLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase));

        _isSyncingTranslationSelection = true;
        try
        {
            WorkingCopy.Translation.ActiveTranslationModelId = matched?.Id;
        }
        finally
        {
            _isSyncingTranslationSelection = false;
        }
    }

    private TranslationModelOption? ResolveInitialTranslationModel(TranslationSettings translation)
    {
        if (!string.IsNullOrWhiteSpace(translation.ActiveTranslationModelId))
        {
            var byId = AvailableTranslationModels.FirstOrDefault(m =>
                string.Equals(m.Id, translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        return AvailableTranslationModels.FirstOrDefault(m =>
            m.Type == ModelType.Translation &&
            string.Equals(m.SourceLanguage, translation.DefaultSourceLanguage, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.TargetLanguage, translation.DefaultTargetLanguage, StringComparison.OrdinalIgnoreCase));
    }

    private void HookModelItemChanges()
    {
        foreach (var item in Models)
            item.PropertyChanged += OnModelItemPropertyChanged;
    }

    private void OnModelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelItemViewModel.IsInstalled))
            RefreshTranslationModelsInternal();
    }

    private void RefreshTranslationModelsInternal()
    {
        var currentSource = WorkingCopy.Translation.DefaultSourceLanguage;
        var currentTarget = WorkingCopy.Translation.DefaultTargetLanguage;
        var currentModelId = WorkingCopy.Translation.ActiveTranslationModelId;

        var merged = BuildRegistryModels().ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        if (_modelManager is not null)
        {
            foreach (var installed in _modelManager.ListInstalled()
                         .Where(m => m.Type is ModelType.Translation or ModelType.PostProcessing))
            {
                if (merged.ContainsKey(installed.Id))
                    continue;

                if (installed.Type == ModelType.Translation &&
                    TryParseLanguagePairFromModelId(installed.Id, out var src, out var tgt))
                {
                    merged[installed.Id] = new TranslationModelOption(
                        installed.Id,
                        installed.DisplayName,
                        ModelType.Translation,
                        src,
                        tgt);
                    continue;
                }

                if (installed.Type == ModelType.PostProcessing)
                {
                    merged[installed.Id] = new TranslationModelOption(
                        installed.Id,
                        installed.DisplayName,
                        ModelType.PostProcessing,
                        null,
                        null);
                }
            }
        }

        var ordered = merged.Values
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AvailableTranslationModels.Clear();
        foreach (var model in ordered)
            AvailableTranslationModels.Add(model);

        _isSyncingTranslationSelection = true;
        try
        {
            var restored = !string.IsNullOrWhiteSpace(currentModelId)
                ? AvailableTranslationModels.FirstOrDefault(m =>
                    string.Equals(m.Id, currentModelId, StringComparison.OrdinalIgnoreCase))
                : null;
            restored ??= AvailableTranslationModels.FirstOrDefault(m =>
                m.Type == ModelType.Translation &&
                string.Equals(m.SourceLanguage, currentSource, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.TargetLanguage, currentTarget, StringComparison.OrdinalIgnoreCase));
            WorkingCopy.Translation.ActiveTranslationModelId = restored?.Id;
        }
        finally
        {
            _isSyncingTranslationSelection = false;
        }
    }

    private static IReadOnlyList<TranslationModelOption> BuildRegistryModels()
    {
        return ModelRegistry.AllModels
            .Where(m => m.Type is ModelType.Translation or ModelType.PostProcessing)
            .Select(CreateTranslationModelOption)
            .Where(m => m is not null)
            .Select(m => m!)
            .ToArray();
    }

    private static TranslationModelOption? CreateTranslationModelOption(ModelDescriptor descriptor)
    {
        if (descriptor.Type == ModelType.Translation)
        {
            if (!TryParseLanguagePairFromModelId(descriptor.Id, out var source, out var target))
                return null;

            return new TranslationModelOption(descriptor.Id, descriptor.DisplayName, ModelType.Translation, source, target);
        }

        if (descriptor.Type == ModelType.PostProcessing)
            return new TranslationModelOption(descriptor.Id, descriptor.DisplayName, ModelType.PostProcessing, null, null);

        return null;
    }

    private static bool TryParseLanguagePairFromModelId(string modelId, out string sourceLanguage, out string targetLanguage)
    {
        sourceLanguage = string.Empty;
        targetLanguage = string.Empty;

        const string prefix = "opus-mt-";
        if (!modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var pairPart = modelId[prefix.Length..];
        var parts = pairPart.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        sourceLanguage = parts[0];
        targetLanguage = parts[1];
        return true;
    }
}

public record UILanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public record TranslationModelOption(
    string Id,
    string DisplayName,
    ModelType Type,
    string? SourceLanguage,
    string? TargetLanguage)
{
    public override string ToString() => DisplayName;

    public string PairLabel =>
        Type == ModelType.Translation && !string.IsNullOrWhiteSpace(SourceLanguage) && !string.IsNullOrWhiteSpace(TargetLanguage)
            ? $"{SourceLanguage}→{TargetLanguage}"
            : Type == ModelType.PostProcessing
                ? "Post-processing"
                : Type.ToString();
}

using LiveLingo.Core.Dictation;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Services.Warmup;

/// <summary>
/// The one place that decides when the models are loaded, so that nothing is waited for when it is needed:
/// <list type="bullet">
/// <item>the speech models a few seconds after the application started (unless the user turned that off), and right
/// after they were downloaded: dictation then starts at once;</item>
/// <item>when the composer opens, whatever is not in memory yet: the speech models first, then the model of the output
/// the composer starts with, since that is what the first sentence will need;</item>
/// <item>when the output is changed, the model of the new output.</item>
/// </list>
/// What stays in memory, and for how long, is the business of the models themselves (they unload when idle). A model that
/// is not installed, or a role that runs in the cloud, is simply not loaded. Every method starts its work and returns at
/// once; the work runs on the thread pool once it has to wait.
/// </summary>
public sealed class ModelWarmup : IDisposable
{
    /// <summary>The application finishes starting before it loads gigabytes of models.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    private readonly ISpeechWarmup _speech;
    private readonly IInferenceWarmup _inference;
    private readonly ISettingsService _settings;
    private readonly IModelDownloadCoordinator _downloads;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public ModelWarmup(
        ISpeechWarmup speech,
        IInferenceWarmup inference,
        ISettingsService settings,
        IModelDownloadCoordinator downloads,
        ILogger<ModelWarmup> logger,
        TimeProvider? time = null)
    {
        _speech = speech;
        _inference = inference;
        _settings = settings;
        _downloads = downloads;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _downloads.StateChanged += OnDownloadChanged;
    }

    /// <summary>The application has started.</summary>
    public void AtStartup()
    {
        if (!_settings.Current.Composer.PreloadSpeech)
            return;

        _ = Start(async () =>
        {
            await Task.Delay(StartupDelay, _time).ConfigureAwait(false);
            await WarmSpeechAsync().ConfigureAwait(false);
        });
    }

    /// <summary>The composer is opening with <paramref name="output"/>.</summary>
    public void OnSummon(ComposerOutput output) =>
        _ = Start(async () =>
        {
            await WarmSpeechAsync().ConfigureAwait(false);
            await WarmOutputAsync(output).ConfigureAwait(false);
        });

    /// <summary>The user (or a banner) changed the output of an open composer.</summary>
    public void OnOutputChanged(ComposerOutput output) => _ = Start(() => WarmOutputAsync(output));

    public void Dispose() => _downloads.StateChanged -= OnDownloadChanged;

    private void OnDownloadChanged(ModelDownloadState state)
    {
        if (state.IsInstalled && ModelCatalog.Speech.Any(model => model.Id == state.ModelId) && _settings.Current.Composer.PreloadSpeech)
            _ = Start(WarmSpeechAsync);
    }

    private Task WarmSpeechAsync() =>
        Warm("speech models", () => _speech.WarmAsync(Glossary.Parse(_settings.Current.Composer.Glossary).Terms));

    private Task WarmOutputAsync(ComposerOutput output)
    {
        var composer = _settings.Current.Composer;
        InferenceRole? role = output switch
        {
            ComposerOutput.Clean when composer.CleanupEnabled => InferenceRole.Cleaner,
            ComposerOutput.Translate => InferenceRole.Translator,
            _ => null
        };

        return role is { } needed ? Warm($"{needed} model", () => _inference.WarmAsync(needed)) : Task.CompletedTask;
    }

    private async Task Warm(string what, Func<Task> warm)
    {
        try
        {
            await warm().ConfigureAwait(false);
        }
        catch (ModelNotReadyException)
        {
            _logger.LogDebug("Not preparing the {What}: they are not installed yet", what);
        }
        catch (Exception ex)
        {
            // Preparing is an optimization: whoever needs the model asks for it again and reports the failure.
            _logger.LogWarning(ex, "Could not prepare the {What}", what);
        }
    }

    /// <summary>
    /// Runs on the caller's thread up to the first wait, so a model that starts loading is already reported as loading
    /// when the caller returns. The work never throws.
    /// </summary>
    private static Task Start(Func<Task> work) => work();
}

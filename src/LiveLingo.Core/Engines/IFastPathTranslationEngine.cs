namespace LiveLingo.Core.Engines;

/// <summary>
/// Marker for a translation engine optimised for specific language pairs with
/// minimal latency (e.g. Marian ONNX). Only used for pairs it natively
/// supports; <see cref="HybridTranslationEngine"/> delegates to it when
/// <see cref="ITranslationEngine.SupportsLanguagePair"/> returns true AND the
/// underlying model assets are complete on disk.
/// </summary>
public interface IFastPathTranslationEngine : ITranslationEngine;

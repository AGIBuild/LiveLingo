namespace LiveLingo.Core.Engines;

/// <summary>
/// Marker for the general-purpose chat-based translation engine
/// (e.g. Gemma/Qwen via MEA <c>IChatClient</c>).
/// <see cref="HybridTranslationEngine"/> routes to this engine whenever no
/// fast-path specialised engine applies.
/// </summary>
public interface IChatPathTranslationEngine : ITranslationEngine;

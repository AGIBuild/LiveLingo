namespace LiveLingo.Core.Cloud;

/// <summary>A role is switched to the cloud, but its endpoint or key is missing or unusable.</summary>
public sealed class CloudNotConfiguredException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

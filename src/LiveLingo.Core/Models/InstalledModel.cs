namespace LiveLingo.Core.Models;

public record InstalledModel(
    string Id,
    string DisplayName,
    string LocalPath,
    long SizeBytes,
    ModelType Type,
    DateTime InstalledAt);

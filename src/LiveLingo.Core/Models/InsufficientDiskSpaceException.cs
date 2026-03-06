namespace LiveLingo.Core.Models;

public sealed class InsufficientDiskSpaceException : Exception
{
    public long RequiredBytes { get; }
    public long AvailableBytes { get; }

    public InsufficientDiskSpaceException(long requiredBytes, long availableBytes)
        : base($"Insufficient disk space. Required: {requiredBytes} bytes, Available: {availableBytes} bytes")
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }
}

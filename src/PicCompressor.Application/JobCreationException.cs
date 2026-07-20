using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed class JobCreationException(
    CompressionErrorCategory category,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public CompressionErrorCategory Category { get; } = category;
}

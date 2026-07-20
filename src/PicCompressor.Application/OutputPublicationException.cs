using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed class OutputPublicationException(
    CompressionErrorCategory category,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public CompressionErrorCategory Category { get; } = category;
}

namespace PicCompressor.Domain;

public enum CompressionErrorCategory
{
    InvalidArguments,
    InputNotFound,
    UnsupportedInput,
    LimitExceeded,
    EngineUnavailable,
    EngineFailed,
    OutputValidationFailed,
    OutputConflict,
    FileSystemError,
    Canceled,
    Unexpected
}

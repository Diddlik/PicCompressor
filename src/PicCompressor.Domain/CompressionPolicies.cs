namespace PicCompressor.Domain;

public enum CollisionPolicy
{
    Skip,
    Rename,
    Overwrite
}

public enum LargerOutputPolicy
{
    Discard,
    Keep
}

public enum ExifPolicy
{
    Keep,
    Private,
    Remove
}

public enum ColorProfilePolicy
{
    Preserve,
    Srgb,
    Remove
}

public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public static RgbColor White { get; } = new(255, 255, 255);
}

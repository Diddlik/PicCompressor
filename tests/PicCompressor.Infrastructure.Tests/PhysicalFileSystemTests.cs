namespace PicCompressor.Infrastructure.Tests;

public sealed class PhysicalFileSystemTests
{
    [Fact]
    public void GetCanonicalPath_returns_fully_qualified_path()
    {
        var fileSystem = new PhysicalFileSystem(StringComparer.Ordinal);

        var result = fileSystem.GetCanonicalPath(".");

        Assert.True(Path.IsPathFullyQualified(result));
    }

    [Fact]
    public void PathsEqual_uses_configured_platform_comparer()
    {
        var fileSystem = new PhysicalFileSystem(StringComparer.OrdinalIgnoreCase);

        Assert.True(fileSystem.PathsEqual("IMAGE.JPG", "image.jpg"));
    }

    [Fact]
    public void CreateTemporaryFile_reserves_unique_file_in_target_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PicCompressor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var target = Path.Combine(directory, "output.jpg");
            var fileSystem = new PhysicalFileSystem(StringComparer.OrdinalIgnoreCase);

            var first = fileSystem.CreateTemporaryFile(target);
            var second = fileSystem.CreateTemporaryFile(target);

            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.NotEqual(first, second);
            Assert.Equal(directory, Path.GetDirectoryName(first));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CreateTemporaryFile_creates_missing_target_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PicCompressor-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "nested", "output.jpg");

        try
        {
            var temporary = new PhysicalFileSystem(StringComparer.OrdinalIgnoreCase)
                .CreateTemporaryFile(target);

            Assert.True(File.Exists(temporary));
            Assert.Equal(Path.Combine(root, "nested"), Path.GetDirectoryName(temporary));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}

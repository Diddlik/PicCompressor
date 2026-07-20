using PicCompressor.Application;
using System.Security.Cryptography;

namespace PicCompressor.Infrastructure;

public sealed class PhysicalFileSystem(StringComparer pathComparer) : IOutputFileSystem
{
    public string GetCanonicalPath(string path) => Path.GetFullPath(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool PathsEqual(string left, string right) => pathComparer.Equals(left, right);

    public string CreateTemporaryFile(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("Target path has no directory.", nameof(targetPath));
        Directory.CreateDirectory(directory);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var path = Path.Combine(
                directory,
                $".piccompressor-{RandomNumberGenerator.GetHexString(16)}.tmp");
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        throw new IOException("Could not reserve a unique temporary output file.");
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void MoveFile(string sourcePath, string targetPath, bool overwrite) =>
        File.Move(sourcePath, targetPath, overwrite);
}

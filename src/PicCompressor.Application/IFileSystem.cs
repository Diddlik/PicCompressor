namespace PicCompressor.Application;

public interface IFileSystem
{
    string GetCanonicalPath(string path);

    bool FileExists(string path);

    bool PathsEqual(string left, string right);
}

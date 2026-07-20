namespace PicCompressor.Application;

public interface IOutputFileSystem : IFileSystem
{
    string CreateTemporaryFile(string targetPath);

    void DeleteFile(string path);

    void MoveFile(string sourcePath, string targetPath, bool overwrite);
}

namespace PicCompressor.Application;

public interface IOutputFileSystem : IFileSystem
{
    string CreateTemporaryFile(string targetPath);

    void DeleteFile(string path);

    void MoveFile(string sourcePath, string targetPath, bool overwrite);

    /// <summary>Liest die Bytes einer reservierten temporären Ausgabe (für die Nachbearbeitung).</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>Ersetzt den Inhalt einer temporären Ausgabe vollständig.</summary>
    void WriteAllBytes(string path, byte[] bytes);
}

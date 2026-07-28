using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed class OutputPathPlanner(IFileSystem fileSystem)
{
    /// <param name="replaceable">
    /// Der Zielpfad ist eine in einem früheren Lauf selbst erzeugte und aufgezeichnete Ausgabe
    /// (MP-005). Sie darf ersetzt werden, ohne dass die Kollisionsrichtlinie das Überschreiben
    /// fremder Dateien erlaubt.
    /// </param>
    public string Plan(
        string desiredPath,
        CollisionPolicy policy,
        IReadOnlyCollection<string>? reservedPaths = null,
        bool replaceable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredPath);
        reservedPaths ??= [];

        var reserved = IsReserved(desiredPath, reservedPaths);
        var exists = fileSystem.FileExists(desiredPath);
        if (!reserved && (!exists || replaceable))
        {
            return desiredPath;
        }

        if (reserved || policy is CollisionPolicy.Skip)
        {
            if (policy is not CollisionPolicy.Rename)
            {
                throw Conflict();
            }
        }
        else if (policy is CollisionPolicy.Overwrite)
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath)!;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);

        for (var number = 1; number < int.MaxValue; number++)
        {
            var candidate = fileSystem.GetCanonicalPath(
                Path.Combine(directory, $"{name}_{number}{extension}"));
            if (!fileSystem.FileExists(candidate) && !IsReserved(candidate, reservedPaths))
            {
                return candidate;
            }
        }

        throw Conflict();
    }

    private bool IsReserved(string path, IEnumerable<string> reservedPaths) =>
        reservedPaths.Any(reserved => fileSystem.PathsEqual(path, reserved));

    private static JobCreationException Conflict() =>
        new(
            CompressionErrorCategory.OutputConflict,
            "Output path already exists or is reserved by another planned job.");
}

namespace PicCompressor.Infrastructure;

/// <summary>
/// Locations of the data both hosts share. Requirement 13.1 demands that GUI
/// and CLI use the same history database, so the path is resolved here instead
/// of in each composition root.
/// </summary>
public static class ApplicationDataPaths
{
    public static string ApplicationDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PicCompressor");

    public static string HistoryDatabasePath { get; } =
        Path.Combine(ApplicationDataDirectory, "history.db");
}

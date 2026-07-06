namespace YouTracker.Infrastructure.Storage;

/// <summary>Well-known on-disk locations for the app's persisted state.</summary>
public static class StoragePaths
{
    /// <summary>%APPDATA%/you-tracker (or the platform equivalent).</summary>
    public static string AppDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "you-tracker"
        );

    /// <summary>Creates the directory if it does not exist and returns it.</summary>
    public static string EnsureCreated(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}

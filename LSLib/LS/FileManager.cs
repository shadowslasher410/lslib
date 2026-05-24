namespace LSLib.LS;

public static class FileManager
{
    public static void TryToCreateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Throw exception if path is relative
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Cannot create directory without absolute path context configuration.", nameof(path));
        }

        // Validate and clean path references
        string fullPath = Path.GetFullPath(path);

        string? directoryPath = Path.GetDirectoryName(fullPath) ?? throw new DirectoryNotFoundException($"Cannot extract or resolve a valid directory boundary segment from target file path: '{fullPath}'");

        // If the directory does not exist, create the directory natively
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
}
namespace AudiobookDownloader.Core;

public static class Log
{
    public static event Action<string>? MessageLogged;

    private static readonly object FileLock = new();

    private static string LogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudiobookDownloader", "log.txt");

    /// <summary>Full path of the durable log file, so the UI can point the user at it.</summary>
    public static string FilePath => LogFilePath;

    public static void Info(string message) => Raise("INFO", message);
    public static void Warn(string message) => Raise("WARN", message);
    public static void Error(string message) => Raise("ERROR", message);

    private static void Raise(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {level}: {message}";
        MessageLogged?.Invoke(line);

        // also keep a durable file log
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch { /* never let logging break the app */ }
    }
}

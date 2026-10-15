namespace KizaTweaks.Core;

/// <summary>Registro simple a archivo (%ProgramData%\KizaTweaks\logs) y evento para la interfaz.</summary>
public static class Log
{
    private static readonly object Lock = new();
    public static string? Directory { get; set; }
    public static event Action<string>? Written;

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write("ERROR", ex is null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}";
        try
        {
            if (Directory is not null)
            {
                lock (Lock)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    File.AppendAllText(Path.Combine(Directory, $"kizatweaks-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
                }
            }
        }
        catch
        {
            // El log nunca debe tumbar la app.
        }
        Written?.Invoke(line);
    }
}

public static class AppPaths
{
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KizaTweaks");

    public static string StateDir => Path.Combine(DataDir, "state");
    public static string LogsDir => Path.Combine(DataDir, "logs");
    public static string SnapshotsDir => Path.Combine(DataDir, "snapshots");
    public static string ToolsDir => Path.Combine(DataDir, "tools");
    public static string BackupsFile => Path.Combine(StateDir, "backups.json");
    public static string HistoryFile => Path.Combine(StateDir, "history.jsonl");
    public static string SettingsFile => Path.Combine(StateDir, "settings.json");
    public static string FpsResultsFile => Path.Combine(StateDir, "fps-results.json");
}

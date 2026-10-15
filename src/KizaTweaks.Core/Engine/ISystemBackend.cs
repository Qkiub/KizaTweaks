namespace KizaTweaks.Core.Engine;

public sealed record CommandResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Todo lo que el motor de tweaks necesita del sistema. Existe una implementación real
/// (WindowsBackend) y una en memoria para pruebas (InMemoryBackend).
/// </summary>
public interface ISystemBackend
{
    // ---- Registro ----
    RegValue? GetValue(string hive, string path, string name);
    void SetValue(string hive, string path, string name, RegValue value);
    void DeleteValue(string hive, string path, string name);
    bool KeyExists(string hive, string path);
    bool KeyIsEmpty(string hive, string path);
    void CreateKey(string hive, string path);
    void DeleteKey(string hive, string path);
    RegKeySnapshot? ExportKey(string hive, string path);
    void ImportKey(string hive, string path, RegKeySnapshot snapshot);

    // ---- Servicios ----
    /// <summary>Automatic | AutomaticDelayed | Manual | Disabled | Boot | System; null si no existe.</summary>
    string? GetServiceStartType(string name);
    void SetServiceStartType(string name, string startType);

    // ---- Tareas programadas ----
    /// <summary>null si la tarea no existe.</summary>
    bool? GetTaskEnabled(string path);
    void SetTaskEnabled(string path, bool enabled);

    // ---- Variables de entorno de máquina ----
    string? GetMachineEnv(string name);
    void SetMachineEnv(string name, string? value);

    // ---- Características opcionales de Windows ----
    /// <summary>null si la característica no existe en este Windows.</summary>
    bool? GetFeatureEnabled(string name);
    void SetFeatureEnabled(string name, bool enabled);

    // ---- Energía ----
    string? GetActivePowerScheme();
    /// <summary>Devuelve el GUID del plan (balanced|high|ultimate). Si create es true y no existe, lo crea.</summary>
    string? ResolvePowerScheme(string kind, bool create);
    void SetActivePowerScheme(string guid);

    // ---- Comandos ----
    CommandResult Run(string file, string args, int timeoutMs = 120_000);
}

using System.Runtime.Versioning;
using KizaTweaks.Core.Engine;

namespace KizaTweaks.Core.Processes;

public sealed class StartupItem
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Location { get; init; } = "";
    public bool Enabled { get; set; }
    public string ApprovedHive { get; init; } = "";
    public string ApprovedPath { get; init; } = "";
    /// <summary>Nombre del valor en StartupApproved (para la carpeta Inicio es el nombre del archivo).</summary>
    public string ApprovedName { get; init; } = "";
}

/// <summary>
/// Apps que arrancan con Windows. Se activan/desactivan igual que el Administrador de tareas
/// (claves StartupApproved), así que es totalmente reversible y no borra nada.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupService
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private readonly ISystemBackend _b;
    public StartupService(ISystemBackend backend) => _b = backend;

    public List<StartupItem> List()
    {
        var items = new List<StartupItem>();
        AddRunKey(items, "HKCU", Run, "HKCU", $@"{Approved}\Run", "Usuario (registro)");
        AddRunKey(items, "HKLM", Run, "HKLM", $@"{Approved}\Run", "Todos los usuarios (registro)");
        AddRunKey(items, "HKLM", RunWow, "HKLM", $@"{Approved}\Run32", "Todos los usuarios (32 bits)");
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HKCU", "Carpeta Inicio (usuario)");
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "HKLM", "Carpeta Inicio (todos)");
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void AddRunKey(List<StartupItem> items, string hive, string path, string apHive, string apPath, string label)
    {
        var snap = _b.ExportKey(hive, path);
        if (snap is null) return;
        foreach (var (name, val) in snap.Values)
        {
            if (string.IsNullOrEmpty(name)) continue;
            items.Add(new StartupItem
            {
                Name = name,
                Command = val.Text ?? val.ToString(),
                Location = label,
                ApprovedHive = apHive,
                ApprovedPath = apPath,
                ApprovedName = name,
                Enabled = IsEnabled(apHive, apPath, name),
            });
        }
    }

    private void AddFolder(List<StartupItem> items, string folder, string apHive, string label)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder))
        {
            var fn = Path.GetFileName(file);
            if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new StartupItem
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Command = file,
                Location = label,
                ApprovedHive = apHive,
                ApprovedPath = $@"{Approved}\StartupFolder",
                ApprovedName = fn,
                Enabled = IsEnabled(apHive, $@"{Approved}\StartupFolder", fn),
            });
        }
    }

    /// <summary>Byte 0 par (02, 06…) = activado; impar (03…) = desactivado. Sin valor = activado.</summary>
    private bool IsEnabled(string hive, string path, string name)
    {
        var v = _b.GetValue(hive, path, name);
        if (v?.Bytes is not { Length: > 0 } bytes) return true;
        return (bytes[0] & 1) == 0;
    }

    public void SetEnabled(StartupItem item, bool enabled)
    {
        var data = new byte[12];
        if (enabled)
        {
            data[0] = 0x02;
        }
        else
        {
            data[0] = 0x03;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }
        _b.SetValue(item.ApprovedHive, item.ApprovedPath, item.ApprovedName, RegValue.Bin(data));
        item.Enabled = enabled;
        Log.Info($"Inicio: {item.Name} → {(enabled ? "activado" : "desactivado")}");
    }
}

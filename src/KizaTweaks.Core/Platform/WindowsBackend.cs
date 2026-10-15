using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using KizaTweaks.Core.Engine;
using Microsoft.Win32;

namespace KizaTweaks.Core.Platform;

/// <summary>Implementación real sobre Windows: registro, servicios, tareas, energía, etc.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBackend : ISystemBackend
{
    // GUIDs de planes de energía de Windows
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimateSourceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    // GUIDs fijos de KizaTweaks para las copias de los planes ocultos (así no se crean duplicados)
    public const string KizaHighGuid = "6b2d4f1a-5c3e-4a7b-9d8e-1f2a3b4c5d61";
    public const string KizaUltimateGuid = "6b2d4f1a-5c3e-4a7b-9d8e-1f2a3b4c5d62";

    private static readonly Regex GuidRx = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    // =====================================================================
    // Registro
    // =====================================================================

    private static RegistryKey Base(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
        "HKLM" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        "HKCR" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64),
        "HKU" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64),
        _ => throw new ArgumentException($"Hive desconocido: {hive}"),
    };

    public RegValue? GetValue(string hive, string path, string name)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path, writable: false);
        if (k is null) return null;
        if (!k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        return Read(k, name);
    }

    private static RegValue? Read(RegistryKey k, string name)
    {
        var kind = k.GetValueKind(name);
        var raw = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return kind switch
        {
            RegistryValueKind.String => RegValue.Str(raw as string ?? ""),
            RegistryValueKind.ExpandString => RegValue.Expand(raw as string ?? ""),
            RegistryValueKind.MultiString => RegValue.MultiStr(raw as string[] ?? Array.Empty<string>()),
            RegistryValueKind.DWord => RegValue.Dword(unchecked((uint)Convert.ToInt32(raw))),
            RegistryValueKind.QWord => RegValue.Qword(Convert.ToInt64(raw)),
            RegistryValueKind.Binary => RegValue.Bin(raw as byte[] ?? Array.Empty<byte>()),
            _ => new RegValue { Kind = RegKind.None, Bytes = raw as byte[] ?? Array.Empty<byte>() },
        };
    }

    public void SetValue(string hive, string path, string name, RegValue value)
    {
        using var b = Base(hive);
        using var k = b.CreateSubKey(path, writable: true)
                      ?? throw new InvalidOperationException($"No se pudo abrir {hive}\\{path}");
        Write(k, name, value);
    }

    private static void Write(RegistryKey k, string name, RegValue v)
    {
        switch (v.Kind)
        {
            case RegKind.String: k.SetValue(name, v.Text ?? "", RegistryValueKind.String); break;
            case RegKind.ExpandString: k.SetValue(name, v.Text ?? "", RegistryValueKind.ExpandString); break;
            case RegKind.MultiString: k.SetValue(name, v.Multi ?? Array.Empty<string>(), RegistryValueKind.MultiString); break;
            case RegKind.DWord: k.SetValue(name, unchecked((int)(uint)(v.Number & 0xFFFFFFFFL)), RegistryValueKind.DWord); break;
            case RegKind.QWord: k.SetValue(name, v.Number, RegistryValueKind.QWord); break;
            case RegKind.Binary: k.SetValue(name, v.Bytes ?? Array.Empty<byte>(), RegistryValueKind.Binary); break;
            default: k.SetValue(name, v.Bytes ?? Array.Empty<byte>(), RegistryValueKind.None); break;
        }
    }

    public void DeleteValue(string hive, string path, string name)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool KeyExists(string hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path, writable: false);
        return k is not null;
    }

    public bool KeyIsEmpty(string hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path, writable: false);
        return k is null || (k.ValueCount == 0 && k.SubKeyCount == 0);
    }

    public void CreateKey(string hive, string path)
    {
        using var b = Base(hive);
        b.CreateSubKey(path, writable: true)?.Dispose();
    }

    public void DeleteKey(string hive, string path)
    {
        using var b = Base(hive);
        b.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    public RegKeySnapshot? ExportKey(string hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path, writable: false);
        return k is null ? null : Snapshot(k, depth: 0);
    }

    private static RegKeySnapshot Snapshot(RegistryKey k, int depth)
    {
        var snap = new RegKeySnapshot();
        foreach (var n in k.GetValueNames())
        {
            try { if (Read(k, n) is { } v) snap.Values[n] = v; } catch { /* valor ilegible */ }
        }
        if (depth > 16) return snap;
        foreach (var sub in k.GetSubKeyNames())
        {
            try
            {
                using var sk = k.OpenSubKey(sub, writable: false);
                if (sk is not null) snap.SubKeys[sub] = Snapshot(sk, depth + 1);
            }
            catch { /* sin permisos */ }
        }
        return snap;
    }

    public void ImportKey(string hive, string path, RegKeySnapshot snapshot)
    {
        using var b = Base(hive);
        using var k = b.CreateSubKey(path, writable: true)
                      ?? throw new InvalidOperationException($"No se pudo crear {hive}\\{path}");
        Import(k, snapshot);
    }

    private static void Import(RegistryKey k, RegKeySnapshot s)
    {
        foreach (var (name, v) in s.Values) Write(k, name, v);
        foreach (var (name, sub) in s.SubKeys)
        {
            using var sk = k.CreateSubKey(name, writable: true);
            if (sk is not null) Import(sk, sub);
        }
    }

    // =====================================================================
    // Servicios (se leen del registro; se cambian con sc.exe para que el SCM se entere al instante)
    // =====================================================================

    public string? GetServiceStartType(string name)
    {
        using var b = Base("HKLM");
        using var k = b.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", false);
        if (k?.GetValue("Start") is not int start) return null;
        var delayed = k.GetValue("DelayedAutostart") is int d && d == 1;
        return start switch
        {
            0 => "Boot",
            1 => "System",
            2 => delayed ? "AutomaticDelayed" : "Automatic",
            3 => "Manual",
            4 => "Disabled",
            _ => start.ToString(),
        };
    }

    public void SetServiceStartType(string name, string startType)
    {
        var sc = startType.ToLowerInvariant() switch
        {
            "automatic" or "auto" => "auto",
            "automaticdelayed" or "delayed-auto" => "delayed-auto",
            "manual" or "demand" => "demand",
            "disabled" => "disabled",
            _ => throw new ArgumentException($"Tipo de inicio no soportado: {startType}"),
        };
        var r = ProcessRunner.Run(ProcessRunner.System32("sc.exe"), $"config \"{name}\" start= {sc}");
        if (!r.Ok) throw new InvalidOperationException($"sc config {name} falló ({r.ExitCode}): {r.Output.Trim()}");
        if (sc == "disabled")
            ProcessRunner.Run(ProcessRunner.System32("sc.exe"), $"stop \"{name}\"", 30_000);
    }

    // =====================================================================
    // Tareas programadas (COM Schedule.Service: no depende del idioma de Windows)
    // =====================================================================

    private static (string folder, string name) SplitTask(string path)
    {
        var i = path.LastIndexOf('\\');
        var folder = i <= 0 ? "\\" : path[..i];
        return (folder, path[(i + 1)..]);
    }

    private static dynamic? OpenTask(string path)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return null;
        dynamic svc = Activator.CreateInstance(type)!;
        svc.Connect();
        var (folder, name) = SplitTask(path);
        try
        {
            dynamic f = svc.GetFolder(folder);
            return f.GetTask(name);
        }
        catch (Exception ex) when (ex is COMException or System.Reflection.TargetInvocationException or FileNotFoundException or DirectoryNotFoundException)
        {
            return null; // no existe
        }
    }

    public bool? GetTaskEnabled(string path)
    {
        var t = OpenTask(path);
        return t is null ? null : (bool)t.Enabled;
    }

    public void SetTaskEnabled(string path, bool enabled)
    {
        var t = OpenTask(path) ?? throw new InvalidOperationException($"La tarea {path} no existe");
        t.Enabled = enabled;
    }

    // =====================================================================
    // Variables de entorno
    // =====================================================================

    public string? GetMachineEnv(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);

    public void SetMachineEnv(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, string.IsNullOrEmpty(value) ? null : value, EnvironmentVariableTarget.Machine);

    // =====================================================================
    // Características opcionales
    // =====================================================================

    public bool? GetFeatureEnabled(string name)
    {
        var safe = name.Replace("'", "");
        var rows = Wmi.Query(@"root\cimv2", $"SELECT InstallState FROM Win32_OptionalFeature WHERE Name='{safe}'", "InstallState");
        if (rows.Count == 0) return null;
        return rows[0].Long("InstallState") switch
        {
            1 => true,  // habilitada
            2 => false, // deshabilitada
            _ => null,  // ausente / desconocido
        };
    }

    public void SetFeatureEnabled(string name, bool enabled)
    {
        var args = enabled
            ? $"/Online /Enable-Feature /FeatureName:{name} /All /NoRestart /Quiet"
            : $"/Online /Disable-Feature /FeatureName:{name} /NoRestart /Quiet";
        var r = ProcessRunner.Run(ProcessRunner.System32("dism.exe"), args, 600_000);
        // 3010 = éxito, requiere reinicio
        if (r.ExitCode != 0 && r.ExitCode != 3010)
            throw new InvalidOperationException($"DISM terminó con código {r.ExitCode}");
    }

    // =====================================================================
    // Energía
    // =====================================================================

    public string? GetActivePowerScheme()
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), "/getactivescheme");
        var m = GuidRx.Match(r.Output);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private static HashSet<string> ListSchemes()
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), "/list");
        return GuidRx.Matches(r.Output).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
    }

    public string? ResolvePowerScheme(string kind, bool create)
    {
        var list = ListSchemes();
        switch (kind.ToLowerInvariant())
        {
            case "balanced":
                return list.Contains(BalancedGuid) ? BalancedGuid : list.FirstOrDefault();
            case "high":
                if (list.Contains(HighPerfGuid)) return HighPerfGuid;
                if (list.Contains(KizaHighGuid)) return KizaHighGuid;
                return create ? Duplicate(HighPerfGuid, KizaHighGuid) : null;
            case "ultimate":
                if (list.Contains(KizaUltimateGuid)) return KizaUltimateGuid;
                return create ? Duplicate(UltimateSourceGuid, KizaUltimateGuid) : null;
            default:
                return null;
        }
    }

    private static string? Duplicate(string source, string dest)
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), $"-duplicatescheme {source} {dest}");
        if (!r.Ok) Log.Warn($"powercfg -duplicatescheme {source} falló: {r.Output.Trim()}");
        return ListSchemes().Contains(dest) ? dest : null;
    }

    public void SetActivePowerScheme(string guid)
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), $"/setactive {guid}");
        if (!r.Ok) throw new InvalidOperationException($"powercfg /setactive falló: {r.Output.Trim()}");
    }

    public uint? GetPowerSetting(string scheme, string subGroup, string setting)
    {
        var sc = Guid.Parse(scheme); var sg = Guid.Parse(subGroup); var st = Guid.Parse(setting);
        return PowerNative.PowerReadACValueIndex(IntPtr.Zero, ref sc, ref sg, ref st, out var v) == 0 ? v : null;
    }

    public void SetPowerSetting(string scheme, string subGroup, string setting, uint value, bool dc)
    {
        var sc = Guid.Parse(scheme); var sg = Guid.Parse(subGroup); var st = Guid.Parse(setting);
        var err = PowerNative.PowerWriteACValueIndex(IntPtr.Zero, ref sc, ref sg, ref st, value);
        if (err != 0) throw new InvalidOperationException($"No se pudo escribir el ajuste de energía (código {err}).");
        if (dc) PowerNative.PowerWriteDCValueIndex(IntPtr.Zero, ref sc, ref sg, ref st, value);
        // Los cambios en el plan activo se aplican al volver a activarlo.
        if (string.Equals(GetActivePowerScheme(), scheme, StringComparison.OrdinalIgnoreCase))
            PowerNative.PowerSetActiveScheme(IntPtr.Zero, ref sc);
    }

    private static class PowerNative
    {
        [System.Runtime.InteropServices.DllImport("powrprof.dll")]
        public static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [System.Runtime.InteropServices.DllImport("powrprof.dll")]
        public static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [System.Runtime.InteropServices.DllImport("powrprof.dll")]
        public static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [System.Runtime.InteropServices.DllImport("powrprof.dll")]
        public static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    }

    // =====================================================================
    // Comandos
    // =====================================================================

    public CommandResult Run(string file, string args, int timeoutMs = 120_000)
    {
        var path = Path.IsPathRooted(file) ? file : ProcessRunner.System32(file);
        if (!File.Exists(path)) path = file; // se busca en PATH
        return ProcessRunner.Run(path, args, timeoutMs);
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using KizaTweaks.Core.Platform;

namespace KizaTweaks.Core.Maintenance;

public sealed class MaintenanceResult
{
    public bool Ok { get; init; } = true;
    public string Summary { get; init; } = "";
    public long BytesFreed { get; init; }
}

public sealed class MaintenanceContext
{
    public required Action<string> Output { get; init; }
    public CancellationToken Cancel { get; init; }
}

public sealed class MaintenanceTask
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>disk | cleanup | repair | network | tools</summary>
    public required string Group { get; init; }
    /// <summary>Comando que se ejecuta, para mostrarlo tal cual en la interfaz.</summary>
    public required string Command { get; init; }
    public string Duration { get; init; } = "menos de 1 min";
    /// <summary>safe | moderate</summary>
    public string Risk { get; init; } = "safe";
    public string? Note { get; init; }
    public bool RequiresReboot { get; init; }
    public bool InQuick { get; init; }
    public bool InFull { get; init; }
    public required Func<MaintenanceContext, Task<MaintenanceResult>> Run { get; init; }
}

/// <summary>
/// Mantenimiento del sistema con las herramientas propias de Windows (defrag, chkdsk, DISM, sfc,
/// ipconfig, netsh, winget) y limpieza de archivos temporales hecha con cuidado: nunca borra fuera
/// de las carpetas previstas, salta archivos en uso y reporta cuánto liberó.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MaintenanceService
{
    public static readonly IReadOnlyList<(string Id, string Name)> Groups = new[]
    {
        ("disk", "Disco"),
        ("cleanup", "Limpieza"),
        ("network", "Red"),
        ("repair", "Reparación (solo si hay problemas)"),
        ("tools", "Programas y diagnóstico"),
    };

    public IReadOnlyList<MaintenanceTask> Tasks { get; }

    public MaintenanceService() => Tasks = Build();

    // =====================================================================
    // Catálogo
    // =====================================================================

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private List<MaintenanceTask> Build() => new()
    {
        // ---------------- Disco ----------------
        new MaintenanceTask
        {
            Id = "retrim", Group = "disk", InQuick = true, InFull = true,
            Name = "ReTRIM de SSD",
            Description = "Le indica al SSD qué espacio está libre para que mantenga su velocidad de escritura. En discos mecánicos se omite.",
            Command = "defrag <unidad>: /L",
            Run = async c => await PerVolume(c, "/L /U /V", "ReTRIM"),
        },
        new MaintenanceTask
        {
            Id = "optimize", Group = "disk", InFull = true,
            Name = "Optimizar unidades",
            Description = "Hace lo correcto según el tipo de disco: TRIM en SSD y desfragmentación en discos mecánicos (HDD).",
            Command = "defrag <unidad>: /O",
            Duration = "1 min (SSD) a 1 h (HDD muy fragmentado)",
            Run = async c => await PerVolume(c, "/O /U /V", "Optimización"),
        },
        new MaintenanceTask
        {
            Id = "analyze", Group = "disk",
            Name = "Analizar fragmentación",
            Description = "Solo revisa qué tan fragmentado está cada disco, sin cambiar nada.",
            Command = "defrag <unidad>: /A",
            Run = async c => await PerVolume(c, "/A /V", "Análisis"),
        },
        new MaintenanceTask
        {
            Id = "chkdsk", Group = "disk", InFull = true,
            Name = "Revisar errores del disco",
            Description = "Busca errores del sistema de archivos sin reiniciar ni bloquear el disco. Si encuentra algo, te dirá cómo repararlo.",
            Command = "chkdsk <unidad>: /scan",
            Duration = "1 a 10 min por disco",
            Run = async c => await ChkdskScan(c),
        },
        new MaintenanceTask
        {
            Id = "trim-on", Group = "disk", InQuick = true, InFull = true,
            Name = "Asegurar que TRIM esté activo",
            Description = "Activa TRIM para los SSD si estaba apagado (viene activo por defecto).",
            Command = "fsutil behavior set DisableDeleteNotify 0",
            Run = async c => await Cmd(c, "fsutil.exe", "behavior set DisableDeleteNotify 0", "TRIM"),
        },

        // ---------------- Limpieza ----------------
        new MaintenanceTask
        {
            Id = "temp", Group = "cleanup", InQuick = true, InFull = true,
            Name = "Archivos temporales",
            Description = "Borra temporales de tu usuario y de Windows con más de 24 horas. Los que están en uso se saltan.",
            Command = "del %TEMP%\\* y %WINDIR%\\Temp\\* (más de 24 h)",
            Run = c => Task.FromResult(CleanMany(c, "Temporales", TimeSpan.FromHours(24),
                Path.GetTempPath(), Path.Combine(WinDir, "Temp"))),
        },
        new MaintenanceTask
        {
            Id = "wer", Group = "cleanup", InQuick = true, InFull = true,
            Name = "Reportes de errores y volcados",
            Description = "Borra informes de errores de Windows y volcados de memoria de apps que se cerraron.",
            Command = "del %ProgramData%\\Microsoft\\Windows\\WER\\Report*  y  %LOCALAPPDATA%\\CrashDumps",
            Run = c => Task.FromResult(CleanMany(c, "Reportes de errores", null,
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
                Path.Combine(LocalAppData, "CrashDumps"))),
        },
        new MaintenanceTask
        {
            Id = "do-cache", Group = "cleanup", InQuick = true, InFull = true,
            Name = "Caché de optimización de distribución",
            Description = "Borra las actualizaciones que Windows guarda para compartir con otros PCs.",
            Command = "Delete-DeliveryOptimizationCache -Force",
            Run = async c =>
            {
                c.Output("> Delete-DeliveryOptimizationCache -Force");
                var r = await Task.Run(() => ProcessRunner.PowerShell("Delete-DeliveryOptimizationCache -Force -ErrorAction Stop; 'KIZA_OK'"));
                foreach (var l in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (l != "KIZA_OK") c.Output(l);
                var ok = r.Output.Contains("KIZA_OK");
                return new MaintenanceResult { Ok = ok, Summary = ok ? "Caché de distribución limpia" : "No se pudo limpiar la caché de distribución" };
            },
        },
        new MaintenanceTask
        {
            Id = "wu-cache", Group = "cleanup", InFull = true,
            Name = "Caché de Windows Update",
            Description = "Borra instaladores de actualizaciones ya descargados. Útil también cuando Windows Update se queda atascado.",
            Command = "net stop wuauserv & net stop bits & del %WINDIR%\\SoftwareDistribution\\Download\\* & net start bits & net start wuauserv",
            Duration = "1 a 3 min",
            Run = async c =>
            {
                await Cmd(c, "net.exe", "stop wuauserv /y", "Detener Windows Update", quiet: true);
                await Cmd(c, "net.exe", "stop bits /y", "Detener BITS", quiet: true);
                try
                {
                    return CleanMany(c, "Caché de Windows Update", null, Path.Combine(WinDir, "SoftwareDistribution", "Download"));
                }
                finally
                {
                    await Cmd(c, "net.exe", "start bits", "Iniciar BITS", quiet: true);
                    await Cmd(c, "net.exe", "start wuauserv", "Iniciar Windows Update", quiet: true);
                }
            },
        },
        new MaintenanceTask
        {
            Id = "components", Group = "cleanup", InFull = true,
            Name = "Limpiar componentes de Windows",
            Description = "Elimina versiones antiguas de componentes reemplazadas por actualizaciones (lo mismo que hace Windows en segundo plano, pero ya).",
            Command = "DISM /Online /Cleanup-Image /StartComponentCleanup",
            Duration = "5 a 20 min",
            Run = async c => await Cmd(c, "dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", "Limpieza de componentes", okCodes: new[] { 0, 3010 }),
        },
        new MaintenanceTask
        {
            Id = "recycle", Group = "cleanup",
            Name = "Vaciar papelera de reciclaje",
            Description = "Vacía la papelera de todas las unidades.",
            Command = "rd /s /q <unidad>:\\$Recycle.Bin",
            Risk = "moderate",
            Note = "Lo que esté en la papelera se pierde definitivamente.",
            Run = c => Task.FromResult(EmptyRecycleBin(c)),
        },
        new MaintenanceTask
        {
            Id = "shader", Group = "cleanup",
            Name = "Caché de sombreadores (shaders)",
            Description = "Borra los shaders compilados de DirectX y de los drivers de video. Útil tras actualizar el driver o si un juego tiene errores gráficos.",
            Command = "del %LOCALAPPDATA%\\D3DSCache, NVIDIA\\DXCache, AMD\\DxCache…",
            Risk = "moderate",
            Note = "Las primeras partidas después pueden tener tirones mientras los juegos recompilan sus shaders.",
            Run = c => Task.FromResult(CleanMany(c, "Caché de shaders", null,
                Path.Combine(LocalAppData, "D3DSCache"),
                Path.Combine(LocalAppData, "NVIDIA", "DXCache"),
                Path.Combine(LocalAppData, "NVIDIA", "GLCache"),
                Path.Combine(LocalAppData, "AMD", "DxCache"),
                Path.Combine(LocalAppData, "AMD", "DxcCache"),
                Path.Combine(LocalAppData, "AMD", "VkCache"),
                Path.Combine(LocalAppData, "Intel", "ShaderCache"))),
        },

        // ---------------- Red ----------------
        new MaintenanceTask
        {
            Id = "dns", Group = "network", InQuick = true, InFull = true,
            Name = "Limpiar caché DNS",
            Description = "Borra direcciones guardadas de sitios y servidores. Ayuda cuando una página o servidor de juego no carga.",
            Command = "ipconfig /flushdns",
            Run = async c => await Cmd(c, "ipconfig.exe", "/flushdns", "Caché DNS"),
        },
        new MaintenanceTask
        {
            Id = "net-reset", Group = "network",
            Name = "Restablecer red",
            Description = "Reinicia la pila de red de Windows (Winsock y TCP/IP). Para cuando internet falla y nada más funciona.",
            Command = "netsh winsock reset & netsh int ip reset",
            Risk = "moderate", RequiresReboot = true,
            Note = "Las VPN y algunos programas de red pueden necesitar reinstalarse después.",
            Run = async c =>
            {
                var a = await Cmd(c, "netsh.exe", "winsock reset", "Winsock");
                var b = await Cmd(c, "netsh.exe", "int ip reset", "TCP/IP");
                return new MaintenanceResult { Ok = a.Ok && b.Ok, Summary = "Red restablecida (reinicia para terminar)" };
            },
        },

        // ---------------- Reparación ----------------
        new MaintenanceTask
        {
            Id = "dism-restore", Group = "repair",
            Name = "Reparar imagen de Windows (DISM)",
            Description = "Descarga y reemplaza componentes de Windows dañados. Ejecútalo antes de SFC si hay cierres o errores raros.",
            Command = "DISM /Online /Cleanup-Image /RestoreHealth",
            Duration = "10 a 30 min",
            Run = async c => await Cmd(c, "dism.exe", "/Online /Cleanup-Image /RestoreHealth", "Reparación DISM", okCodes: new[] { 0, 3010 }),
        },
        new MaintenanceTask
        {
            Id = "sfc", Group = "repair",
            Name = "Verificar archivos del sistema (SFC)",
            Description = "Revisa y repara archivos protegidos de Windows.",
            Command = "sfc /scannow",
            Duration = "10 a 20 min",
            Run = async c => await Cmd(c, "sfc.exe", "/scannow", "Verificación SFC", encoding: Encoding.Unicode),
        },
        new MaintenanceTask
        {
            Id = "memtest", Group = "repair",
            Name = "Diagnóstico de memoria RAM",
            Description = "Abre la herramienta de Windows que prueba la RAM al reiniciar. Útil si hay pantallazos azules.",
            Command = "mdsched.exe",
            RequiresReboot = true,
            Run = c =>
            {
                c.Output("> mdsched.exe");
                ProcessRunner.OpenShell(ProcessRunner.System32("mdsched.exe"));
                return Task.FromResult(new MaintenanceResult { Summary = "Herramienta de diagnóstico abierta" });
            },
        },

        // ---------------- Programas y diagnóstico ----------------
        new MaintenanceTask
        {
            Id = "winget-upgrade", Group = "tools",
            Name = "Actualizar todos los programas",
            Description = "Actualiza los programas instalados que tengan versión nueva disponible.",
            Command = "winget upgrade --all --silent",
            Duration = "varios minutos",
            Risk = "moderate",
            Note = "Cierra los programas antes. Algunos instaladores pueden reiniciar el programa o pedir confirmación.",
            Run = async c =>
            {
                var winget = Apps.AppService.WingetPath();
                if (winget is null)
                {
                    c.Output("WinGet no está disponible. Actualiza «Instalador de aplicación» desde la Microsoft Store.");
                    return new MaintenanceResult { Ok = false, Summary = "WinGet no disponible" };
                }
                return await Cmd(c, winget, "upgrade --all --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
                                 "Actualización de programas", encoding: Encoding.UTF8, anyCodeOk: true);
            },
        },
        new MaintenanceTask
        {
            Id = "battery", Group = "tools",
            Name = "Informe de batería (portátiles)",
            Description = "Genera un informe con la capacidad real de la batería frente a la de fábrica.",
            Command = "powercfg /batteryreport",
            Run = async c =>
            {
                var file = Path.Combine(AppPaths.DataDir, "informe-bateria.html");
                Directory.CreateDirectory(AppPaths.DataDir);
                var r = await Cmd(c, "powercfg.exe", $"/batteryreport /output \"{file}\"", "Informe de batería");
                if (r.Ok && File.Exists(file)) ProcessRunner.OpenShell(file);
                return r;
            },
        },
    };

    // =====================================================================
    // Ejecutores
    // =====================================================================

    /// <summary>Volúmenes fijos NTFS listos (C:, D:, E:…).</summary>
    public static List<string> FixedVolumes() =>
        DriveInfo.GetDrives()
                 .Where(d => d.DriveType == DriveType.Fixed && d.IsReady &&
                             d.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                 .Select(d => d.Name[..1].ToUpperInvariant())
                 .ToList();

    private static async Task<MaintenanceResult> Cmd(MaintenanceContext c, string file, string args, string label,
        int[]? okCodes = null, Encoding? encoding = null, bool quiet = false, bool anyCodeOk = false)
    {
        var path = Path.IsPathRooted(file) ? file : ProcessRunner.System32(file);
        c.Output($"> {Path.GetFileNameWithoutExtension(file)} {args}");
        var r = await Task.Run(() => ProcessRunner.RunStreaming(path, args, quiet ? _ => { } : c.Output, encoding: encoding, ct: c.Cancel));
        var ok = anyCodeOk || (okCodes ?? new[] { 0 }).Contains(r.ExitCode);
        Log.Info($"Mantenimiento: {label} → código {r.ExitCode}");
        return new MaintenanceResult { Ok = ok, Summary = ok ? $"{label}: listo" : $"{label}: terminó con código {r.ExitCode}" };
    }

    private static async Task<MaintenanceResult> PerVolume(MaintenanceContext c, string args, string label)
    {
        var vols = FixedVolumes();
        var parts = new List<string>();
        bool anyOk = false;
        foreach (var v in vols)
        {
            if (c.Cancel.IsCancellationRequested) break;
            c.Output($"> defrag {v}: {args}");
            var r = await Task.Run(() => ProcessRunner.RunStreaming(ProcessRunner.System32("defrag.exe"), $"{v}: {args}", c.Output, ct: c.Cancel));
            if (r.Ok) { anyOk = true; parts.Add($"{v}: ok"); }
            else parts.Add($"{v}: omitido");
        }
        return new MaintenanceResult { Ok = anyOk || vols.Count == 0, Summary = $"{label} ({string.Join(", ", parts)})" };
    }

    private static async Task<MaintenanceResult> ChkdskScan(MaintenanceContext c)
    {
        var problems = new List<string>();
        foreach (var v in FixedVolumes())
        {
            if (c.Cancel.IsCancellationRequested) break;
            c.Output($"> chkdsk {v}: /scan");
            var r = await Task.Run(() => ProcessRunner.RunStreaming(ProcessRunner.System32("chkdsk.exe"), $"{v}: /scan", c.Output, ct: c.Cancel));
            if (!r.Ok) problems.Add(v + ":");
        }
        return problems.Count == 0
            ? new MaintenanceResult { Summary = "Discos sin errores" }
            : new MaintenanceResult
            {
                Ok = false,
                Summary = $"Revisar {string.Join(" ", problems)}: ejecuta «chkdsk X: /f» en CMD como administrador (puede pedir reiniciar)",
            };
    }

    private static MaintenanceResult CleanMany(MaintenanceContext c, string label, TimeSpan? olderThan, params string[] dirs)
    {
        long bytes = 0; int files = 0, skipped = 0;
        foreach (var d in dirs.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (c.Cancel.IsCancellationRequested) break;
            c.Output($"> limpiar {d}");
            var r = Cleaner.CleanDirectory(d, olderThan, c.Cancel);
            bytes += r.Bytes; files += r.Files; skipped += r.Skipped;
        }
        var msg = $"{label}: {FormatBytes(bytes)} liberados ({files} archivos" + (skipped > 0 ? $", {skipped} en uso se saltaron)" : ")");
        c.Output(msg);
        Log.Info("Mantenimiento: " + msg);
        return new MaintenanceResult { Summary = msg, BytesFreed = bytes };
    }

    private static MaintenanceResult EmptyRecycleBin(MaintenanceContext c)
    {
        c.Output("> vaciar papelera de reciclaje");
        long size = 0;
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        if (SHQueryRecycleBin(null, ref info) == 0) size = info.i64Size;
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); // sin confirmación, sin progreso, sin sonido
        // 0x8000FFFF = la papelera ya estaba vacía
        var ok = hr == 0 || hr == unchecked((int)0x8000FFFF);
        var msg = ok ? $"Papelera vaciada: {FormatBytes(size)} liberados" : $"No se pudo vaciar la papelera (0x{hr:X8})";
        c.Output(msg);
        return new MaintenanceResult { Ok = ok, Summary = msg, BytesFreed = ok ? size : 0 };
    }

    public static string FormatBytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{b / 1024.0:0} KB",
        _ => $"{b} B",
    };

    [StructLayout(LayoutKind.Sequential)] // alineación natural de x64
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}

public sealed record CleanResult(long Bytes, int Files, int Skipped);

/// <summary>Borrado seguro de contenido de carpetas temporales. Lógica sin dependencias de Windows (se prueba en cualquier SO).</summary>
public static class Cleaner
{
    /// <summary>
    /// Borra el CONTENIDO de la carpeta (nunca la carpeta en sí). Salta enlaces/uniones, archivos en uso
    /// y, si se indica, archivos más nuevos que olderThan. Se niega a operar sobre carpetas críticas.
    /// </summary>
    public static CleanResult CleanDirectory(string dir, TimeSpan? olderThan, CancellationToken ct = default)
    {
        if (!IsSafeRoot(dir) || !Directory.Exists(dir)) return new CleanResult(0, 0, 0);
        var root = Path.GetFullPath(dir);
        var limit = olderThan is { } o ? DateTime.UtcNow - o : DateTime.MaxValue;
        long bytes = 0; int files = 0, skipped = 0;
        var dirs = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0 && !ct.IsCancellationRequested)
        {
            var current = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(current).ToList(); }
            catch { skipped++; continue; }
            foreach (var e in entries)
            {
                try
                {
                    var attr = File.GetAttributes(e);
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue; // no seguir enlaces ni uniones
                    if ((attr & FileAttributes.Directory) != 0)
                    {
                        stack.Push(e);
                        dirs.Add(e);
                        continue;
                    }
                    var fi = new FileInfo(e);
                    if (fi.LastWriteTimeUtc > limit) continue;
                    var len = fi.Length;
                    if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(e, attr & ~FileAttributes.ReadOnly);
                    File.Delete(e);
                    bytes += len;
                    files++;
                }
                catch { skipped++; }
            }
        }
        // Carpetas vacías, de la más profunda a la más externa (la raíz se conserva)
        foreach (var d in dirs.OrderByDescending(d => d.Length))
        {
            try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        }
        return new CleanResult(bytes, files, skipped);
    }

    /// <summary>Evita cualquier limpieza sobre raíces de disco o carpetas críticas del sistema/usuario.</summary>
    public static bool IsSafeRoot(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)); }
        catch { return false; }
        var root = Path.GetPathRoot(full);
        if (root is null || Path.TrimEndingDirectorySeparator(root).Equals(full, StringComparison.OrdinalIgnoreCase)) return false;
        if (full.Count(ch => ch == Path.DirectorySeparatorChar) < 2) return false; // p. ej. C:\Windows o /tmp
        var forbidden = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop,
        }.Select(f => Environment.GetFolderPath(f)).Where(p => !string.IsNullOrEmpty(p))
         .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)));
        return !forbidden.Any(p => p.Equals(full, StringComparison.OrdinalIgnoreCase));
    }
}

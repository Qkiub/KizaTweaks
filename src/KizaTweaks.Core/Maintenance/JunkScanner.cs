using KizaTweaks.Core.Platform;

namespace KizaTweaks.Core.Maintenance;

/// <summary>Un tipo de archivos que se pueden borrar sin afectar nada (Windows los recrea o son restos).</summary>
public sealed class JunkCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool DefaultChecked { get; init; } = true;
    /// <summary>Carpetas cuyo CONTENIDO se borra (la carpeta se conserva).</summary>
    public IReadOnlyList<string> Dirs { get; init; } = Array.Empty<string>();
    /// <summary>Carpetas completas que se borran (restos de instalaciones). Solo rutas de la lista fija.</summary>
    public IReadOnlyList<string> Trees { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Limpiadores del Liberador de espacio de Windows (cleanmgr) que borran las carpetas de Trees.
    /// Se usan para Windows.old y restos del instalador porque son de TrustedInstaller.
    /// </summary>
    public IReadOnlyList<string> CleanMgr { get; init; } = Array.Empty<string>();
    /// <summary>Archivos sueltos que se borran.</summary>
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
    public TimeSpan? OlderThan { get; init; }
    /// <summary>Tarea de Mantenimiento que hace la limpieza (cuando Windows tiene su propia herramienta).</summary>
    public string? TaskId { get; init; }
    /// <summary>Si es false no se mide el tamaño (DNS, TRIM, componentes).</summary>
    public bool Measurable { get; init; } = true;
}

/// <summary>
/// Revisa las carpetas de Windows y dice cuánto ocupa lo que se puede borrar: temporales, reportes,
/// cachés de actualizaciones, registros viejos, restos de instalación de Windows y de drivers.
/// Nunca toca documentos, descargas, escritorio, papelera, juegos ni cachés de shaders.
/// </summary>
public static class JunkScanner
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string SysDrive => Path.GetPathRoot(WinDir) is { Length: > 0 } r ? r : "C:\\";

    public static IReadOnlyList<JunkCategory> Categories() => new[]
    {
        new JunkCategory
        {
            Id = "temp", Name = "Archivos temporales",
            Description = "Temporales de tu usuario y de Windows con más de un día. Los que están en uso se saltan.",
            Dirs = new[] { Path.GetTempPath(), Path.Combine(WinDir, "Temp") }, OlderThan = TimeSpan.FromHours(24),
        },
        new JunkCategory
        {
            Id = "wu-cache", Name = "Descargas de Windows Update",
            Description = "Instaladores de actualizaciones que ya se instalaron.",
            Dirs = new[] { Path.Combine(WinDir, "SoftwareDistribution", "Download") }, TaskId = "wu-cache",
        },
        new JunkCategory
        {
            Id = "do-cache", Name = "Caché de optimización de distribución",
            Description = "Copias de actualizaciones que Windows guarda para compartir con otros PCs.",
            Dirs = new[] { Path.Combine(WinDir, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache") },
            TaskId = "do-cache",
        },
        new JunkCategory
        {
            Id = "wer", Name = "Reportes de errores",
            Description = "Informes que Windows guarda cuando una app se cierra sola.",
            Dirs = new[]
            {
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
                Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "Temp"),
                Path.Combine(Local, "CrashDumps"),
                Path.Combine(Local, "Microsoft", "Windows", "WER"),
            },
        },
        new JunkCategory
        {
            Id = "logs", Name = "Registros viejos de Windows",
            Description = "Archivos .log de instalación y actualizaciones con más de una semana.",
            Dirs = new[]
            {
                Path.Combine(WinDir, "Logs", "CBS"), Path.Combine(WinDir, "Logs", "DISM"), Path.Combine(WinDir, "Logs", "MoSetup"),
                Path.Combine(WinDir, "Logs", "WindowsUpdate"), Path.Combine(WinDir, "Panther"), Path.Combine(WinDir, "Logs", "waasmedic"),
                Path.Combine(WinDir, "Logs", "SIH"), Path.Combine(WinDir, "Logs", "NetSetup"),
            },
            OlderThan = TimeSpan.FromDays(7),
        },
        new JunkCategory
        {
            Id = "thumbs", Name = "Caché de miniaturas",
            Description = "Vistas previas de fotos y videos del Explorador. Se vuelven a crear solas.",
            Files = ThumbCacheFiles(),
        },
        new JunkCategory
        {
            Id = "setup", Name = "Restos de la instalación de Windows",
            Description = "Carpetas que deja el instalador ($Windows.~BT, $Windows.~WS) y archivos de actualizaciones de versión.",
            Trees = new[] { Path.Combine(SysDrive, "$Windows.~BT"), Path.Combine(SysDrive, "$Windows.~WS"), Path.Combine(SysDrive, "$WINDOWS.~Q"), Path.Combine(SysDrive, "$GetCurrent") },
            CleanMgr = new[] { "Temporary Setup Files", "Setup Log Files", "Windows Upgrade Log Files" },
        },
        new JunkCategory
        {
            Id = "drivers", Name = "Restos de instaladores de drivers",
            Description = "Archivos que NVIDIA, AMD e Intel descomprimen al instalar el driver. El driver instalado no se toca.",
            Trees = new[] { Path.Combine(SysDrive, "NVIDIA"), Path.Combine(SysDrive, "AMD") },
            Dirs = new[] { Path.Combine(ProgramData, "NVIDIA Corporation", "Downloader") },
        },
        new JunkCategory
        {
            Id = "dumps", Name = "Volcados de pantallazos azules",
            Description = "Archivos de memoria de cuelgues de Windows. Desmárcalo si quieres revisar por qué hubo un pantallazo.",
            Dirs = new[] { Path.Combine(WinDir, "Minidump"), Path.Combine(WinDir, "LiveKernelReports") },
            Files = new[] { Path.Combine(WinDir, "MEMORY.DMP") },
            DefaultChecked = false,
        },
        new JunkCategory
        {
            Id = "windows-old", Name = "Windows anterior (Windows.old)",
            Description = "Copia del Windows de antes de formatear. Si dejaste ahí archivos tuyos (Windows.old\\Users), sácalos antes de marcarla.",
            Trees = new[] { Path.Combine(SysDrive, "Windows.old") },
            CleanMgr = new[] { "Previous Installations" },
            DefaultChecked = false,
        },
        new JunkCategory
        {
            Id = "components", Name = "Componentes viejos de actualizaciones",
            Description = "Versiones reemplazadas de archivos de Windows (la misma limpieza que hace Windows solo). Tarda de 5 a 20 minutos.",
            TaskId = "components", Measurable = false, DefaultChecked = false,
        },
        new JunkCategory
        {
            Id = "dns", Name = "Caché DNS", Description = "Direcciones de páginas guardadas. Se vuelve a llenar sola.",
            TaskId = "dns", Measurable = false,
        },
        new JunkCategory
        {
            Id = "trim", Name = "TRIM del SSD", Description = "Le avisa al SSD qué espacio quedó libre para que siga rápido.",
            TaskId = "retrim", Measurable = false,
        },
    };

    private static string[] ThumbCacheFiles()
    {
        try
        {
            var dir = Path.Combine(Local, "Microsoft", "Windows", "Explorer");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "thumbcache_*.db") : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Rutas completas que se pueden borrar enteras. Cualquier otra se rechaza.</summary>
    public static bool IsAllowedTree(string path)
    {
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return false; }
        var parent = Path.GetDirectoryName(full);
        var root = Path.GetPathRoot(full);
        if (parent is null || root is null || !Path.TrimEndingDirectorySeparator(parent).Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            return false; // solo carpetas directamente en la raíz del disco
        var name = Path.GetFileName(full);
        return new[] { "NVIDIA", "AMD" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Cuánto ocupa la categoría (bytes). -1 si no se puede medir.</summary>
    public static long Measure(JunkCategory c, CancellationToken ct = default)
    {
        if (!c.Measurable) return -1;
        long total = 0;
        foreach (var d in c.Dirs) total += DirSize(d, c.OlderThan, ct);
        foreach (var t in c.Trees) total += DirSize(t, null, ct);
        foreach (var f in c.Files)
        {
            try { if (File.Exists(f)) total += new FileInfo(f).Length; } catch { }
        }
        return total;
    }

    /// <summary>Tamaño de una carpeta sin seguir enlaces ni uniones.</summary>
    public static long DirSize(string dir, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return 0;
        var limit = olderThan is { } o ? DateTime.UtcNow - o : DateTime.MaxValue;
        long total = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(dir));
        while (stack.Count > 0 && !ct.IsCancellationRequested)
        {
            var cur = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = cur.EnumerateFileSystemInfos().ToList(); }
            catch { continue; }
            foreach (var e in entries)
            {
                try
                {
                    if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (e is DirectoryInfo di) stack.Push(di);
                    else if (e is FileInfo fi && fi.LastWriteTimeUtc <= limit) total += fi.Length;
                }
                catch { }
            }
        }
        return total;
    }

    /// <summary>Borra los archivos de la categoría (sin las que usan una tarea de Mantenimiento). Devuelve bytes liberados.</summary>
    public static long CleanFiles(JunkCategory c, Action<string> output, CancellationToken ct = default)
    {
        long bytes = 0;
        foreach (var d in c.Dirs)
        {
            if (ct.IsCancellationRequested) break;
            var r = Cleaner.CleanDirectory(d, c.OlderThan, ct);
            bytes += r.Bytes;
        }
        foreach (var f in c.Files)
        {
            try
            {
                if (!File.Exists(f)) continue;
                var len = new FileInfo(f).Length;
                File.SetAttributes(f, FileAttributes.Normal);
                File.Delete(f);
                bytes += len;
            }
            catch { /* en uso */ }
        }
        if (c.CleanMgr.Count > 0)
        {
            var before = c.Trees.Sum(t => DirSize(t));
            if (before > 0) RunCleanMgr(c.CleanMgr, output);
            bytes += Math.Max(0, before - c.Trees.Sum(t => DirSize(t)));
        }
        else
        {
            foreach (var t in c.Trees)
            {
                if (ct.IsCancellationRequested) break;
                bytes += DeleteTree(t, output);
            }
        }
        return bytes;
    }

    /// <summary>Borra una carpeta de restos de drivers (C:\NVIDIA, C:\AMD). No sigue uniones.</summary>
    public static long DeleteTree(string path, Action<string> output)
    {
        if (!IsAllowedTree(path) || !Directory.Exists(path)) return 0;
        var before = DirSize(path);
        output($"> borrar {path}");
        try { Directory.Delete(path, recursive: true); }
        catch
        {
            Cleaner.CleanDirectory(path, null); // lo que no esté en uso
        }
        var after = Directory.Exists(path) ? DirSize(path) : 0;
        Log.Info($"Limpieza: {path} → {before - after} bytes liberados");
        return Math.Max(0, before - after);
    }

    /// <summary>Ejecuta el Liberador de espacio de Windows solo con los limpiadores indicados.</summary>
    private static void RunCleanMgr(IReadOnlyList<string> handlers, Action<string> output)
    {
        if (!OperatingSystem.IsWindows()) return;
        const string root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
        const string flag = "StateFlags0777";
        var set = new List<string>();
        try
        {
            foreach (var h in handlers)
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root + "\\" + h, writable: true);
                if (k is null) continue;
                k.SetValue(flag, 2, Microsoft.Win32.RegistryValueKind.DWord);
                set.Add(h);
            }
            if (set.Count == 0) return;
            output("> cleanmgr /sagerun:777 (" + string.Join(", ", set) + ")");
            var r = ProcessRunner.Run(ProcessRunner.System32("cleanmgr.exe"), "/sagerun:777", 3_600_000);
            Log.Info($"cleanmgr {string.Join(", ", set)} → código {r.ExitCode}");
        }
        catch (Exception ex) { Log.Warn("Liberador de espacio: " + ex.Message); }
        finally
        {
            foreach (var h in set)
            {
                try
                {
                    using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root + "\\" + h, writable: true);
                    k?.DeleteValue(flag, throwOnMissingValue: false);
                }
                catch { }
            }
        }
    }
}

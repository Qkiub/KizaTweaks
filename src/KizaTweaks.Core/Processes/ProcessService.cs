using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using KizaTweaks.Core.Engine;

namespace KizaTweaks.Core.Processes;

public enum ProcessKind { Protected, Windows, App }

public sealed class ProcessInfo
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string? Path { get; init; }
    public string? Description { get; init; }
    public string? Company { get; init; }
    public double CpuPercent { get; init; }
    public long MemoryBytes { get; init; }
    public bool HasWindow { get; init; }
    public string? WindowTitle { get; init; }
    public ProcessKind Kind { get; init; }
    public string? Priority { get; init; }
}

public sealed record BackgroundCandidate(string FriendlyName, string[] ExeNames, bool DefaultSelected, string Why);

public sealed record PriorityRule(string ExeName, int CpuPriorityClass)
{
    public string Label => CpuPriorityClass switch
    {
        1 => "Baja", 5 => "Debajo de lo normal", 2 => "Normal", 6 => "Encima de lo normal", 3 => "Alta", _ => CpuPriorityClass.ToString(),
    };
}

[SupportedOSPlatform("windows")]
public sealed class ProcessService
{
    /// <summary>Procesos críticos: cerrarlos cuelga o reinicia Windows. KizaTweaks nunca los toca.</summary>
    public static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "fontdrvhost", "Memory Compression", "Secure System", "LsaIso", "spoolsv",
        "MsMpEng", "NisSrv", "SecurityHealthService", "SgrmBroker", "audiodg", "conhost", "ctfmon",
        "sihost", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost",
        "TextInputHost", "SearchHost", "dllhost", "WmiPrvSE", "KizaTweaks", "explorer",
    };

    /// <summary>Procesos de fondo que normalmente se pueden cerrar antes de jugar sin romper nada.</summary>
    public static readonly IReadOnlyList<BackgroundCandidate> BackgroundCandidates = new[]
    {
        new BackgroundCandidate("OneDrive", new[] { "OneDrive" }, true, "Sincronización en la nube: usa disco y red mientras juegas."),
        new BackgroundCandidate("Widgets", new[] { "Widgets", "WidgetService" }, true, "Noticias y clima en segundo plano."),
        new BackgroundCandidate("Vínculo móvil", new[] { "PhoneExperienceHost", "YourPhone" }, true, "Conexión con el celular."),
        new BackgroundCandidate("Microsoft Teams", new[] { "ms-teams", "Teams" }, true, "Chat corporativo en segundo plano."),
        new BackgroundCandidate("Copilot", new[] { "Copilot" }, true, "Asistente de IA."),
        new BackgroundCandidate("Microsoft PC Manager", new[] { "MSPCManager", "MSPCManagerService" }, true, "Limpiador preinstalado."),
        // Solo el actualizador: AdobeIPCBroker, CoreSync y CCXProcess los usan Photoshop y demás apps de Adobe abiertas.
        new BackgroundCandidate("Actualizador de Adobe", new[] { "AdobeARM" }, true, "Busca actualizaciones de Acrobat/Reader en segundo plano."),
        new BackgroundCandidate("Google Drive", new[] { "GoogleDriveFS" }, false, "Sincronización en la nube."),
        new BackgroundCandidate("Dropbox", new[] { "Dropbox" }, false, "Sincronización en la nube."),
        new BackgroundCandidate("Spotify", new[] { "Spotify" }, false, "Ciérralo solo si no escuchas música mientras juegas."),
        new BackgroundCandidate("Microsoft Edge", new[] { "msedge" }, false, "Cierra TODAS las ventanas de Edge."),
        new BackgroundCandidate("Google Chrome", new[] { "chrome" }, false, "Cierra TODAS las ventanas de Chrome."),
    };

    private readonly string _winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>Toma dos muestras separadas por sampleMs para calcular el % de CPU de cada proceso.</summary>
    public List<ProcessInfo> List(int sampleMs = 1000)
    {
        var first = new Dictionary<int, TimeSpan>();
        foreach (var p in Process.GetProcesses())
        {
            try { first[p.Id] = p.TotalProcessorTime; } catch { /* protegido */ }
            p.Dispose();
        }
        var sw = Stopwatch.StartNew();
        Thread.Sleep(sampleMs);
        var elapsed = sw.Elapsed.TotalMilliseconds;
        var cores = Environment.ProcessorCount;

        var list = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    double cpu = 0;
                    try
                    {
                        if (first.TryGetValue(p.Id, out var t0))
                            cpu = Math.Max(0, (p.TotalProcessorTime - t0).TotalMilliseconds / (elapsed * cores) * 100);
                    }
                    catch { }
                    var path = ImagePath(p.Id);
                    string? desc = null, company = null;
                    if (path is not null)
                    {
                        try
                        {
                            var vi = FileVersionInfo.GetVersionInfo(path);
                            desc = string.IsNullOrWhiteSpace(vi.FileDescription) ? null : vi.FileDescription.Trim();
                            company = string.IsNullOrWhiteSpace(vi.CompanyName) ? null : vi.CompanyName.Trim();
                        }
                        catch { }
                    }
                    string? title = null; bool hasWindow = false;
                    try { hasWindow = p.MainWindowHandle != IntPtr.Zero; title = hasWindow ? p.MainWindowTitle : null; } catch { }
                    string? prio = null;
                    try { prio = p.PriorityClass.ToString(); } catch { }

                    list.Add(new ProcessInfo
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        Path = path,
                        Description = desc,
                        Company = company,
                        CpuPercent = Math.Round(cpu, 1),
                        MemoryBytes = SafeWorkingSet(p),
                        HasWindow = hasWindow,
                        WindowTitle = string.IsNullOrWhiteSpace(title) ? null : title,
                        Kind = Classify(p.ProcessName, path),
                        Priority = prio,
                    });
                }
                catch { /* el proceso terminó entre muestras */ }
            }
        }
        return list.OrderByDescending(x => x.CpuPercent).ThenByDescending(x => x.MemoryBytes).ToList();
    }

    private static long SafeWorkingSet(Process p)
    {
        try { return p.WorkingSet64; } catch { return 0; }
    }

    private ProcessKind Classify(string name, string? path)
    {
        if (ProtectedNames.Contains(name)) return ProcessKind.Protected;
        if (path is null) return ProcessKind.Protected; // sin acceso: proceso del sistema
        if (path.StartsWith(_winDir, StringComparison.OrdinalIgnoreCase)) return ProcessKind.Windows;
        return ProcessKind.App;
    }

    public (bool ok, string message) Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (ProtectedNames.Contains(p.ProcessName))
                return (false, $"{p.ProcessName} es un proceso crítico de Windows y no se puede cerrar.");
            if (p.Id == Environment.ProcessId) return (false, "No se puede cerrar KizaTweaks desde sí mismo.");
            p.Kill();
            p.WaitForExit(5000);
            Log.Info($"Proceso cerrado: {p.ProcessName} ({pid})");
            return (true, $"{p.ProcessName} cerrado.");
        }
        catch (ArgumentException) { return (true, "El proceso ya no estaba en ejecución."); }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Cierra todas las instancias de los ejecutables indicados. Devuelve cuántas cerró.</summary>
    public int KillByNames(IEnumerable<string> exeNames)
    {
        int n = 0;
        foreach (var name in exeNames)
        {
            if (ProtectedNames.Contains(name)) continue;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try { p.Kill(); n++; } catch (Exception ex) { Log.Warn($"No se pudo cerrar {name}: {ex.Message}"); }
                }
            }
        }
        Log.Info($"Sesión de juego: {n} procesos cerrados");
        return n;
    }

    public static int CountRunning(IEnumerable<string> exeNames) =>
        exeNames.Sum(n => { var ps = Process.GetProcessesByName(n); foreach (var p in ps) p.Dispose(); return ps.Length; });

    public (bool ok, string message) SetPriority(int pid, ProcessPriorityClass priority)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (ProtectedNames.Contains(p.ProcessName)) return (false, "Proceso crítico: no se modifica.");
            p.PriorityClass = priority;
            return (true, $"Prioridad de {p.ProcessName}: {priority}.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ---------------------------------------------------------------
    // Prioridad permanente (Image File Execution Options\PerfOptions)
    // Windows aplica la prioridad cada vez que se abre ese .exe.
    // ---------------------------------------------------------------

    private const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    public static List<PriorityRule> GetPriorityRules(ISystemBackend b)
    {
        var snap = b.ExportKey("HKLM", Ifeo);
        var list = new List<PriorityRule>();
        if (snap is null) return list;
        foreach (var (exe, key) in snap.SubKeys)
        {
            if (key.SubKeys.TryGetValue("PerfOptions", out var perf) &&
                perf.Values.TryGetValue("CpuPriorityClass", out var v) && v.Kind == RegKind.DWord)
                list.Add(new PriorityRule(exe, (int)v.Number));
        }
        return list.OrderBy(r => r.ExeName).ToList();
    }

    public static void SetPriorityRule(ISystemBackend b, string exeName, int cpuPriorityClass)
    {
        var exe = NormalizeExe(exeName);
        b.SetValue("HKLM", $@"{Ifeo}\{exe}\PerfOptions", "CpuPriorityClass", RegValue.Dword(cpuPriorityClass));
        Log.Info($"Prioridad permanente {exe} = {cpuPriorityClass}");
    }

    public static void RemovePriorityRule(ISystemBackend b, string exeName)
    {
        var exe = NormalizeExe(exeName);
        b.DeleteKey("HKLM", $@"{Ifeo}\{exe}\PerfOptions");
        // Solo se borra la clave del .exe si quedó vacía (podría tener otras opciones de depuración).
        if (b.KeyExists("HKLM", $@"{Ifeo}\{exe}") && b.KeyIsEmpty("HKLM", $@"{Ifeo}\{exe}"))
            b.DeleteKey("HKLM", $@"{Ifeo}\{exe}");
        Log.Info($"Prioridad permanente eliminada: {exe}");
    }

    private static string NormalizeExe(string name)
    {
        var n = System.IO.Path.GetFileName(name.Trim());
        if (n.Length == 0 || n.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Nombre de ejecutable inválido.");
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : n + ".exe";
    }

    // ---------------------------------------------------------------
    // Ruta del ejecutable sin requerir permisos de lectura de memoria
    // ---------------------------------------------------------------

    public static string? ImagePath(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
}

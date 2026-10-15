using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml.Linq;
using KizaTweaks.Core.Platform;
using KizaTweaks.Core.Processes;

namespace KizaTweaks.Core.Benchmark;

/// <summary>Foto del sistema en reposo, para comparar antes y después de optimizar.</summary>
public sealed class SystemSnapshot
{
    public string Label { get; set; } = "";
    public DateTime Time { get; set; }
    public int Processes { get; set; }
    public int RunningServices { get; set; }
    public int StartupEnabled { get; set; }
    public double CpuIdlePercent { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    /// <summary>Duración del último arranque en segundos (según Windows).</summary>
    public double? LastBootSeconds { get; set; }
    public List<double> RecentBootsSeconds { get; set; } = new();
}

public sealed record SnapshotRow(string Metric, string Before, string After, string Change, bool Better);

[SupportedOSPlatform("windows")]
public sealed class SnapshotService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly StartupService _startup;

    public SnapshotService(StartupService startup) => _startup = startup;

    public SystemSnapshot Take(string label)
    {
        var s = new SystemSnapshot { Label = label, Time = DateTime.Now };
        var ps = Process.GetProcesses();
        s.Processes = ps.Length;
        foreach (var p in ps) p.Dispose();
        s.CpuIdlePercent = Math.Round(Native.CpuUsage(3000), 1);
        if (Native.TryGetMemory(out var total, out var avail))
        {
            s.RamTotalGb = Math.Round(total / 1073741824.0, 1);
            s.RamUsedGb = Math.Round((total - avail) / 1073741824.0, 2);
        }
        s.RunningServices = Wmi.Query(@"root\cimv2", "SELECT Name FROM Win32_Service WHERE State='Running'", "Name").Count;
        try { s.StartupEnabled = _startup.List().Count(i => i.Enabled); } catch { }
        s.RecentBootsSeconds = BootTimes(5);
        s.LastBootSeconds = s.RecentBootsSeconds.Count > 0 ? s.RecentBootsSeconds[0] : null;
        Save(s);
        return s;
    }

    /// <summary>Tiempos de arranque (evento 100 de Diagnostics-Performance), del más reciente al más antiguo.</summary>
    public static List<double> BootTimes(int count)
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("wevtutil.exe"),
            $"qe Microsoft-Windows-Diagnostics-Performance/Operational /q:\"*[System[(EventID=100)]]\" /c:{count} /rd:true /f:xml",
            30_000, System.Text.Encoding.UTF8);
        var list = new List<double>();
        if (!r.Ok || string.IsNullOrWhiteSpace(r.Output)) return list;
        try
        {
            var root = XElement.Parse("<root>" + r.Output + "</root>");
            foreach (var ev in root.Elements().Where(e => e.Name.LocalName == "Event"))
            {
                var boot = ev.Descendants().FirstOrDefault(d => d.Name.LocalName == "Data" && (string?)d.Attribute("Name") == "BootTime");
                if (boot is not null && double.TryParse(boot.Value, out var ms) && ms > 0)
                    list.Add(Math.Round(ms / 1000.0, 1));
            }
        }
        catch (Exception ex) { Log.Warn($"No se pudieron leer los tiempos de arranque: {ex.Message}"); }
        return list;
    }

    private static void Save(SystemSnapshot s)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SnapshotsDir);
            File.WriteAllText(Path.Combine(AppPaths.SnapshotsDir, $"snapshot-{s.Time:yyyyMMdd-HHmmss}.json"), JsonSerializer.Serialize(s, Json));
        }
        catch (Exception ex) { Log.Error("No se pudo guardar la medición", ex); }
    }

    public static List<SystemSnapshot> LoadAll()
    {
        var list = new List<SystemSnapshot>();
        if (!Directory.Exists(AppPaths.SnapshotsDir)) return list;
        foreach (var f in Directory.GetFiles(AppPaths.SnapshotsDir, "snapshot-*.json"))
        {
            try { if (JsonSerializer.Deserialize<SystemSnapshot>(File.ReadAllText(f)) is { } s) list.Add(s); } catch { }
        }
        return list.OrderByDescending(s => s.Time).ToList();
    }

    public static List<SnapshotRow> Compare(SystemSnapshot a, SystemSnapshot b)
    {
        static SnapshotRow Row(string metric, double before, double after, string unit, bool lowerIsBetter, string fmt = "0.#")
        {
            var diff = after - before;
            var better = lowerIsBetter ? diff < 0 : diff > 0;
            var pct = before != 0 ? $" ({diff / before * 100:+0;-0}%)" : "";
            return new SnapshotRow(metric, before.ToString(fmt) + unit, after.ToString(fmt) + unit,
                (diff >= 0 ? "+" : "") + diff.ToString(fmt) + unit + pct, better && Math.Abs(diff) > 0.0001);
        }
        var rows = new List<SnapshotRow>
        {
            Row("Procesos en ejecución", a.Processes, b.Processes, "", true, "0"),
            Row("Servicios en ejecución", a.RunningServices, b.RunningServices, "", true, "0"),
            Row("Apps de inicio activas", a.StartupEnabled, b.StartupEnabled, "", true, "0"),
            Row("CPU en reposo", a.CpuIdlePercent, b.CpuIdlePercent, " %", true),
            Row("RAM en uso", a.RamUsedGb, b.RamUsedGb, " GB", true, "0.00"),
        };
        if (a.LastBootSeconds is { } ab && b.LastBootSeconds is { } bb)
            rows.Add(Row("Último arranque", ab, bb, " s", true));
        return rows;
    }
}

[SupportedOSPlatform("windows")]
public sealed class FpsResultStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<FpsResult> Load()
    {
        try
        {
            return File.Exists(AppPaths.FpsResultsFile)
                ? JsonSerializer.Deserialize<List<FpsResult>>(File.ReadAllText(AppPaths.FpsResultsFile)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Add(FpsResult r)
    {
        var list = Load();
        list.Insert(0, r);
        Directory.CreateDirectory(AppPaths.StateDir);
        File.WriteAllText(AppPaths.FpsResultsFile, JsonSerializer.Serialize(list.Take(200).ToList(), Json));
    }
}

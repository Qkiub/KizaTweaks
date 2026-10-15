using KizaTweaks.Core;
using KizaTweaks.Core.Apps;
using KizaTweaks.Core.Benchmark;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Maintenance;
using KizaTweaks.Core.Platform;
using KizaTweaks.Core.Processes;

namespace KizaTweaks.Tests;

/// <summary>
/// Pruebas contra el Windows REAL (solo lectura, salvo dos ajustes de HKCU que se aplican y se deshacen).
/// Se ejecutan en CI (runner de Windows) con: KizaTweaks.Tests --windows
/// </summary>
public static class WindowsIntegration
{
    public static int Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Las pruebas de integración solo corren en Windows.");
            return 0;
        }
        var logLines = new List<string>();
        Log.Written += l => { if (l.Contains("[WARN]") || l.Contains("[ERROR]")) { Console.WriteLine("    log: " + l); lock (logLines) logLines.Add(l); } };
        var tests = new List<(string, Action)>
        {
            ("Hardware: detección completa", Hardware),
            ("Motor: estado de todo el catálogo con el backend real", States),
            ("Motor: aplicar y deshacer en el registro real (HKCU)", RealApplyRevert),
            ("Sistema: servicios, tareas, características y energía", SystemReads),
            ("Procesos y arranque", ProcessesAndStartup),
            ("Apps Appx y WinGet", Apps),
            ("Arranque: tiempos desde el visor de eventos", Boot),
            ("Mantenimiento: analizar discos, DNS y TRIM (comandos reales)", Maintenance),
        };
        int failed = 0;
        foreach (var (name, body) in tests)
        {
            try { body(); Console.WriteLine($"  ✔ {name}"); }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  ✘ {name}\n      {ex}");
                Gh("error", name, ex.ToString());
            }
        }
        if (logLines.Count > 0) Gh("warning", "Avisos del registro", string.Join("\n", logLines.Take(40)));
        Console.WriteLine(failed == 0 ? "\nINTEGRACIÓN OK" : $"\nINTEGRACIÓN: {failed} fallos");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>En GitHub Actions, publica el mensaje como anotación visible en el resumen del run.</summary>
    public static void Gh(string level, string title, string message)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return;
        var msg = message.Length > 3000 ? message[..3000] : message;
        msg = msg.Replace("%", "%25").Replace("\r", "").Replace("\n", "%0A");
        Console.WriteLine($"::{level} title={title.Replace(",", " ").Replace(":", " ")}::{msg}");
    }

    private static void Check(bool cond, string msg)
    {
        if (!cond) throw new Exception(msg);
    }

    private static void Hardware()
    {
        var b = new WindowsBackend();
        var r = new HardwareService(b).Collect();
        Console.WriteLine($"      OS: {r.Os} | CPU: {r.Cpu.Name} ({r.Cpu.Cores}c/{r.Cpu.Threads}t)");
        Console.WriteLine($"      RAM: {Fmt(r.RamTotalBytes)} en {r.Ram.Count} módulos | GPUs: {string.Join(", ", r.Gpus.Select(g => $"{g.Name} {Fmt(g.VramBytes)}"))}");
        foreach (var d in r.Disks) Console.WriteLine($"      Disco: {d.Model} {d.Kind} {Fmt(d.SizeBytes)} salud={d.Health} letras={string.Join(",", d.Letters)} sistema={d.HasSystem}");
        foreach (var d in r.Displays) Console.WriteLine($"      Pantalla: {d.MonitorName} {d.Width}x{d.Height} {d.CurrentHz}/{d.MaxHz} Hz");
        Console.WriteLine($"      TRIM={r.TrimEnabled} VBS={r.VbsRunning} HVCI={r.HvciRunning} HAGS={r.HagsEnabled} Energía={r.PowerSchemeName} Portátil={r.IsLaptop}");
        Check(!string.IsNullOrWhiteSpace(r.Cpu.Name) && r.Cpu.Threads > 0, "CPU no detectada");
        Check(r.RamTotalBytes > 0, $"RAM no detectada (módulos WMI: {r.Ram.Count}, GPUs: {r.Gpus.Count})");
        Check(r.Disks.Count > 0, "discos no detectados");
        Check(r.Volumes.Any(v => v.Letter.Equals(r.SystemDrive, StringComparison.OrdinalIgnoreCase)), "volumen del sistema no detectado");
        Gh("notice", "Hardware detectado", $"{r.Os} | CPU {r.Cpu.Name} | RAM {Fmt(r.RamTotalBytes)} | GPU {string.Join(", ", r.Gpus.Select(g => g.Name))} | Discos {string.Join("; ", r.Disks.Select(d => $"{d.Model} {d.Kind} {d.Health} {string.Join(",", d.Letters)}"))} | TRIM={r.TrimEnabled} VBS={r.VbsRunning}");
        var recs = RecommendationEngine.Build(r);
        Console.WriteLine($"      {recs.Count} recomendaciones, puntaje {RecommendationEngine.Score(recs)}");
    }

    private static void States()
    {
        var e = new TweakEngine(new WindowsBackend(), new BackupStore(null));
        var c = CatalogLoader.LoadTweaks();
        var build = SystemActions.OsBuild();
        var counts = c.Tweaks.Where(t => t.IsCompatible(build)).GroupBy(e.GetState).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("      " + string.Join(" | ", counts.Select(kv => $"{kv.Key}: {kv.Value}")));
        Check(counts.Values.Sum() > 50, "muy pocos tweaks evaluados");
    }

    private static void RealApplyRevert()
    {
        var b = new WindowsBackend();
        var store = new BackupStore(Path.Combine(Path.GetTempPath(), $"kiza-int-{Guid.NewGuid():N}.json"));
        var e = new TweakEngine(b, store);
        var c = CatalogLoader.LoadTweaks();
        foreach (var id in new[] { "q.ShowKnownFileExt", "kiza.windowedOpt", "kiza.vrr" })
        {
            var t = c.Find(id)!;
            var before = Snapshot(b, t);
            var r = e.Apply(t);
            Check(r.Success, $"{id}: {string.Join(";", r.Errors)}");
            Check(e.GetState(t) == TweakState.Applied, $"{id}: no quedó aplicado");
            var rv = e.Revert(t);
            Check(rv.Success, $"{id} deshacer: {string.Join(";", rv.Errors)}");
            var after = Snapshot(b, t);
            Check(before == after, $"{id}: el registro no volvió a su estado original\n antes: {before}\n después: {after}");
        }
    }

    private static string Snapshot(ISystemBackend b, TweakDefinition t) =>
        string.Join(";", t.Apply.Where(o => o.Op.StartsWith("reg.")).Select(o => $"{o.Path}\\{o.Name}={b.GetValue(o.Hive!, o.Path!, o.Name ?? "")}"))
        + "|dx=" + DxSettings.Read(b);

    private static void SystemReads()
    {
        var b = new WindowsBackend();
        Console.WriteLine($"      DiagTrack: {b.GetServiceStartType("DiagTrack")} | Spooler: {b.GetServiceStartType("Spooler")}");
        Console.WriteLine($"      Tarea ProgramDataUpdater: {b.GetTaskEnabled(@"\Microsoft\Windows\Application Experience\ProgramDataUpdater")?.ToString() ?? "no existe"}");
        Console.WriteLine($"      Tarea ScheduledDefrag: {b.GetTaskEnabled(@"\Microsoft\Windows\Defrag\ScheduledDefrag")?.ToString() ?? "no existe"}");
        Console.WriteLine($"      VirtualMachinePlatform: {b.GetFeatureEnabled("VirtualMachinePlatform")?.ToString() ?? "no existe"}");
        var active = b.GetActivePowerScheme();
        Console.WriteLine($"      Plan activo: {active} | equilibrado: {b.ResolvePowerScheme("balanced", false)}");
        Check(b.GetServiceStartType("Spooler") is not null, "no se pudo leer un servicio");
        Check(active is not null, "no se pudo leer el plan de energía");
        Check(b.GetTaskEnabled(@"\Microsoft\Windows\Defrag\ScheduledDefrag") is not null, "no se pudo leer una tarea programada");
    }

    private static void ProcessesAndStartup()
    {
        var list = new ProcessService().List(300);
        Console.WriteLine($"      {list.Count} procesos, {list.Count(p => p.Kind == ProcessKind.App)} apps; top CPU: {list.FirstOrDefault()?.Name}");
        Check(list.Count > 10, "pocos procesos");
        var st = new StartupService(new WindowsBackend()).List();
        Console.WriteLine($"      {st.Count} elementos de arranque");
        var rules = ProcessService.GetPriorityRules(new WindowsBackend());
        Console.WriteLine($"      {rules.Count} reglas de prioridad permanente");
    }

    private static void Apps()
    {
        var svc = new AppService();
        var appx = svc.GetInstalledAppxNames();
        Console.WriteLine($"      {appx.Count} paquetes Appx; WinGet: {AppService.WingetPath() ?? "no disponible"}");
        Check(appx.Count > 0, "no se listaron paquetes Appx");
        var cat = CatalogLoader.LoadApps();
        var installed = cat.Apps.Where(a => a.Method == "Appx" && svc.IsInstalled(a, appx)).Select(a => a.Name).ToList();
        Console.WriteLine($"      Del catálogo instaladas: {string.Join(", ", installed.Take(15))}");
    }

    private static void Boot()
    {
        var times = SnapshotService.BootTimes(3);
        Console.WriteLine($"      Arranques: {string.Join(", ", times.Select(t => t + " s"))}");
    }

    private static void Maintenance()
    {
        var svc = new MaintenanceService();
        Console.WriteLine($"      Volúmenes: {string.Join(", ", MaintenanceService.FixedVolumes())}");
        foreach (var id in new[] { "analyze", "dns", "trim-on" })
        {
            var t = svc.Tasks.First(x => x.Id == id);
            var lines = new List<string>();
            var r = t.Run(new MaintenanceContext { Output = lines.Add }).GetAwaiter().GetResult();
            Console.WriteLine($"      {t.Name}: {r.Summary} ({lines.Count} líneas)");
            foreach (var l in lines.Take(4)) Console.WriteLine("        | " + l);
            Check(r.Ok, $"{id} falló: {r.Summary}");
        }
    }

    private static string Fmt(long b) => b <= 0 ? "?" : $"{b / 1073741824.0:0.#} GB";
}

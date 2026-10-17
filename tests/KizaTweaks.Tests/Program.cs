using KizaTweaks.Core.Benchmark;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Maintenance;
using KizaTweaks.Core.Platform;
using KizaTweaks.Core.Processes;
using KizaTweaks.Tests;

if (args.Contains("--windows")) return WindowsIntegration.Run();

var tests = new List<(string name, Action body)>
{
    ("Catálogo: carga, ids únicos y categorías válidas", CatalogIsValid),
    ("Catálogo: todos los valores de registro se pueden convertir", CatalogValuesParse),
    ("Motor: aplicar y deshacer TODO el catálogo deja el sistema idéntico", ApplyRevertWholeCatalogRestoresState),
    ("Motor: deshacer restaura valores previos exactos (no los 'por defecto')", RevertRestoresExactPreviousValues),
    ("Motor: reaplicar no pisa el respaldo original", ReapplyKeepsOriginalBackup),
    ("Motor: estado aplicado / no aplicado / parcial", DetectsState),
    ("Motor: borrar clave se respalda con todo su árbol", DeleteKeyBackupRestoresTree),
    ("Motor: sin respaldo usa las operaciones 'undo' del catálogo", RevertWithoutBackupUsesUndo),
    ("DirectX: los ajustes conviven sin pisarse", DxSettingsIndependent),
    ("Energía: deshacer vuelve al plan anterior", PowerSchemeRestores),
    ("Energía: ajuste del plan activo se aplica y se restaura exacto", PowerSettingRestores),
    ("Perfiles: Máximo rendimiento es seguro, acumulativo y se adapta a portátil", ProfilesAdaptToHardware),
    ("Limpieza: deshace solo los ajustes retirados que aplicó KizaTweaks y quita prioridades", SafetyCleanupRevertsRetired),
    ("Perfiles: el plan de energía se aplica antes que sus ajustes", ProfileApplyOrder),
    ("FPS: promedio, 1 % low y 0,1 % low", FrameStatsMath),
    ("FPS: lectura de CSV de PresentMon", PresentMonCsv),
    ("Recomendaciones: caso real (BX500 + NV3, 60/144 Hz, RAM sin XMP)", RecommendationsRealCase),
    ("Respaldo: se guarda y se lee desde disco", BackupStoreRoundTrip),
    ("Mantenimiento: catálogo válido y sin comandos peligrosos", MaintenanceCatalog),
    ("Limpieza: borra solo lo viejo, salta enlaces y conserva la carpeta", CleanerDeletesOnlyOld),
    ("Limpieza: se niega a tocar carpetas críticas", CleanerRefusesCriticalFolders),
    ("Interfaz: perfil Gamer → aplicar → respaldos → deshacer todo", ViewModelTests.GamerProfileFlow),
    ("Interfaz: cancelar no modifica nada", ViewModelTests.CancelDoesNothing),
    ("Interfaz: navegación, filtros y búsqueda", ViewModelTests.NavigationAndFilters),
    ("Modo FPS: activar nivel, progreso e interruptor", ViewModelTests.FpsModeFlow),
    ("Modo FPS: configurar y quitar un juego", ViewModelTests.GameConfigFlow),
    ("Cierres: lectura del registro de eventos y explicación del módulo", CrashLogParse),
};

int failed = 0;
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"  ✔ {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  ✘ {name}\n      {ex.GetType().Name}: {ex.Message}");
        WindowsIntegration.Gh("error", name, ex.ToString());
    }
}
Console.WriteLine(failed == 0 ? $"\nOK: {tests.Count} pruebas" : $"\nFALLARON {failed} de {tests.Count}");
return failed == 0 ? 0 : 1;

// =====================================================================

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static TweakEngine NewEngine(InMemoryBackend b) => new(b, new BackupStore(null));

static void CatalogIsValid()
{
    var c = CatalogLoader.LoadTweaks();
    Assert(c.Tweaks.Count >= 70, $"pocos tweaks: {c.Tweaks.Count}");
    Assert(c.Tweaks.Select(t => t.Id).Distinct().Count() == c.Tweaks.Count, "ids duplicados");
    var cats = c.Categories.Select(x => x.Id).ToHashSet();
    foreach (var t in c.Tweaks)
    {
        Assert(cats.Contains(t.Category), $"{t.Id}: categoría '{t.Category}' no existe");
        Assert(t.Apply.Count > 0 && t.Undo.Count > 0, $"{t.Id}: sin apply/undo");
        Assert(t.Risk is "safe" or "moderate" or "risky", $"{t.Id}: riesgo inválido");
        Assert(!string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Description), $"{t.Id}: sin textos");
    }
    // Lo que KizaTweaks NO debe ofrecer nunca
    var forbidden = new[] { "WinDefend", "wuauserv", "DisableAntiSpyware", "NoAutoUpdate" };
    var all = System.Text.Json.JsonSerializer.Serialize(c);
    foreach (var f in forbidden) Assert(!all.Contains(f, StringComparison.OrdinalIgnoreCase), $"el catálogo contiene {f}");
    var apps = CatalogLoader.LoadApps();
    Assert(apps.Apps.Count > 100, "pocas apps");
    Assert(!apps.Apps.Any(a => a.Ids.Contains("Microsoft.Edge")), "Edge no debe estar en la lista");
    Assert(!apps.Apps.Any(a => a.SelectedByDefault && a.Recommendation != "safe"), "solo apps seguras preseleccionadas");
}

static void CatalogValuesParse()
{
    foreach (var t in CatalogLoader.LoadTweaks().Tweaks)
        foreach (var op in t.Apply.Concat(t.Undo).Concat(t.Detect ?? new()))
        {
            if (op.Op.StartsWith("reg."))
                Assert(op.Hive is "HKCU" or "HKLM" or "HKCR" or "HKU" && !string.IsNullOrEmpty(op.Path), $"{t.Id}: {op}");
            if (op.Op == "reg.set") RegValue.FromOp(op);
        }
}

static void ApplyRevertWholeCatalogRestoresState()
{
    var b = new InMemoryBackend();
    // Estado inicial realista para algunos objetivos no-registro
    b.Services["DiagTrack"] = "Automatic";
    b.Tasks[@"\Microsoft\Windows\Application Experience\ProgramDataUpdater"] = true;
    b.Features["VirtualMachinePlatform"] = true;
    // Ajustes de energía existentes en el plan equilibrado (USB, PCIe, núcleos)
    b.PowerSettings["381b4222-f694-41f0-9685-ff5bb260df2e|48e6b7a6-50f5-4782-a5d4-53bb8f07e226"] = 1;
    b.PowerSettings["381b4222-f694-41f0-9685-ff5bb260df2e|ee12f906-d277-404b-b6da-e5fa1a576df5"] = 1;
    b.PowerSettings["381b4222-f694-41f0-9685-ff5bb260df2e|0cc5b647-c1df-4637-891a-dec35c318583"] = 10;
    b.SetValue("HKCU", @"Control Panel\Mouse", "MouseSpeed", RegValue.Str("1"));
    b.SetValue("HKCU", @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", RegValue.Str("VRROptimizeEnable=0;"));
    var before = b.Dump();

    var e = NewEngine(b);
    var tweaks = CatalogLoader.LoadTweaks().Tweaks;
    foreach (var t in tweaks)
    {
        var r = e.Apply(t);
        Assert(r.Success, $"{t.Id} falló: {string.Join(";", r.Errors)}");
    }
    Assert(b.Dump() != before, "aplicar no cambió nada");
    foreach (var t in Enumerable.Reverse(tweaks))
    {
        var r = e.Revert(t);
        Assert(r.Success, $"deshacer {t.Id} falló: {string.Join(";", r.Errors)}");
    }
    var after = b.Dump();
    if (after != before)
    {
        var bl = before.Split('\n'); var al = after.Split('\n');
        var diff = al.Except(bl).Take(5).Concat(new[] { "--- faltan:" }).Concat(bl.Except(al).Take(5));
        throw new Exception("el estado final difiere:\n" + string.Join("\n", diff));
    }
}

static void RevertRestoresExactPreviousValues()
{
    var b = new InMemoryBackend();
    b.SetValue("HKCU", @"Control Panel\Mouse", "MouseSpeed", RegValue.Str("2"));
    b.SetValue("HKCU", @"Control Panel\Mouse", "MouseThreshold1", RegValue.Str("4"));
    var e = NewEngine(b);
    var t = CatalogLoader.LoadTweaks().Find("q.DisableMouseAcceleration")!;
    e.Apply(t);
    Assert(b.GetValue("HKCU", @"Control Panel\Mouse", "MouseSpeed")!.Text == "0", "no aplicó");
    e.Revert(t);
    Assert(b.GetValue("HKCU", @"Control Panel\Mouse", "MouseSpeed")!.Text == "2", "no restauró MouseSpeed=2");
    Assert(b.GetValue("HKCU", @"Control Panel\Mouse", "MouseThreshold1")!.Text == "4", "no restauró MouseThreshold1=4");
    Assert(b.GetValue("HKCU", @"Control Panel\Mouse", "MouseThreshold2") is null, "dejó un valor que no existía");
}

static void ReapplyKeepsOriginalBackup()
{
    var b = new InMemoryBackend();
    b.SetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", RegValue.Dword(1));
    var e = NewEngine(b);
    var t = CatalogLoader.LoadTweaks().Find("kiza.hags")!;
    e.Apply(t);
    e.Apply(t); // segunda vez: el respaldo debe seguir diciendo 1
    e.Revert(t);
    Assert(b.GetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode")!.Number == 1, "perdió el valor original");
}

static void DetectsState()
{
    var b = new InMemoryBackend();
    var e = NewEngine(b);
    var t = CatalogLoader.LoadTweaks().Find("q.DisableMouseAcceleration")!;
    Assert(e.GetState(t) == TweakState.NotApplied, "debería estar no aplicado");
    e.Apply(t);
    Assert(e.GetState(t) == TweakState.Applied, "debería estar aplicado");
    b.SetValue("HKCU", @"Control Panel\Mouse", "MouseSpeed", RegValue.Str("1"));
    Assert(e.GetState(t) == TweakState.Partial, "debería estar parcial");
}

static void DeleteKeyBackupRestoresTree()
{
    var b = new InMemoryBackend();
    var root = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    b.SetValue("HKCU", root + @"\InprocServer32", "", RegValue.Str(""));
    b.SetValue("HKCU", root + @"\Otra", "x", RegValue.Dword(5));
    var tweak = new TweakDefinition
    {
        Id = "t", Name = "t",
        Apply = { new TweakOp { Op = "reg.deleteKey", Hive = "HKCU", Path = root } },
        Undo = { new TweakOp { Op = "reg.ensureKey", Hive = "HKCU", Path = root } },
    };
    var e = NewEngine(b);
    e.Apply(tweak);
    Assert(!b.KeyExists("HKCU", root), "no borró la clave");
    e.Revert(tweak);
    Assert(b.GetValue("HKCU", root + @"\Otra", "x")?.Number == 5, "no restauró la subclave");
    Assert(b.GetValue("HKCU", root + @"\InprocServer32", "") is not null, "no restauró el valor por defecto");
}

static void RevertWithoutBackupUsesUndo()
{
    var b = new InMemoryBackend();
    var t = CatalogLoader.LoadTweaks().Find("kiza.hags")!;
    b.SetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", RegValue.Dword(2)); // aplicado por otra herramienta
    NewEngine(b).Revert(t);
    Assert(b.GetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode")!.Number == 1, "no usó el undo del catálogo");
}

static void DxSettingsIndependent()
{
    Assert(DxSettings.With("A=1;B=2;", "B", "3") == "A=1;B=3;", "reemplazo");
    Assert(DxSettings.With("A=1;", "C", "0") == "A=1;C=0;", "agregar");
    Assert(DxSettings.With("A=1;C=0;", "C", null) == "A=1;", "quitar");
    var b = new InMemoryBackend();
    var e = NewEngine(b);
    var c = CatalogLoader.LoadTweaks();
    var w = c.Find("kiza.windowedOpt")!; var v = c.Find("kiza.vrr")!;
    e.Apply(w); e.Apply(v);
    Assert(DxSettings.Read(b) == "SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;", DxSettings.Read(b) ?? "null");
    e.Revert(w);
    Assert(DxSettings.Read(b) == "VRROptimizeEnable=1;", "deshacer uno pisó el otro: " + DxSettings.Read(b));
    e.Revert(v);
    Assert(DxSettings.Read(b) is null, "debería quedar vacío");
}

static void PowerSchemeRestores()
{
    var b = new InMemoryBackend { ActiveScheme = "aaaaaaaa-0000-0000-0000-000000000001" };
    var e = NewEngine(b);
    var t = CatalogLoader.LoadTweaks().Find("kiza.powerUltimate")!;
    e.Apply(t);
    Assert(b.ActiveScheme == "6b2d4f1a-5c3e-4a7b-9d8e-1f2a3b4c5d62", "no activó máximo rendimiento");
    Assert(e.GetState(t) == TweakState.Applied, "estado");
    e.Revert(t);
    Assert(b.ActiveScheme == "aaaaaaaa-0000-0000-0000-000000000001", "no volvió al plan personalizado anterior");
}

static void FrameStatsMath()
{
    // 99 frames de 10 ms (100 fps) + 1 frame de 50 ms (20 fps)
    var ft = Enumerable.Repeat(10.0, 99).Append(50.0).ToList();
    var r = FrameStats.Compute(ft);
    Assert(Math.Abs(r.AvgFps - 100 / 1.04) < 0.2, $"promedio {r.AvgFps}");
    Assert(Math.Abs(r.Low1 - 20) < 0.01, $"1% low {r.Low1}");
    Assert(r.Frames == 100, "frames");
    Assert(r.P99FrameTimeMs == 10, $"p99 {r.P99FrameTimeMs}");
}

static void PresentMonCsv()
{
    var csv = new[]
    {
        "Application,ProcessID,SwapChainAddress,PresentRuntime,MsBetweenPresents,MsBetweenDisplayChange",
        "game.exe,10,0x1,DXGI,16.6,16.6",
        "game.exe,10,0x1,DXGI,16.8,16.8",
        "other.exe,11,0x2,DXGI,5.0,5.0",
        "game.exe,10,0x1,DXGI,NA,16.8",
    };
    var ft = FrameStats.ParsePresentMonCsv(csv, "game.exe");
    Assert(ft.Count == 2 && ft[0] == 16.6 && ft[1] == 16.8, string.Join(",", ft));
}

static void RecommendationsRealCase()
{
    const long GB = 1024L * 1024 * 1024;
    var r = new HardwareReport
    {
        SystemDrive = "C:",
        Disks =
        {
            new DiskInfo { DeviceId = "0", Model = "CT120BX500SSD1", MediaType = "SSD", Bus = "SATA", Health = "Buena", Letters = { "C:" }, HasSystem = true },
            new DiskInfo { DeviceId = "1", Model = "ST1000LM035", MediaType = "HDD", Bus = "SATA", Health = "Buena", Letters = { "D:" } },
            new DiskInfo { DeviceId = "2", Model = "KINGSTON SNV3S1000G", MediaType = "SSD", Bus = "NVMe", Health = "Buena", Letters = { "E:" }, TemperatureC = 45 },
        },
        Volumes = { new VolumeInfo { Letter = "C:", SizeBytes = 111 * GB, FreeBytes = 76 * GB } },
        Displays = { new DisplayInfo { MonitorName = "Monitor", Width = 1920, Height = 1080, CurrentHz = 60, MaxHz = 144, IsPrimary = true } },
        Ram = { new RamModule { RatedMhz = 3200, ConfiguredMhz = 2666, Type = "DDR4", CapacityBytes = 8 * GB },
                new RamModule { RatedMhz = 3200, ConfiguredMhz = 2666, Type = "DDR4", CapacityBytes = 8 * GB } },
        RamTotalBytes = 16 * GB,
        HagsEnabled = false,
        HibernationEnabled = true,
    };
    var recs = RecommendationEngine.Build(r);
    Assert(recs.Any(x => x.Title.Contains("144 Hz")), "no detectó el monitor");
    Assert(recs.Any(x => x.Title.Contains("3200")), "no detectó XMP");
    Assert(recs.Any(x => x.Title.Contains("disco más lento")), "no detectó Windows en el disco lento");
    Assert(!recs.Any(x => x.TweakId is { } id && CatalogLoader.LoadTweaks().Find(id)!.Retired), "recomendó un ajuste retirado");
    Assert(recs.Any(x => x.TweakId == "kiza.hibernate"), "no recomendó quitar hibernación con C: de 120 GB");
    Assert(recs[0].Severity == RecSeverity.High, "orden por severidad");
    var score = RecommendationEngine.Score(recs);
    Assert(score is > 0 and < 100, $"puntaje {score}");
}

static void BackupStoreRoundTrip()
{
    var file = Path.Combine(Path.GetTempPath(), $"kiza-test-{Guid.NewGuid():N}.json");
    try
    {
        var s = new BackupStore(file);
        s.Put(new TweakBackup
        {
            TweakId = "x", TweakName = "X", AppliedAt = DateTime.Now,
            Entries =
            {
                new RegValueBackup { Hive = "HKCU", Path = "a", Name = "b", Previous = RegValue.Bin(new byte[] { 1, 2 }), KeyExisted = true },
                new DxBackup { Key = "K", Previous = "1" },
                new PowerBackup { PreviousScheme = "g" },
            },
        });
        var s2 = new BackupStore(file);
        var b = s2.Get("x")!;
        Assert(b.Entries.Count == 3, "entradas");
        Assert(b.Entries[0] is RegValueBackup rv && rv.Previous!.Bytes!.SequenceEqual(new byte[] { 1, 2 }), "tipo polimórfico");
        Assert(b.Entries[2] is PowerBackup { PreviousScheme: "g" }, "power");
    }
    finally { File.Delete(file); }
}

static void MaintenanceCatalog()
{
    var tasks = new MaintenanceService().Tasks;
    Assert(tasks.Count >= 15, $"pocas tareas: {tasks.Count}");
    Assert(tasks.Select(t => t.Id).Distinct().Count() == tasks.Count, "ids duplicados");
    var groups = MaintenanceService.Groups.Select(g => g.Id).ToHashSet();
    Assert(tasks.All(t => groups.Contains(t.Group)), "grupo inexistente");
    Assert(tasks.Any(t => t.InQuick) && tasks.Any(t => t.InFull), "faltan conjuntos rápido/completo");
    // El rápido no debe incluir nada que pida reinicio, sea moderado o tarde mucho
    Assert(tasks.Where(t => t.InQuick).All(t => t.Risk == "safe" && !t.RequiresReboot), "el rápido incluye algo riesgoso");
    var all = string.Join("\n", tasks.Select(t => t.Command)).ToLowerInvariant();
    foreach (var bad in new[] { "format", "diskpart", "bcdedit", "prefetch", "/resetbase", "vssadmin delete", "cipher /w" })
        Assert(!all.Contains(bad), $"comando no permitido: {bad}");
}

static void CleanerDeletesOnlyOld()
{
    var root = Path.Combine(Path.GetTempPath(), "kiza-clean-" + Guid.NewGuid().ToString("N"));
    var outside = Path.Combine(Path.GetTempPath(), "kiza-outside-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
        Directory.CreateDirectory(outside);
        void Make(string path, int bytes, bool old)
        {
            File.WriteAllBytes(path, new byte[bytes]);
            if (old) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        }
        Make(Path.Combine(root, "viejo.tmp"), 1000, true);
        Make(Path.Combine(root, "nuevo.tmp"), 500, false);
        Make(Path.Combine(root, "sub", "deep", "viejo2.log"), 2000, true);
        Make(Path.Combine(outside, "importante.txt"), 10, true);
        // Enlace simbólico hacia fuera: no se debe seguir
        try { Directory.CreateSymbolicLink(Path.Combine(root, "enlace"), outside); } catch { }

        var r = Cleaner.CleanDirectory(root, TimeSpan.FromHours(24));
        Assert(r.Files == 2 && r.Bytes == 3000, $"borró {r.Files} archivos / {r.Bytes} bytes");
        Assert(File.Exists(Path.Combine(root, "nuevo.tmp")), "borró un archivo reciente");
        Assert(!Directory.Exists(Path.Combine(root, "sub")), "no quitó carpetas vacías");
        Assert(Directory.Exists(root), "borró la carpeta raíz");
        Assert(File.Exists(Path.Combine(outside, "importante.txt")), "siguió un enlace y borró fuera de la carpeta");
    }
    finally
    {
        try { Directory.Delete(root, true); } catch { }
        try { Directory.Delete(outside, true); } catch { }
    }
}

static void CleanerRefusesCriticalFolders()
{
    Assert(!Cleaner.IsSafeRoot("/"), "raíz");
    Assert(!Cleaner.IsSafeRoot(""), "vacío");
    Assert(!Cleaner.IsSafeRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "perfil de usuario");
    Assert(!Cleaner.IsSafeRoot("/tmp"), "carpeta de primer nivel");
    Assert(Cleaner.IsSafeRoot(Path.Combine(Path.GetTempPath(), "kiza", "x")), "subcarpeta temporal debería permitirse");
    var r = Cleaner.CleanDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null);
    Assert(r.Files == 0, "intentó limpiar el perfil de usuario");
}

static void PowerSettingRestores()
{
    const string usb = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    var b = new InMemoryBackend();
    b.PowerSettings[$"{b.ActiveScheme}|{usb}"] = 1;
    var e = NewEngine(b);
    var t = CatalogLoader.LoadTweaks().Find("kiza.usbSuspend")!;
    Assert(e.GetState(t) == TweakState.NotApplied, "debería estar sin aplicar");
    Assert(e.Apply(t).Success, "aplicar");
    Assert(b.GetPowerSetting(b.ActiveScheme, "", usb) == 0, "no se escribió 0");
    Assert(e.GetState(t) == TweakState.Applied, "no figura aplicado");
    Assert(e.Revert(t).Success, "deshacer");
    Assert(b.GetPowerSetting(b.ActiveScheme, "", usb) == 1, "no volvió a 1");

    // Si el ajuste no existe en el equipo, se omite sin error.
    var b2 = new InMemoryBackend();
    var e2 = NewEngine(b2);
    Assert(e2.Apply(t).Success && e2.Revert(t).Success, "ajuste inexistente debería omitirse");
}

static void ProfilesAdaptToHardware()
{
    var c = CatalogLoader.LoadTweaks();
    var desktop = new HardwareReport { IsLaptop = false, Cpu = new CpuInfo { Name = "Intel Core i5-12400F", Cores = 6 } };
    var laptop = new HardwareReport { IsLaptop = true, Cpu = new CpuInfo { Name = "AMD Ryzen 7 6800H", Cores = 8 } };
    var gamer = ProfileResolver.Resolve(Profiles.Gamer, c, null).Ids;
    var perf = ProfileResolver.Resolve(Profiles.Performance, c, desktop).Ids;
    Assert(gamer.IsSubsetOf(perf), "Máximo rendimiento debe incluir Gamer");
    Assert(perf.Contains("kiza.powerUltimate") && perf.Contains("kiza.usbSuspend") && perf.Contains("kiza.pcieLspm"), "máximo rendimiento incompleto");
    foreach (var id in new[] { Profiles.Gamer, Profiles.Performance, Profiles.Privacy, ProfileResolver.Safe })
    {
        var sel = ProfileResolver.Resolve(id, c, desktop).Ids;
        Assert(!sel.Any(x => c.Find(x)!.Retired), $"{id} incluye ajustes retirados");
        Assert(!sel.Any(x => c.Find(x)!.Risk == "risky" || c.Find(x)!.Category is "security" or "experimental"), $"{id} incluye ajustes con riesgo");
    }
    foreach (var id in new[] { "kiza.hags", "kiza.windowedOpt", "kiza.vrr", "kiza.win32Priority", "kiza.timerResolution", "kiza.coreParking", "kiza.mmcssGames" })
        Assert(c.Find(id)!.Retired, $"{id} debería estar retirado");
    Assert(!perf.Contains("q.DisableLocationServices") && !perf.Contains("q.DisableNotifications"), "no debe quitar funciones básicas");
    var lap = ProfileResolver.Resolve(Profiles.Performance, c, laptop);
    Assert(!lap.Ids.Contains("kiza.powerUltimate") && lap.AdjustedForHardware.Count == 1, "portátil");
}

static void SafetyCleanupRevertsRetired()
{
    var b = new InMemoryBackend();
    var e = NewEngine(b);
    var c = CatalogLoader.LoadTweaks();
    const string prio = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    b.SetValue("HKLM", prio, "Win32PrioritySeparation", RegValue.Dword(2));
    Assert(e.Apply(c.Find("kiza.win32Priority")!).Success, "aplicar retirado");
    Assert(e.Apply(c.Find("kiza.gameMode")!).Success, "aplicar seguro");
    // HAGS activado por el usuario (sin respaldo de KizaTweaks): no se toca
    b.SetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", RegValue.Dword(2));
    ProcessService.SetPriorityRule(b, "FiveM_GTAProcess.exe", 3);
    var r = SafetyCleanup.Run(e, c, b, removePriorityRules: true);
    Assert(r.Reverted.Count == 1, $"deshechos: {string.Join(",", r.Reverted)}");
    Assert(b.GetValue("HKLM", prio, "Win32PrioritySeparation")!.Number == 2, "no volvió al valor original");
    Assert(e.GetState(c.Find("kiza.gameMode")!) == TweakState.Applied, "no debe tocar ajustes seguros");
    Assert(b.GetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode")!.Number == 2, "no debe tocar lo que el usuario activó");
    Assert(r.PriorityRulesRemoved.SequenceEqual(new[] { "FiveM_GTAProcess.exe" }) && ProcessService.GetPriorityRules(b).Count == 0, "prioridad");
    Assert(!SafetyCleanup.Run(e, c, b, true).AnythingDone, "la segunda vez no hay nada que hacer");
}

static void ProfileApplyOrder()
{
    var c = CatalogLoader.LoadTweaks();
    var order = ProfileResolver.ApplyOrder(new[] { c.Find("kiza.usbSuspend")!, c.Find("kiza.gameMode")!, c.Find("kiza.powerUltimate")! });
    Assert(order[0].Id == "kiza.powerUltimate" && order[2].Id == "kiza.usbSuspend", string.Join(",", order.Select(t => t.Id)));
}

static void CrashLogParse()
{
    var xml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>1000</EventID>" +
              "<TimeCreated SystemTime='2026-10-16T16:40:00.000Z'/></System><EventData>" +
              "<Data Name='AppName'>Photoshop.exe</Data><Data Name='AppVersion'>26.0</Data><Data Name='AppTimeStamp'>0</Data>" +
              "<Data Name='ModuleName'>nvoglv64.dll</Data><Data Name='ModuleVersion'>32.0</Data><Data Name='ModuleTimeStamp'>0</Data>" +
              "<Data Name='ExceptionCode'>c0000005</Data></EventData></Event>" +
              "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><TimeCreated SystemTime='2026-10-15T10:00:00Z'/></System>" +
              "<EventData><Data>chrome.exe</Data><Data>1</Data><Data>0</Data><Data>ntdll.dll</Data><Data>1</Data><Data>0</Data><Data>c0000374</Data></EventData></Event>";
    var list = CrashLog.Parse(xml);
    Assert(list.Count == 2, $"eventos: {list.Count}");
    Assert(list[0].App == "Photoshop.exe" && list[0].Module == "nvoglv64.dll" && list[0].ExceptionCode == "c0000005", list[0].ToString());
    Assert(list[0].Hint.Contains("NVIDIA"), list[0].Hint);
    Assert(list[1].App == "chrome.exe" && list[1].Module == "ntdll.dll", list[1].ToString());
    Assert(CrashLog.Parse("basura <<").Count == 0, "XML dañado");
}

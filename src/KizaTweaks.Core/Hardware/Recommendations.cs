using KizaTweaks.Core.Engine;

namespace KizaTweaks.Core.Hardware;

/// <summary>
/// Convierte la detección de hardware en acciones concretas, ordenadas por impacto real.
/// Lógica pura (sin acceso al sistema) para poder probarla.
/// </summary>
public static class RecommendationEngine
{
    // Velocidades JEDEC típicas sin XMP/EXPO
    private static readonly HashSet<int> JedecDdr4 = new() { 2133, 2400, 2666 };
    private static readonly HashSet<int> JedecDdr5 = new() { 4800, 5200, 5600 };

    public static List<Recommendation> Build(HardwareReport r, Func<string, TweakState>? state = null, DateTime? now = null)
    {
        var today = now ?? DateTime.Now;
        var list = new List<Recommendation>();
        state ??= _ => TweakState.Unknown;
        const long GB = 1024L * 1024 * 1024;

        // ---- Monitor: lo que más se nota al jugar ----
        foreach (var d in r.Displays.Where(d => d.MaxHz > d.CurrentHz + 1 && d.CurrentHz > 0))
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.High,
                Title = $"Tu monitor está a {d.CurrentHz} Hz pero soporta {d.MaxHz} Hz",
                Detail = $"{d.MonitorName} ({d.Width}×{d.Height}). Súbelo en Configuración > Pantalla > Pantalla avanzada. Es el cambio más notorio al jugar.",
                SettingsUri = "ms-settings:display-advanced",
            });
        }

        // ---- RAM / XMP ----
        var ram = r.Ram.Where(m => m.ConfiguredMhz > 0).ToList();
        if (ram.Count > 0)
        {
            var conf = ram.Min(m => m.ConfiguredMhz);
            var rated = ram.Max(m => m.RatedMhz);
            if (rated > conf + 100)
            {
                list.Add(new Recommendation
                {
                    Severity = RecSeverity.High,
                    Title = $"Tu RAM corre a {conf} MHz pero soporta {rated} MHz",
                    Detail = "Activa el perfil XMP (Intel) o EXPO (AMD) en la BIOS. Suele dar más FPS y mejores 1% low, sobre todo en juegos que dependen del CPU.",
                });
            }
            else if (!r.IsLaptop && ((ram[0].Type == "DDR4" && JedecDdr4.Contains(conf)) || (ram[0].Type == "DDR5" && JedecDdr5.Contains(conf))))
            {
                list.Add(new Recommendation
                {
                    Severity = RecSeverity.Medium,
                    Title = $"Tu RAM {ram[0].Type} está a {conf} MHz (velocidad estándar)",
                    Detail = "Si tus módulos son de mayor velocidad (revisa la etiqueta o el modelo), activa XMP/EXPO en la BIOS.",
                });
            }
            if (ram.Count == 1 && r.RamTotalBytes >= 8 * GB && !r.IsLaptop)
            {
                list.Add(new Recommendation
                {
                    Severity = RecSeverity.Info,
                    Title = "RAM en un solo canal",
                    Detail = "Con un único módulo la memoria trabaja en single channel. Dos módulos iguales (dual channel) suelen mejorar FPS, sobre todo con gráficos integrados.",
                });
            }
        }

        // ---- Discos ----
        var sys = r.SystemDisk;
        var nvme = r.Disks.Where(d => d.Bus == "NVMe").ToList();
        if (sys is not null && sys.Bus != "NVMe" && nvme.Count > 0)
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.Medium,
                Title = "Windows está en un disco más lento que el que tienes disponible",
                Detail = $"El sistema está en {sys.Model} ({sys.Kind}) y tienes {nvme[0].Model} (NVMe), que es mucho más rápido. " +
                         "Instalar Windows en el NVMe acelera arranque y cargas. Como mínimo, instala tus juegos ahí.",
            });
        }
        foreach (var d in r.Disks.Where(d => d.Health is "Advertencia" or "Mala"))
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.High,
                Title = $"Salud del disco {d.Model}: {d.Health}",
                Detail = "Haz copia de seguridad de lo importante cuanto antes y revisa el disco con la herramienta del fabricante.",
            });
        }
        foreach (var d in r.Disks.Where(d => d.TemperatureC >= 70))
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.Medium,
                Title = $"{d.Model} está a {d.TemperatureC} °C",
                Detail = "Un SSD tan caliente reduce su velocidad (throttling). Considera un disipador o mejor flujo de aire.",
            });
        }
        if (r.SystemVolume is { } sv && sv.SizeBytes > 0 && (sv.FreePercent < 15 || sv.FreeBytes < 20 * GB))
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.Medium,
                Title = $"Poco espacio en {sv.Letter}: {sv.FreeBytes / (double)GB:0.#} GB libres",
                Detail = "Windows necesita espacio para actualizaciones y memoria virtual. Mueve juegos y descargas a otro disco.",
                SettingsUri = "ms-settings:storagesense",
            });
        }
        if (r.TrimEnabled == false)
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.Medium,
                Title = "TRIM está desactivado",
                Detail = "Sin TRIM los SSD pierden velocidad con el tiempo. Actívalo con: fsutil behavior set DisableDeleteNotify 0",
            });
        }

        // ---- GPU ----
        var gpu = r.MainGpu;
        if (gpu?.DriverDate is { } dd && (today - dd).TotalDays > 240 && !gpu.IsVirtualOrBasic)
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.Medium,
                Title = $"Driver de GPU de {dd:MMM yyyy}",
                Detail = $"{gpu.Name}: descarga el driver más reciente desde la web de NVIDIA, AMD o Intel. Los drivers nuevos traen optimizaciones para juegos recientes.",
            });
        }
        if (gpu is not null && gpu.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.High,
                Title = "La GPU no tiene driver instalado",
                Detail = "Windows está usando el adaptador básico. Instala el driver de tu tarjeta gráfica.",
            });
        }

        // ---- Ajustes de Windows con efecto en juegos ----
        void Tweak(string id, RecSeverity sev, string title, string detail)
        {
            if (state(id) != TweakState.Applied)
                list.Add(new Recommendation { Severity = sev, Title = title, Detail = detail, TweakId = id });
        }
        if (r.HagsEnabled != true)
            Tweak("kiza.hags", RecSeverity.Medium, "Activa la programación de GPU acelerada por hardware",
                "Frames más estables y requisito para DLSS Frame Generation.");
        if (r.WindowedOptimizations != true)
            Tweak("kiza.windowedOpt", RecSeverity.Medium, "Activa las optimizaciones para juegos en ventana",
                "Menos latencia en juegos en ventana o sin bordes, y habilita VRR.");
        if (r.GameMode == false)
            Tweak("kiza.gameMode", RecSeverity.Medium, "Activa el Modo de juego", "Prioriza el juego y pausa actualizaciones mientras juegas.");
        Tweak("q.DisableDVR", RecSeverity.Medium, "Desactiva la grabación en segundo plano de Xbox",
            "Grabar en segundo plano consume GPU y disco mientras juegas.");
        Tweak("q.DisableMouseAcceleration", RecSeverity.Info, "Desactiva la aceleración del mouse",
            "Movimiento 1:1 para apuntar con precisión.");

        if (r.HibernationEnabled == true && !r.IsLaptop && r.SystemVolume is { } v2 && v2.SizeBytes < 260 * GB)
            Tweak("kiza.hibernate", RecSeverity.Info, "Desactiva la hibernación para liberar espacio",
                $"Libera unos {r.RamTotalBytes / (double)GB:0} GB en {v2.Letter}. No afecta el rendimiento.");

        if (r.HvciRunning == true)
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.Info,
                Title = "Integridad de memoria (HVCI) está activa",
                Detail = "Protege contra drivers maliciosos y puede costar algo de rendimiento en ciertos equipos. Solo apágala si el benchmark muestra una mejora clara.",
                TweakId = "kiza.memoryIntegrity",
            });
        }

        if (r.IsLaptop && r.PowerSchemeGuid is { } g &&
            (g.Equals(Platform.WindowsBackend.KizaUltimateGuid, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(new Recommendation
            {
                Severity = RecSeverity.Info,
                Title = "Plan 'Máximo rendimiento' en un portátil",
                Detail = "Gasta más batería y calienta más. Úsalo solo conectado a la corriente.",
                TweakId = "kiza.powerUltimate",
            });
        }

        return list.OrderBy(x => x.Severity).ToList();
    }

    /// <summary>
    /// Puntaje 0-100: cuántas recomendaciones de alto/medio impacto quedan pendientes.
    /// No mide FPS; mide qué tan bien configurado está el equipo.
    /// </summary>
    public static int Score(IEnumerable<Recommendation> recs)
    {
        var penalty = recs.Sum(r => r.Severity switch { RecSeverity.High => 18, RecSeverity.Medium => 8, _ => 2 });
        return Math.Clamp(100 - penalty, 0, 100);
    }
}

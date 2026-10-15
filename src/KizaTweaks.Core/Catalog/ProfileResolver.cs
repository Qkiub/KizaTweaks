using KizaTweaks.Core.Hardware;

namespace KizaTweaks.Core.Catalog;

public sealed record ProfileInfo(string Id, string Name, string Description, string Glyph);

public sealed record ProfileSelection(IReadOnlySet<string> Ids, IReadOnlyList<string> AdjustedForHardware);

/// <summary>
/// Qué ajustes incluye cada perfil. Los perfiles de rendimiento se adaptan al hardware:
/// en portátil no se fuerza el consumo máximo y en Ryzen X3D de dos chiplets no se toca el
/// estacionamiento de núcleos.
/// </summary>
public static class ProfileResolver
{
    public const string Safe = "safe";
    public const string None = "none";

    public static readonly IReadOnlyList<ProfileInfo> All = new[]
    {
        new ProfileInfo(Profiles.Gamer, "Gamer recomendado", "Rendimiento y limpieza con riesgo bajo. Ideal para empezar.", ""),
        new ProfileInfo(Profiles.Performance, "Máximo rendimiento", "Gamer + plan de energía al máximo, USB y PCIe sin ahorro, prioridad multimedia. Sin quitar nada básico.", ""),
        new ProfileInfo(Profiles.Competitive, "Competitivo", "Todo por FPS y latencia: lo anterior + ajustes experimentales y menos efectos visuales.", ""),
        new ProfileInfo(Profiles.Privacy, "Gamer + privacidad máxima", "Gamer + ubicación, IA de apps y OneDrive fuera.", ""),
        new ProfileInfo(Safe, "Todo lo seguro", "Todos los ajustes marcados como seguros (incluye apariencia).", ""),
    };

    /// <summary>Ajustes que no se incluyen en portátiles dentro de los perfiles de rendimiento.</summary>
    public static readonly IReadOnlySet<string> NotOnLaptop = new HashSet<string>
    {
        "kiza.powerUltimate", "kiza.powerHigh", "kiza.powerThrottling", "kiza.coreParking", "kiza.timerResolution",
    };

    public static bool IsDualCcdX3D(CpuInfo cpu) =>
        cpu.Name.Contains("X3D", StringComparison.OrdinalIgnoreCase) && cpu.Cores > 8;

    public static ProfileSelection Resolve(string profileId, TweakCatalog catalog, HardwareReport? hw)
    {
        var ids = new HashSet<string>();
        var adjusted = new List<string>();
        foreach (var t in catalog.Tweaks)
        {
            if (Includes(profileId, t)) ids.Add(t.Id);
        }

        if (hw is not null && profileId is Profiles.Performance or Profiles.Competitive)
        {
            if (hw.IsLaptop)
            {
                var removed = ids.Where(NotOnLaptop.Contains).ToList();
                if (removed.Count > 0)
                {
                    ids.ExceptWith(removed);
                    adjusted.Add("Es un portátil: no se fuerza el plan de máximo consumo ni los ajustes que gastan batería en reposo.");
                }
            }
            if (IsDualCcdX3D(hw.Cpu) && ids.Remove("kiza.coreParking"))
                adjusted.Add($"{hw.Cpu.Name}: se mantiene el estacionamiento de núcleos (lo usa para elegir los núcleos con caché extra).");
        }
        return new ProfileSelection(ids, adjusted);
    }

    private static bool Includes(string profileId, TweakDefinition t) => profileId switch
    {
        None => false,
        Safe => t.Risk == "safe" && !t.IsExperimental && t.Category != "security",
        Profiles.Performance => t.Profiles.Contains(Profiles.Gamer) || t.Profiles.Contains(Profiles.Performance),
        Profiles.Competitive => t.Profiles.Contains(Profiles.Gamer) || t.Profiles.Contains(Profiles.Performance) || t.Profiles.Contains(Profiles.Competitive),
        _ => t.Profiles.Contains(profileId),
    };

    /// <summary>
    /// Orden de aplicación: primero el plan de energía (los ajustes de energía se escriben en el plan
    /// activo, así que deben ir después de cambiarlo).
    /// </summary>
    public static List<TweakDefinition> ApplyOrder(IEnumerable<TweakDefinition> tweaks) =>
        tweaks.OrderBy(t => t.Apply.Any(o => o.Op == "power.scheme") ? 0 : t.Apply.Any(o => o.Op == "power.setting") ? 2 : 1).ToList();
}

using KizaTweaks.Core.Hardware;

namespace KizaTweaks.Core.Catalog;

public sealed record ProfileInfo(string Id, string Name, string Description, string Glyph);

public sealed record ProfileSelection(IReadOnlySet<string> Ids, IReadOnlyList<string> AdjustedForHardware);

/// <summary>
/// Qué ajustes incluye cada perfil. Los ajustes retirados nunca entran. En portátil no se fuerza el
/// plan de máximo consumo.
/// </summary>
public static class ProfileResolver
{
    public const string Safe = "safe";
    public const string None = "none";

    public static readonly IReadOnlyList<ProfileInfo> All = new[]
    {
        new ProfileInfo(Profiles.Gamer, "Gamer recomendado", "Rendimiento y limpieza sin riesgo. Ideal para empezar.", ""),
        new ProfileInfo(Profiles.Performance, "Máximo rendimiento", "Gamer + plan de energía al máximo y USB/PCIe sin ahorro. Sin quitar nada básico ni cambiar cómo funcionan tus apps y juegos.", ""),
        new ProfileInfo(Profiles.Privacy, "Gamer + privacidad máxima", "Gamer + ubicación, IA de apps y OneDrive fuera.", ""),
        new ProfileInfo(Safe, "Todo lo seguro", "Todos los ajustes marcados como seguros (incluye apariencia).", ""),
    };

    /// <summary>Ajustes que no se incluyen en portátiles dentro del perfil de rendimiento.</summary>
    public static readonly IReadOnlySet<string> NotOnLaptop = new HashSet<string> { "kiza.powerUltimate", "kiza.powerHigh" };

    public static ProfileSelection Resolve(string profileId, TweakCatalog catalog, HardwareReport? hw)
    {
        var ids = new HashSet<string>();
        var adjusted = new List<string>();
        foreach (var t in catalog.Tweaks)
        {
            if (!t.Retired && Includes(profileId, t)) ids.Add(t.Id);
        }

        if (hw is not null && profileId == Profiles.Performance && hw.IsLaptop)
        {
            var removed = ids.Where(NotOnLaptop.Contains).ToList();
            if (removed.Count > 0)
            {
                ids.ExceptWith(removed);
                adjusted.Add("Es un portátil: no se fuerza el plan de máximo consumo.");
            }
        }
        return new ProfileSelection(ids, adjusted);
    }

    private static bool Includes(string profileId, TweakDefinition t) => profileId switch
    {
        None => false,
        Safe => t.Risk == "safe" && !t.IsExperimental && t.Category != "security",
        Profiles.Performance => t.Profiles.Contains(Profiles.Gamer) || t.Profiles.Contains(Profiles.Performance),
        _ => t.Profiles.Contains(profileId),
    };

    /// <summary>
    /// Orden de aplicación: primero el plan de energía (los ajustes de energía se escriben en el plan
    /// activo, así que deben ir después de cambiarlo).
    /// </summary>
    public static List<TweakDefinition> ApplyOrder(IEnumerable<TweakDefinition> tweaks) =>
        tweaks.OrderBy(t => t.Apply.Any(o => o.Op == "power.scheme") ? 0 : t.Apply.Any(o => o.Op == "power.setting") ? 2 : 1).ToList();
}

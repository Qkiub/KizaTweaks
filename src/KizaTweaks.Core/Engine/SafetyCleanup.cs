using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Processes;

namespace KizaTweaks.Core.Engine;

public sealed class CleanupResult
{
    public List<string> Reverted { get; } = new();
    public List<string> PriorityRulesRemoved { get; } = new();
    public List<string> Errors { get; } = new();
    public bool RequiresReboot { get; set; }
    public bool RestartExplorer { get; set; }
    public bool AnythingDone => Reverted.Count > 0 || PriorityRulesRemoved.Count > 0;
}

/// <summary>
/// Deshace lo que versiones anteriores aplicaron y que se retiró por poder causar conflictos
/// (ajustes retirados del catálogo y prioridades altas permanentes por juego).
/// Solo toca lo que tiene respaldo de KizaTweaks: si el usuario activó algo por su cuenta, se respeta.
/// </summary>
public static class SafetyCleanup
{
    /// <summary>Ajustes que la versión 1.0.21 retiró (hoy están en «Avanzado» y solo se activan a mano).</summary>
    public static readonly IReadOnlySet<string> RetiredIn1021 = new HashSet<string>
    {
        "kiza.hags", "kiza.windowedOpt", "kiza.vrr", "kiza.mmcssGames", "kiza.networkThrottling",
        "kiza.powerThrottling", "kiza.disableMpo", "kiza.fseGlobal", "kiza.coreParking", "kiza.win32Priority",
        "kiza.timerResolution", "kiza.vmp", "kiza.memoryIntegrity", "kiza.bgApps", "q.DisableGameBarIntegration",
    };

    public static CleanupResult Run(TweakEngine engine, TweakCatalog catalog, ISystemBackend backend, bool removePriorityRules)
    {
        var result = new CleanupResult();
        foreach (var t in catalog.Tweaks.Where(t => RetiredIn1021.Contains(t.Id) && engine.Store.Get(t.Id) is not null))
        {
            var r = engine.Revert(t);
            if (r.Success) result.Reverted.Add(t.Name);
            else result.Errors.AddRange(r.Errors);
            result.RequiresReboot |= r.RequiresReboot;
            result.RestartExplorer |= r.RestartExplorer;
        }
        if (removePriorityRules)
        {
            foreach (var rule in ProcessService.GetPriorityRules(backend).Where(r => r.CpuPriorityClass == 3))
            {
                try
                {
                    ProcessService.RemovePriorityRule(backend, rule.ExeName);
                    result.PriorityRulesRemoved.Add(rule.ExeName);
                }
                catch (Exception ex) { result.Errors.Add($"{rule.ExeName}: {ex.Message}"); }
            }
        }
        if (result.AnythingDone)
            Log.Info($"Limpieza de seguridad: {result.Reverted.Count} ajustes deshechos, {result.PriorityRulesRemoved.Count} prioridades quitadas.");
        return result;
    }
}

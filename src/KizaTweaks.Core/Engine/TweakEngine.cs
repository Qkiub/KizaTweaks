using System.Text.Json;
using KizaTweaks.Core.Catalog;

namespace KizaTweaks.Core.Engine;

public enum TweakState { Unknown, NotApplied, Partial, Applied }

public sealed class TweakRunResult
{
    public string TweakId { get; init; } = "";
    public string TweakName { get; init; } = "";
    public bool Success => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public bool RequiresReboot { get; set; }
    public bool RestartExplorer { get; set; }
}

public sealed record HistoryEntry(DateTime Time, string Action, string Target, bool Ok, string? Detail);

/// <summary>
/// Aplica y deshace tweaks. Antes de tocar cualquier cosa guarda su estado original,
/// de modo que "Deshacer" deja el sistema exactamente como estaba (no un valor "por defecto").
/// </summary>
public sealed class TweakEngine
{
    private readonly ISystemBackend _b;
    private readonly BackupStore _store;
    private readonly string? _historyFile;
    private readonly object _historyLock = new();

    public TweakEngine(ISystemBackend backend, BackupStore store, string? historyFile = null)
    {
        _b = backend;
        _store = store;
        _historyFile = historyFile;
    }

    public BackupStore Store => _store;

    // =====================================================================
    // Detección
    // =====================================================================

    public TweakState GetState(TweakDefinition t)
    {
        var checks = t.Detect is { Count: > 0 } ? t.Detect : t.Apply;
        int applicable = 0, satisfied = 0;
        foreach (var op in checks)
        {
            bool? ok;
            try { ok = IsSatisfied(op); }
            catch (Exception ex)
            {
                Log.Warn($"No se pudo verificar {op} ({t.Id}): {ex.Message}");
                ok = null;
            }
            if (ok is null) continue;
            applicable++;
            if (ok == true) satisfied++;
        }
        if (applicable == 0) return _store.Get(t.Id) is not null ? TweakState.Applied : TweakState.Unknown;
        if (satisfied == applicable) return TweakState.Applied;
        return satisfied == 0 ? TweakState.NotApplied : TweakState.Partial;
    }

    /// <summary>true/false si se puede verificar; null si no aplica (p. ej. servicio inexistente o comando).</summary>
    private bool? IsSatisfied(TweakOp op)
    {
        switch (op.Op)
        {
            case "reg.set":
                return RegValue.FromOp(op).SameAs(_b.GetValue(op.Hive!, op.Path!, op.Name ?? ""));
            case "reg.deleteValue":
                return _b.GetValue(op.Hive!, op.Path!, op.Name ?? "") is null;
            case "reg.deleteKey":
                return !_b.KeyExists(op.Hive!, op.Path!);
            case "reg.ensureKey":
                return _b.KeyExists(op.Hive!, op.Path!);
            case "service":
            {
                var cur = _b.GetServiceStartType(op.Name!);
                if (cur is null) return null;
                return NormalizeStart(cur) == NormalizeStart(op.StartType!);
            }
            case "task":
            {
                var cur = _b.GetTaskEnabled(op.Path!);
                return cur is null ? null : cur == op.Enabled;
            }
            case "feature":
            {
                var cur = _b.GetFeatureEnabled(op.Name!);
                return cur is null ? null : cur == op.Enabled;
            }
            case "env":
            {
                var want = op.ValueAsString();
                var cur = _b.GetMachineEnv(op.Name!);
                return string.IsNullOrEmpty(want) ? string.IsNullOrEmpty(cur) : cur == want;
            }
            case "dx.set":
                return DxSettings.Get(DxSettings.Read(_b), op.Key!) == op.ValueAsString();
            case "power.scheme":
            {
                var target = _b.ResolvePowerScheme(op.Scheme!, create: false);
                if (target is null) return false;
                return string.Equals(_b.GetActivePowerScheme(), target, StringComparison.OrdinalIgnoreCase);
            }
            case "power.setting":
            {
                if (_b.GetActivePowerScheme() is not { } scheme) return null;
                var cur = _b.GetPowerSetting(scheme, op.Sub!, op.Setting!);
                return cur is null ? null : cur == UInt(op);
            }
            default:
                return null; // cmd y otros: no verificables
        }
    }

    private static string NormalizeStart(string s) => s.ToLowerInvariant() switch
    {
        "auto" or "automatic" => "automatic",
        "delayed-auto" or "automaticdelayed" or "automaticdelayedstart" => "automaticdelayed",
        "demand" or "manual" => "manual",
        "disabled" => "disabled",
        var x => x,
    };

    // =====================================================================
    // Aplicar
    // =====================================================================

    public TweakRunResult Apply(TweakDefinition t)
    {
        var result = new TweakRunResult
        {
            TweakId = t.Id, TweakName = t.Name,
            RequiresReboot = t.RequiresReboot, RestartExplorer = t.RestartExplorer,
        };

        // Si ya existe respaldo (aplicado antes), se conserva el estado ORIGINAL y solo se agrega lo nuevo.
        var backup = _store.Get(t.Id) ?? new TweakBackup { TweakId = t.Id, TweakName = t.Name, AppliedAt = DateTime.Now };
        var seen = backup.Entries.Select(e => e.TargetKey).ToHashSet();

        foreach (var op in t.Apply)
        {
            try
            {
                foreach (var entry in Capture(op))
                {
                    if (seen.Add(entry.TargetKey)) backup.Entries.Add(entry);
                }
                // Se guarda el respaldo ANTES de modificar, por si la app se cierra a mitad de camino.
                _store.Put(backup);
                Execute(op);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{op}: {ex.Message}");
                Log.Error($"Fallo aplicando {t.Id} → {op}", ex);
            }
        }

        Record("apply", t.Name, result.Success, result.Success ? null : string.Join("; ", result.Errors));
        Log.Info($"Aplicado {t.Id} ({(result.Success ? "ok" : $"{result.Errors.Count} errores")})");
        return result;
    }

    /// <summary>Lee y devuelve el estado actual de lo que la operación va a modificar.</summary>
    private IEnumerable<BackupEntry> Capture(TweakOp op)
    {
        switch (op.Op)
        {
            case "reg.set":
            case "reg.deleteValue":
                yield return new RegValueBackup
                {
                    Hive = op.Hive!, Path = op.Path!, Name = op.Name ?? "",
                    Previous = _b.GetValue(op.Hive!, op.Path!, op.Name ?? ""),
                    KeyExisted = _b.KeyExists(op.Hive!, op.Path!),
                    CreatedRoot = op.Op == "reg.set" ? FirstMissing(op.Hive!, op.Path!) : null,
                };
                break;
            case "reg.deleteKey":
            case "reg.ensureKey":
                yield return new RegKeyBackup
                {
                    Hive = op.Hive!, Path = op.Path!,
                    Snapshot = _b.ExportKey(op.Hive!, op.Path!),
                    CreatedRoot = op.Op == "reg.ensureKey" ? FirstMissing(op.Hive!, op.Path!) : null,
                };
                break;
            case "service":
                yield return new ServiceBackup { Name = op.Name!, StartType = _b.GetServiceStartType(op.Name!) };
                break;
            case "task":
                yield return new TaskBackup { Path = op.Path!, Enabled = _b.GetTaskEnabled(op.Path!) };
                break;
            case "env":
                yield return new EnvBackup { Name = op.Name!, Value = _b.GetMachineEnv(op.Name!) };
                break;
            case "feature":
                yield return new FeatureBackup { Name = op.Name!, Enabled = _b.GetFeatureEnabled(op.Name!) };
                break;
            case "dx.set":
                yield return new DxBackup { Key = op.Key!, Previous = DxSettings.Get(DxSettings.Read(_b), op.Key!) };
                break;
            case "power.scheme":
                yield return new PowerBackup { PreviousScheme = _b.GetActivePowerScheme() };
                break;
            case "power.setting":
                if (_b.GetActivePowerScheme() is { } scheme)
                    yield return new PowerSettingBackup
                    {
                        Scheme = scheme, Sub = op.Sub!, Setting = op.Setting!, Dc = op.Dc == true,
                        Previous = _b.GetPowerSetting(scheme, op.Sub!, op.Setting!),
                    };
                break;
        }
    }

    private void Execute(TweakOp op)
    {
        switch (op.Op)
        {
            case "reg.set":
                _b.SetValue(op.Hive!, op.Path!, op.Name ?? "", RegValue.FromOp(op));
                break;
            case "reg.deleteValue":
                _b.DeleteValue(op.Hive!, op.Path!, op.Name ?? "");
                break;
            case "reg.deleteKey":
                _b.DeleteKey(op.Hive!, op.Path!);
                break;
            case "reg.ensureKey":
                _b.CreateKey(op.Hive!, op.Path!);
                break;
            case "service":
                if (_b.GetServiceStartType(op.Name!) is null)
                {
                    Log.Info($"Servicio {op.Name} no existe en este equipo; se omite.");
                    break;
                }
                _b.SetServiceStartType(op.Name!, op.StartType!);
                break;
            case "task":
                if (_b.GetTaskEnabled(op.Path!) is null)
                {
                    Log.Info($"Tarea {op.Path} no existe en este equipo; se omite.");
                    break;
                }
                _b.SetTaskEnabled(op.Path!, op.Enabled == true);
                break;
            case "env":
                _b.SetMachineEnv(op.Name!, op.ValueAsString());
                break;
            case "feature":
                if (_b.GetFeatureEnabled(op.Name!) is null)
                {
                    Log.Info($"Característica {op.Name} no existe en este Windows; se omite.");
                    break;
                }
                _b.SetFeatureEnabled(op.Name!, op.Enabled == true);
                break;
            case "dx.set":
                DxSettings.Write(_b, DxSettings.With(DxSettings.Read(_b), op.Key!, op.ValueAsString()));
                break;
            case "power.scheme":
            {
                var guid = _b.ResolvePowerScheme(op.Scheme!, create: true)
                           ?? throw new InvalidOperationException($"No se pudo crear el plan de energía '{op.Scheme}'.");
                _b.SetActivePowerScheme(guid);
                break;
            }
            case "power.setting":
            {
                var scheme = _b.GetActivePowerScheme() ?? throw new InvalidOperationException("No se pudo leer el plan de energía activo.");
                if (_b.GetPowerSetting(scheme, op.Sub!, op.Setting!) is null)
                {
                    Log.Info($"El ajuste de energía {op.Setting} no existe en este equipo; se omite.");
                    break;
                }
                _b.SetPowerSetting(scheme, op.Sub!, op.Setting!, UInt(op), op.Dc == true);
                break;
            }
            case "cmd":
            {
                var r = _b.Run(op.File!, op.Args ?? "");
                if (!r.Ok) throw new InvalidOperationException($"'{op.File} {op.Args}' terminó con código {r.ExitCode}: {Trim(r.Output)}");
                break;
            }
            default:
                throw new NotSupportedException($"Operación desconocida: {op.Op}");
        }
    }

    // =====================================================================
    // Deshacer
    // =====================================================================

    /// <summary>
    /// Restaura el estado original guardado. Si no hay respaldo (p. ej. el tweak se aplicó con otra
    /// herramienta), usa las operaciones "undo" del catálogo.
    /// </summary>
    public TweakRunResult Revert(TweakDefinition t)
    {
        var result = new TweakRunResult
        {
            TweakId = t.Id, TweakName = t.Name,
            RequiresReboot = t.RequiresReboot, RestartExplorer = t.RestartExplorer,
        };
        var backup = _store.Get(t.Id);
        if (backup is null)
        {
            foreach (var op in t.Undo) RunSafe(op, result, () => Execute(op));
        }
        else
        {
            // Primero los comandos de deshacer (no tienen respaldo), luego los estados exactos.
            foreach (var op in t.Undo.Where(o => o.Op == "cmd")) RunSafe(op, result, () => Execute(op));
            for (int i = backup.Entries.Count - 1; i >= 0; i--)
            {
                var e = backup.Entries[i];
                try { Restore(e); }
                catch (Exception ex)
                {
                    result.Errors.Add($"{e.TargetKey}: {ex.Message}");
                    Log.Error($"Fallo restaurando {t.Id} → {e.TargetKey}", ex);
                }
            }
            if (result.Success) _store.Remove(t.Id);
        }
        Record("undo", t.Name, result.Success, result.Success ? null : string.Join("; ", result.Errors));
        Log.Info($"Deshecho {t.Id} ({(result.Success ? "ok" : $"{result.Errors.Count} errores")})");
        return result;
    }

    /// <summary>Primer componente de la ruta que no existe (lo que crearía escribir ahí), o null si ya existe.</summary>
    private string? FirstMissing(string hive, string path)
    {
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i <= parts.Length; i++)
        {
            var p = string.Join('\\', parts.Take(i));
            if (!_b.KeyExists(hive, p)) return p;
        }
        return null;
    }

    /// <summary>Borra una clave creada por un tweak solo si no contiene ningún valor en todo su árbol.</summary>
    private void RemoveIfUnused(string hive, string path)
    {
        if (!_b.KeyExists(hive, path)) return;
        var snap = _b.ExportKey(hive, path);
        if (snap is null || !HasValues(snap)) _b.DeleteKey(hive, path);
    }

    private static bool HasValues(RegKeySnapshot s) => s.Values.Count > 0 || s.SubKeys.Values.Any(HasValues);

    private void RunSafe(TweakOp op, TweakRunResult result, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            result.Errors.Add($"{op}: {ex.Message}");
            Log.Error($"Fallo en {op}", ex);
        }
    }

    private void Restore(BackupEntry e)
    {
        switch (e)
        {
            case RegValueBackup v:
                if (v.Previous is null) _b.DeleteValue(v.Hive, v.Path, v.Name);
                else _b.SetValue(v.Hive, v.Path, v.Name, v.Previous);
                if (!v.KeyExisted) RemoveIfUnused(v.Hive, v.CreatedRoot ?? v.Path);
                break;
            case RegKeyBackup k:
                if (k.Snapshot is null)
                {
                    RemoveIfUnused(k.Hive, k.CreatedRoot ?? k.Path);
                }
                else
                {
                    _b.ImportKey(k.Hive, k.Path, k.Snapshot);
                }
                break;
            case ServiceBackup s:
                if (s.StartType is not null && _b.GetServiceStartType(s.Name) is not null)
                    _b.SetServiceStartType(s.Name, s.StartType);
                break;
            case TaskBackup tb:
                if (tb.Enabled is not null && _b.GetTaskEnabled(tb.Path) is not null)
                    _b.SetTaskEnabled(tb.Path, tb.Enabled.Value);
                break;
            case EnvBackup env:
                _b.SetMachineEnv(env.Name, env.Value);
                break;
            case FeatureBackup f:
                if (f.Enabled is not null && _b.GetFeatureEnabled(f.Name) is { } cur && cur != f.Enabled)
                    _b.SetFeatureEnabled(f.Name, f.Enabled.Value);
                break;
            case DxBackup d:
                DxSettings.Write(_b, DxSettings.With(DxSettings.Read(_b), d.Key, d.Previous));
                break;
            case PowerBackup p:
                if (!string.IsNullOrEmpty(p.PreviousScheme)) _b.SetActivePowerScheme(p.PreviousScheme);
                break;
            case PowerSettingBackup ps:
                if (ps.Previous is { } prev && _b.GetPowerSetting(ps.Scheme, ps.Sub, ps.Setting) is not null)
                    _b.SetPowerSetting(ps.Scheme, ps.Sub, ps.Setting, prev, ps.Dc);
                break;
        }
    }

    // =====================================================================
    // Historial
    // =====================================================================

    public void Record(string action, string target, bool ok, string? detail)
    {
        if (_historyFile is null) return;
        try
        {
            lock (_historyLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
                File.AppendAllText(_historyFile,
                    JsonSerializer.Serialize(new HistoryEntry(DateTime.Now, action, target, ok, detail)) + Environment.NewLine);
            }
        }
        catch (Exception ex) { Log.Error("No se pudo escribir el historial", ex); }
    }

    public List<HistoryEntry> ReadHistory(int max = 200)
    {
        if (_historyFile is null || !File.Exists(_historyFile)) return new();
        var list = new List<HistoryEntry>();
        foreach (var line in File.ReadLines(_historyFile).Reverse().Take(max))
        {
            try
            {
                if (JsonSerializer.Deserialize<HistoryEntry>(line) is { } h) list.Add(h);
            }
            catch { /* línea dañada */ }
        }
        return list;
    }

    private static uint UInt(TweakOp op) =>
        op.Value.ValueKind == JsonValueKind.Number ? op.Value.GetUInt32() : uint.Parse(op.ValueAsString() ?? "0");

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s.Trim();
}

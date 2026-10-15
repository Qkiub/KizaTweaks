using KizaTweaks.Core.Engine;

namespace KizaTweaks.Tests;

/// <summary>Sistema simulado en memoria para probar el motor sin tocar Windows.</summary>
public sealed class InMemoryBackend : ISystemBackend
{
    private sealed class Node
    {
        public Dictionary<string, RegValue> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // clave: "HIVE\path" en minúsculas → nodo. Se guarda también el nombre original.
    private readonly Dictionary<string, Node> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _original = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Env { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> Features { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActiveScheme { get; set; } = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public HashSet<string> Schemes { get; } = new(StringComparer.OrdinalIgnoreCase) { "381b4222-f694-41f0-9685-ff5bb260df2e" };
    public List<string> Commands { get; } = new();

    private static string K(string hive, string path) => $"{hive.ToUpperInvariant()}\\{path.Trim('\\')}";

    public RegValue? GetValue(string hive, string path, string name) =>
        _keys.TryGetValue(K(hive, path), out var n) && n.Values.TryGetValue(name, out var v) ? v : null;

    public void SetValue(string hive, string path, string name, RegValue value)
    {
        CreateKey(hive, path);
        _keys[K(hive, path)].Values[name] = value;
    }

    public void DeleteValue(string hive, string path, string name)
    {
        if (_keys.TryGetValue(K(hive, path), out var n)) n.Values.Remove(name);
    }

    public bool KeyExists(string hive, string path) => _keys.ContainsKey(K(hive, path));

    public bool KeyIsEmpty(string hive, string path)
    {
        var k = K(hive, path);
        if (!_keys.TryGetValue(k, out var n)) return true;
        return n.Values.Count == 0 && !_keys.Keys.Any(x => x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public void CreateKey(string hive, string path)
    {
        var parts = path.Trim('\\').Split('\\');
        for (int i = 1; i <= parts.Length; i++)
        {
            var k = K(hive, string.Join('\\', parts.Take(i)));
            if (!_keys.ContainsKey(k))
            {
                _keys[k] = new Node();
                _original[k] = k;
            }
        }
    }

    public void DeleteKey(string hive, string path)
    {
        var k = K(hive, path);
        foreach (var x in _keys.Keys.Where(x => x.Equals(k, StringComparison.OrdinalIgnoreCase)
                                                 || x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _keys.Remove(x);
            _original.Remove(x);
        }
    }

    public RegKeySnapshot? ExportKey(string hive, string path)
    {
        var k = K(hive, path);
        return _keys.ContainsKey(k) ? Snap(k) : null;
    }

    private RegKeySnapshot Snap(string k)
    {
        var s = new RegKeySnapshot();
        foreach (var (name, v) in _keys[k].Values) s.Values[name] = v;
        foreach (var child in _keys.Keys.Where(x => x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase)
                                                     && !x[(k.Length + 1)..].Contains('\\')))
        {
            var name = _original[child][(k.Length + 1)..];
            s.SubKeys[name] = Snap(child);
        }
        return s;
    }

    public void ImportKey(string hive, string path, RegKeySnapshot snapshot)
    {
        CreateKey(hive, path);
        foreach (var (name, v) in snapshot.Values) SetValue(hive, path, name, v);
        foreach (var (name, sub) in snapshot.SubKeys) ImportKey(hive, path + "\\" + name, sub);
    }

    /// <summary>Volcado determinista de todo el registro simulado (para comparar estados).</summary>
    public string Dump() => string.Join("\n", _keys.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
        .Select(k => k.Key.ToLowerInvariant() + " {" + string.Join(", ",
            k.Value.Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => $"{v.Key.ToLowerInvariant()}={v.Value}")) + "}"))
        + "\nservices:" + string.Join(",", Services.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}"))
        + "\ntasks:" + string.Join(",", Tasks.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}"))
        + "\nenv:" + string.Join(",", Env.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}"))
        + "\nfeatures:" + string.Join(",", Features.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}"))
        + "\npower:" + ActiveScheme
        + "\npowerSettings:" + string.Join(",", PowerSettings.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}"));

    public string? GetServiceStartType(string name) => Services.TryGetValue(name, out var s) ? s : null;
    public void SetServiceStartType(string name, string startType) => Services[name] = startType;
    public bool? GetTaskEnabled(string path) => Tasks.TryGetValue(path, out var e) ? e : null;
    public void SetTaskEnabled(string path, bool enabled) => Tasks[path] = enabled;
    public string? GetMachineEnv(string name) => Env.TryGetValue(name, out var v) ? v : null;
    public void SetMachineEnv(string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) Env.Remove(name); else Env[name] = value;
    }
    public bool? GetFeatureEnabled(string name) => Features.TryGetValue(name, out var v) ? v : null;
    public void SetFeatureEnabled(string name, bool enabled) => Features[name] = enabled;
    public string? GetActivePowerScheme() => ActiveScheme;

    public string? ResolvePowerScheme(string kind, bool create)
    {
        var guid = kind switch
        {
            "balanced" => "381b4222-f694-41f0-9685-ff5bb260df2e",
            "high" => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
            "ultimate" => "6b2d4f1a-5c3e-4a7b-9d8e-1f2a3b4c5d62",
            _ => null,
        };
        if (guid is null) return null;
        if (!Schemes.Contains(guid))
        {
            if (!create) return null;
            Schemes.Add(guid);
        }
        return guid;
    }

    public void SetActivePowerScheme(string guid) => ActiveScheme = guid;

    /// <summary>Ajustes de energía por plan: clave "plan|ajuste".</summary>
    public Dictionary<string, uint> PowerSettings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public uint? GetPowerSetting(string scheme, string subGroup, string setting) =>
        PowerSettings.TryGetValue($"{scheme}|{setting}", out var v) ? v : null;
    public void SetPowerSetting(string scheme, string subGroup, string setting, uint value, bool dc) =>
        PowerSettings[$"{scheme}|{setting}"] = value;

    public CommandResult Run(string file, string args, int timeoutMs = 120000)
    {
        Commands.Add($"{file} {args}");
        return new CommandResult(0, "");
    }
}

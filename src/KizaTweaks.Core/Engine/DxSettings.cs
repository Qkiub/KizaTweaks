namespace KizaTweaks.Core.Engine;

/// <summary>
/// Ajustes globales de DirectX que Windows guarda como texto "Clave=valor;Clave2=valor;"
/// en HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings.
/// Ahí viven "Optimizaciones para juegos en ventana" (SwapEffectUpgradeEnable) y VRR (VRROptimizeEnable).
/// </summary>
public static class DxSettings
{
    public const string Hive = "HKCU";
    public const string Path = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string ValueName = "DirectXUserGlobalSettings";

    public static List<KeyValuePair<string, string>> Parse(string? raw)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = part.IndexOf('=');
            if (i <= 0) continue;
            list.Add(new(part[..i].Trim(), part[(i + 1)..].Trim()));
        }
        return list;
    }

    public static string Format(IEnumerable<KeyValuePair<string, string>> items) =>
        string.Concat(items.Select(kv => $"{kv.Key}={kv.Value};"));

    public static string? Get(string? raw, string key) =>
        Parse(raw).Where(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                  .Select(kv => kv.Value).Cast<string?>().FirstOrDefault();

    /// <summary>Asigna (value != null) o elimina (value == null) una clave, conservando el resto.</summary>
    public static string With(string? raw, string key, string? value)
    {
        var items = Parse(raw);
        var idx = items.FindIndex(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            if (idx >= 0) items.RemoveAt(idx);
        }
        else if (idx >= 0) items[idx] = new(items[idx].Key, value);
        else items.Add(new(key, value));
        return Format(items);
    }

    public static string? Read(ISystemBackend b) => b.GetValue(Hive, Path, ValueName)?.Text;

    public static void Write(ISystemBackend b, string newRaw)
    {
        if (string.IsNullOrEmpty(newRaw)) b.DeleteValue(Hive, Path, ValueName);
        else b.SetValue(Hive, Path, ValueName, RegValue.Str(newRaw));
    }
}

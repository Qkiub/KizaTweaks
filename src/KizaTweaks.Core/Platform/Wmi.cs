using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace KizaTweaks.Core.Platform;

/// <summary>
/// Consultas WMI usando el objeto COM "WbemScripting.SWbemLocator" que trae Windows,
/// sin dependencias externas.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Wmi
{
    public static List<Dictionary<string, object?>> Query(string ns, string wql, params string[] props)
    {
        var rows = new List<Dictionary<string, object?>>();
        object? locatorObj = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                       ?? throw new InvalidOperationException("WMI no disponible");
            locatorObj = Activator.CreateInstance(type);
            dynamic locator = locatorObj!;
            dynamic svc = locator.ConnectServer(".", ns);
            // Modo semisíncrono (sin forward-only) para poder usar Count e ItemIndex,
            // evitando depender de la enumeración COM (IEnumVARIANT).
            dynamic set = svc.ExecQuery(wql, "WQL", 0x10);
            int count = set.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic obj = set.ItemIndex(i);
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in props)
                {
                    // WMI devuelve DBNull para propiedades vacías: se normaliza a null.
                    try { object? v = obj.Properties_.Item(p).Value; row[p] = v is DBNull ? null : v; }
                    catch { row[p] = null; }
                }
                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"WMI {ns} «{wql}» falló: {ex.Message}");
        }
        finally
        {
            if (locatorObj is not null && Marshal.IsComObject(locatorObj)) Marshal.ReleaseComObject(locatorObj);
        }
        return rows;
    }

    public static string? Str(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() : null;

    public static long Long(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return 0;
        return v switch
        {
            // WMI por scripting devuelve uint64 como texto
            string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l
                      : ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ul) ? unchecked((long)ul) : 0,
            _ => Convert.ToInt64(v, CultureInfo.InvariantCulture),
        };
    }

    public static int[] IntArray(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return Array.Empty<int>();
        if (v is Array arr)
            return arr.Cast<object?>().Where(x => x is not null and not DBNull).Select(x => Convert.ToInt32(x, CultureInfo.InvariantCulture)).ToArray();
        return new[] { Convert.ToInt32(v, CultureInfo.InvariantCulture) };
    }

    /// <summary>Convierte fechas CIM "yyyyMMddHHmmss.ffffff+zzz".</summary>
    public static DateTime? CimDate(this Dictionary<string, object?> row, string key)
    {
        var s = row.Str(key);
        if (s is null || s.Length < 14) return null;
        return DateTime.TryParseExact(s[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var d) ? d : null;
    }
}

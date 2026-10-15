using System.Text.Json;
using System.Text.Json.Serialization;
using KizaTweaks.Core.Catalog;

namespace KizaTweaks.Core.Engine;

public enum RegKind { String, ExpandString, MultiString, DWord, QWord, Binary, None }

/// <summary>Valor de registro serializable (para respaldos) y comparable.</summary>
public sealed class RegValue
{
    [JsonPropertyName("kind")] public RegKind Kind { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("multi")] public string[]? Multi { get; set; }
    [JsonPropertyName("number")] public long Number { get; set; }
    [JsonPropertyName("bytes")] public byte[]? Bytes { get; set; }

    public static RegValue Str(string s) => new() { Kind = RegKind.String, Text = s };
    public static RegValue Expand(string s) => new() { Kind = RegKind.ExpandString, Text = s };
    public static RegValue Dword(long n) => new() { Kind = RegKind.DWord, Number = n & 0xFFFFFFFFL };
    public static RegValue Qword(long n) => new() { Kind = RegKind.QWord, Number = n };
    public static RegValue Bin(byte[] b) => new() { Kind = RegKind.Binary, Bytes = b };
    public static RegValue MultiStr(string[] m) => new() { Kind = RegKind.MultiString, Multi = m };

    /// <summary>Compara contenido. Un DWORD y un String numérico NO se consideran iguales.</summary>
    public bool SameAs(RegValue? other)
    {
        if (other is null) return false;
        if (Kind != other.Kind)
        {
            // Algunas apps guardan "0"/"1" como texto donde el tweak espera texto: se respeta el tipo exacto.
            return false;
        }
        return Kind switch
        {
            RegKind.String or RegKind.ExpandString => string.Equals(Text ?? "", other.Text ?? "", StringComparison.Ordinal),
            RegKind.MultiString => (Multi ?? Array.Empty<string>()).SequenceEqual(other.Multi ?? Array.Empty<string>()),
            RegKind.DWord => (Number & 0xFFFFFFFFL) == (other.Number & 0xFFFFFFFFL),
            RegKind.QWord => Number == other.Number,
            RegKind.Binary or RegKind.None => (Bytes ?? Array.Empty<byte>()).SequenceEqual(other.Bytes ?? Array.Empty<byte>()),
            _ => false,
        };
    }

    public override string ToString() => Kind switch
    {
        RegKind.String or RegKind.ExpandString => $"\"{Text}\"",
        RegKind.MultiString => string.Join(" | ", Multi ?? Array.Empty<string>()),
        RegKind.DWord => $"0x{Number & 0xFFFFFFFFL:X8} ({Number & 0xFFFFFFFFL})",
        RegKind.QWord => $"0x{Number:X16}",
        _ => BitConverter.ToString(Bytes ?? Array.Empty<byte>()),
    };

    /// <summary>Convierte el valor de una operación del catálogo.</summary>
    public static RegValue FromOp(TweakOp op)
    {
        var type = op.Type ?? "String";
        var v = op.Value;
        return type switch
        {
            "DWord" => Dword(ReadLong(v)),
            "QWord" => Qword(ReadLong(v)),
            "String" => Str(v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText()),
            "ExpandString" => Expand(v.GetString() ?? ""),
            "MultiString" => MultiStr(v.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()),
            "Binary" => Bin(v.EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray()),
            _ => throw new NotSupportedException($"Tipo de registro no soportado: {type}"),
        };
    }

    private static long ReadLong(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String) return long.Parse(v.GetString()!);
        if (v.TryGetInt64(out var l)) return l;
        // Valores > long.MaxValue (QWORD sin signo)
        return unchecked((long)v.GetUInt64());
    }
}

public sealed class RegKeySnapshot
{
    [JsonPropertyName("values")] public Dictionary<string, RegValue> Values { get; set; } = new();
    [JsonPropertyName("subkeys")] public Dictionary<string, RegKeySnapshot> SubKeys { get; set; } = new();
}

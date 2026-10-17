using System.Text.Json;
using System.Text.Json.Serialization;

namespace KizaTweaks.Core.Catalog;

/// <summary>Una operación atómica de un tweak (escribir registro, cambiar un servicio, etc.).</summary>
public sealed class TweakOp
{
    [JsonPropertyName("op")] public string Op { get; set; } = "";

    // Registro
    [JsonPropertyName("hive")] public string? Hive { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Value { get; set; }

    // dx.set
    [JsonPropertyName("key")] public string? Key { get; set; }

    // service
    [JsonPropertyName("startType")] public string? StartType { get; set; }

    // task / feature
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }

    // power.scheme
    [JsonPropertyName("scheme")] public string? Scheme { get; set; }

    // power.setting (GUIDs de subgrupo y ajuste de powercfg)
    [JsonPropertyName("sub")] public string? Sub { get; set; }
    [JsonPropertyName("setting")] public string? Setting { get; set; }
    [JsonPropertyName("dc")] public bool? Dc { get; set; }

    // cmd
    [JsonPropertyName("file")] public string? File { get; set; }
    [JsonPropertyName("args")] public string? Args { get; set; }

    /// <summary>Valor como texto (para env y dx.set). Devuelve null si el JSON es null/ausente.</summary>
    public string? ValueAsString() => Value.ValueKind switch
    {
        JsonValueKind.String => Value.GetString(),
        JsonValueKind.Number => Value.GetRawText(),
        JsonValueKind.True => "1",
        JsonValueKind.False => "0",
        _ => null,
    };

    public override string ToString() => Op switch
    {
        "reg.set" => $"{Hive}\\{Path} [{Name}] = {Value}",
        "reg.deleteValue" => $"borrar {Hive}\\{Path} [{Name}]",
        "reg.deleteKey" => $"borrar clave {Hive}\\{Path}",
        "service" => $"servicio {Name} → {StartType}",
        "task" => $"tarea {Path} → {(Enabled == true ? "activada" : "desactivada")}",
        "feature" => $"característica {Name} → {(Enabled == true ? "activada" : "desactivada")}",
        "power.scheme" => $"plan de energía {Scheme}",
        "power.setting" => $"energía (plan activo) {Setting} = {ValueAsString()}",
        "dx.set" => $"DirectX {Key}={ValueAsString()}",
        "env" => $"variable {Name}={ValueAsString()}",
        "cmd" => $"{File} {Args}",
        _ => Op,
    };
}

public sealed class TweakDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("note")] public string? Note { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "other";
    /// <summary>fps | latency | privacy | cleanup | appearance | storage | stability</summary>
    [JsonPropertyName("impact")] public string Impact { get; set; } = "cleanup";
    /// <summary>safe | moderate | risky</summary>
    [JsonPropertyName("risk")] public string Risk { get; set; } = "safe";
    [JsonPropertyName("requiresReboot")] public bool RequiresReboot { get; set; }
    [JsonPropertyName("restartExplorer")] public bool RestartExplorer { get; set; }
    [JsonPropertyName("minBuild")] public int? MinBuild { get; set; }
    [JsonPropertyName("maxBuild")] public int? MaxBuild { get; set; }
    [JsonPropertyName("profiles")] public List<string> Profiles { get; set; } = new();
    /// <summary>Retirado: no se muestra ni se aplica; solo se conserva para deshacerlo donde ya estaba aplicado.</summary>
    [JsonPropertyName("retired")] public bool Retired { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("apply")] public List<TweakOp> Apply { get; set; } = new();
    [JsonPropertyName("undo")] public List<TweakOp> Undo { get; set; } = new();
    /// <summary>Comprobaciones para saber si está aplicado cuando "apply" no es verificable (comandos).</summary>
    [JsonPropertyName("detect")] public List<TweakOp>? Detect { get; set; }

    public bool IsCompatible(int build) =>
        (MinBuild is null || build >= MinBuild) && (MaxBuild is null || build <= MaxBuild);

    public bool IsExperimental => Category == "experimental";
}

public sealed class CategoryDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";
}

public sealed class TweakCatalog
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("sources")] public Dictionary<string, string> Sources { get; set; } = new();
    [JsonPropertyName("categories")] public List<CategoryDefinition> Categories { get; set; } = new();
    [JsonPropertyName("tweaks")] public List<TweakDefinition> Tweaks { get; set; } = new();

    public TweakDefinition? Find(string id) => Tweaks.FirstOrDefault(t => t.Id == id);
}

public sealed class AppDefinition
{
    [JsonPropertyName("ids")] public List<string> Ids { get; set; } = new();
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>safe | optional | unsafe</summary>
    [JsonPropertyName("recommendation")] public string Recommendation { get; set; } = "optional";
    [JsonPropertyName("selectedByDefault")] public bool SelectedByDefault { get; set; }
    /// <summary>Appx | WinGet</summary>
    [JsonPropertyName("method")] public string Method { get; set; } = "Appx";

    public string PrimaryId => Ids.FirstOrDefault() ?? Name;
}

public sealed class AppCatalog
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("apps")] public List<AppDefinition> Apps { get; set; } = new();
}

public static class Profiles
{
    public const string Gamer = "gamer";
    public const string Privacy = "privacy";
    public const string Performance = "perf";
}

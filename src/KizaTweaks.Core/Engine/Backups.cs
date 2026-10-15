using System.Text.Json;
using System.Text.Json.Serialization;

namespace KizaTweaks.Core.Engine;

/// <summary>Estado original de algo que un tweak modificó. Se usa para deshacer con exactitud.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RegValueBackup), "regValue")]
[JsonDerivedType(typeof(RegKeyBackup), "regKey")]
[JsonDerivedType(typeof(ServiceBackup), "service")]
[JsonDerivedType(typeof(TaskBackup), "task")]
[JsonDerivedType(typeof(EnvBackup), "env")]
[JsonDerivedType(typeof(FeatureBackup), "feature")]
[JsonDerivedType(typeof(DxBackup), "dx")]
[JsonDerivedType(typeof(PowerBackup), "power")]
[JsonDerivedType(typeof(PowerSettingBackup), "powerSetting")]
public abstract class BackupEntry
{
    /// <summary>Identificador del objetivo, para no respaldar dos veces lo mismo dentro de un tweak.</summary>
    [JsonIgnore] public abstract string TargetKey { get; }
}

public sealed class RegValueBackup : BackupEntry
{
    public string Hive { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>null = el valor no existía.</summary>
    public RegValue? Previous { get; set; }
    /// <summary>Si la clave no existía, al deshacer se borra si queda vacía.</summary>
    public bool KeyExisted { get; set; }
    /// <summary>Primer nivel de la ruta que no existía (lo creó el tweak). Se borra al deshacer si quedó vacío.</summary>
    public string? CreatedRoot { get; set; }
    public override string TargetKey => $"v|{Hive}|{Path.ToLowerInvariant()}|{Name.ToLowerInvariant()}";
}

public sealed class RegKeyBackup : BackupEntry
{
    public string Hive { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>null = la clave no existía.</summary>
    public RegKeySnapshot? Snapshot { get; set; }
    public string? CreatedRoot { get; set; }
    public override string TargetKey => $"k|{Hive}|{Path.ToLowerInvariant()}";
}

public sealed class ServiceBackup : BackupEntry
{
    public string Name { get; set; } = "";
    public string? StartType { get; set; }
    public override string TargetKey => $"s|{Name.ToLowerInvariant()}";
}

public sealed class TaskBackup : BackupEntry
{
    public string Path { get; set; } = "";
    public bool? Enabled { get; set; }
    public override string TargetKey => $"t|{Path.ToLowerInvariant()}";
}

public sealed class EnvBackup : BackupEntry
{
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public override string TargetKey => $"e|{Name.ToLowerInvariant()}";
}

public sealed class FeatureBackup : BackupEntry
{
    public string Name { get; set; } = "";
    public bool? Enabled { get; set; }
    public override string TargetKey => $"f|{Name.ToLowerInvariant()}";
}

public sealed class DxBackup : BackupEntry
{
    public string Key { get; set; } = "";
    public string? Previous { get; set; }
    public override string TargetKey => $"d|{Key.ToLowerInvariant()}";
}

public sealed class PowerBackup : BackupEntry
{
    public string? PreviousScheme { get; set; }
    public override string TargetKey => "p";
}

public sealed class PowerSettingBackup : BackupEntry
{
    public string Scheme { get; set; } = "";
    public string Sub { get; set; } = "";
    public string Setting { get; set; } = "";
    public uint? Previous { get; set; }
    public bool Dc { get; set; }
    public override string TargetKey => $"ps|{Scheme}|{Setting}".ToLowerInvariant();
}

public sealed class TweakBackup
{
    public string TweakId { get; set; } = "";
    public string TweakName { get; set; } = "";
    public DateTime AppliedAt { get; set; }
    public List<BackupEntry> Entries { get; set; } = new();
}

/// <summary>Guarda los respaldos en disco (JSON). Solo se conserva el estado ANTERIOR a la primera aplicación.</summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string? _file;
    private readonly Dictionary<string, TweakBackup> _items;
    private readonly object _lock = new();

    public BackupStore(string? file)
    {
        _file = file;
        _items = new Dictionary<string, TweakBackup>(StringComparer.OrdinalIgnoreCase);
        if (file is not null && File.Exists(file))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<TweakBackup>>(File.ReadAllText(file), Json) ?? new();
                foreach (var b in list) _items[b.TweakId] = b;
            }
            catch (Exception ex)
            {
                // Respaldo corrupto: se conserva una copia y se empieza de cero.
                Log.Error("No se pudo leer el archivo de respaldos", ex);
                try { File.Copy(file, file + ".corrupto-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
            }
        }
    }

    public TweakBackup? Get(string tweakId)
    {
        lock (_lock) return _items.TryGetValue(tweakId, out var b) ? b : null;
    }

    public IReadOnlyList<TweakBackup> All()
    {
        lock (_lock) return _items.Values.OrderByDescending(b => b.AppliedAt).ToList();
    }

    public void Put(TweakBackup backup)
    {
        lock (_lock)
        {
            _items[backup.TweakId] = backup;
            Save();
        }
    }

    public void Remove(string tweakId)
    {
        lock (_lock)
        {
            if (_items.Remove(tweakId)) Save();
        }
    }

    private void Save()
    {
        if (_file is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items.Values.ToList(), Json));
        File.Move(tmp, _file, overwrite: true);
    }
}

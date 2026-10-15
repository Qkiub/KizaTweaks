namespace KizaTweaks.Core.Hardware;

public sealed class CpuInfo
{
    public string Name { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int MaxClockMhz { get; set; }
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public string? DriverVersion { get; set; }
    public DateTime? DriverDate { get; set; }
    public long VramBytes { get; set; }
    public bool IsVirtualOrBasic { get; set; }
}

public sealed class RamModule
{
    public string Slot { get; set; } = "";
    public long CapacityBytes { get; set; }
    /// <summary>Velocidad máxima que reporta el módulo (SPD/SMBIOS).</summary>
    public int RatedMhz { get; set; }
    /// <summary>Velocidad a la que está funcionando ahora.</summary>
    public int ConfiguredMhz { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    /// <summary>DDR3 | DDR4 | DDR5 | ?</summary>
    public string Type { get; set; } = "?";
}

public sealed class DiskInfo
{
    public string DeviceId { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>SSD | HDD | ?</summary>
    public string MediaType { get; set; } = "?";
    /// <summary>NVMe | SATA | USB | RAID | ?</summary>
    public string Bus { get; set; } = "?";
    public long SizeBytes { get; set; }
    /// <summary>Buena | Advertencia | Mala | ?</summary>
    public string Health { get; set; } = "?";
    public int? TemperatureC { get; set; }
    /// <summary>Desgaste en % (0 = nuevo). Solo SSD que lo reportan.</summary>
    public int? WearPercent { get; set; }
    public List<string> Letters { get; set; } = new();
    public bool HasSystem { get; set; }

    public string Kind => MediaType == "HDD" ? "HDD" : Bus == "NVMe" ? "SSD NVMe" : MediaType == "SSD" ? $"SSD {Bus}" : $"{MediaType} {Bus}";
}

public sealed class VolumeInfo
{
    public string Letter { get; set; } = "";
    public string? Label { get; set; }
    public long SizeBytes { get; set; }
    public long FreeBytes { get; set; }
    public double FreePercent => SizeBytes > 0 ? FreeBytes * 100.0 / SizeBytes : 0;
}

public sealed class DisplayInfo
{
    public string DeviceName { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int CurrentHz { get; set; }
    /// <summary>Máximo Hz disponible en la resolución actual.</summary>
    public int MaxHz { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class HardwareReport
{
    public string Os { get; set; } = "";
    public int Build { get; set; }
    public string ComputerName { get; set; } = Environment.MachineName;
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public List<RamModule> Ram { get; set; } = new();
    public long RamTotalBytes { get; set; }
    public string? Motherboard { get; set; }
    public string? Bios { get; set; }
    public DateTime? BiosDate { get; set; }
    public bool IsLaptop { get; set; }
    public List<DiskInfo> Disks { get; set; } = new();
    public List<VolumeInfo> Volumes { get; set; } = new();
    public List<DisplayInfo> Displays { get; set; } = new();
    public string SystemDrive { get; set; } = "C:";
    public bool? TrimEnabled { get; set; }

    // Estado de Windows relevante para juegos
    public bool? VbsRunning { get; set; }
    public bool? HvciRunning { get; set; }
    public bool? HagsEnabled { get; set; }
    public bool? WindowedOptimizations { get; set; }
    public bool? GameMode { get; set; }
    public bool? HibernationEnabled { get; set; }
    public string? PowerSchemeGuid { get; set; }
    public string? PowerSchemeName { get; set; }

    public DiskInfo? SystemDisk => Disks.FirstOrDefault(d => d.HasSystem);
    public VolumeInfo? SystemVolume => Volumes.FirstOrDefault(v => v.Letter.Equals(SystemDrive, StringComparison.OrdinalIgnoreCase));
    public GpuInfo? MainGpu => Gpus.Where(g => !g.IsVirtualOrBasic).OrderByDescending(g => g.VramBytes).FirstOrDefault() ?? Gpus.FirstOrDefault();
}

public enum RecSeverity { High, Medium, Info }

public sealed class Recommendation
{
    public RecSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>Tweak del catálogo que lo resuelve (si aplica).</summary>
    public string? TweakId { get; init; }
    /// <summary>URI de Windows para resolverlo a mano (ms-settings:...).</summary>
    public string? SettingsUri { get; init; }
}

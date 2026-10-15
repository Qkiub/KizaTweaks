using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Platform;
using Microsoft.Win32;

namespace KizaTweaks.Core.Hardware;

[SupportedOSPlatform("windows")]
public sealed class HardwareService
{
    private readonly ISystemBackend _b;
    public HardwareService(ISystemBackend backend) => _b = backend;

    public HardwareReport Collect()
    {
        var r = new HardwareReport
        {
            Os = SystemActions.OsDisplayVersion(),
            Build = SystemActions.OsBuild(),
            SystemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\'),
        };
        Safe("CPU", () => r.Cpu = Cpu());
        Safe("GPU", () => r.Gpus = Gpus());
        Safe("RAM", () => Ram(r));
        Safe("Placa", () => Board(r));
        Safe("Discos", () => Disks(r));
        Safe("Pantallas", () => r.Displays = Displays.Query());
        Safe("TRIM", () => r.TrimEnabled = Trim());
        Safe("VBS", () => Vbs(r));
        Safe("Estado de juego", () => GamingState(r));
        return r;
    }

    private static void Safe(string what, Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Warn($"Detección de {what} falló: {ex.Message}"); }
    }

    private static CpuInfo Cpu()
    {
        var row = Wmi.Query(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
            "Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed").FirstOrDefault();
        if (row is null) return new CpuInfo { Name = "Desconocido", Threads = Environment.ProcessorCount };
        return new CpuInfo
        {
            Name = Regex.Replace(row.Str("Name") ?? "", @"\s+", " "),
            Cores = (int)row.Long("NumberOfCores"),
            Threads = (int)row.Long("NumberOfLogicalProcessors"),
            MaxClockMhz = (int)row.Long("MaxClockSpeed"),
        };
    }

    private static List<GpuInfo> Gpus()
    {
        var vram = GpuVramFromRegistry();
        var list = new List<GpuInfo>();
        foreach (var row in Wmi.Query(@"root\cimv2", "SELECT Name, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController",
                     "Name", "DriverVersion", "DriverDate", "AdapterRAM"))
        {
            var name = row.Str("Name") ?? "GPU";
            var basic = name.Contains("Basic", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Parsec", StringComparison.OrdinalIgnoreCase);
            list.Add(new GpuInfo
            {
                Name = name,
                DriverVersion = row.Str("DriverVersion"),
                DriverDate = row.CimDate("DriverDate"),
                VramBytes = vram.TryGetValue(name, out var v) ? v : row.Long("AdapterRAM"),
                IsVirtualOrBasic = basic,
            });
        }
        return list;
    }

    /// <summary>AdapterRAM de WMI se queda en 4 GB; el valor real está en la clave de clase del adaptador.</summary>
    private static Dictionary<string, long> GpuVramFromRegistry()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return map;
        foreach (var sub in cls.GetSubKeyNames().Where(s => s.Length == 4 && s.All(char.IsDigit)))
        {
            try
            {
                using var k = cls.OpenSubKey(sub);
                var desc = k?.GetValue("DriverDesc") as string;
                var mem = k?.GetValue("HardwareInformation.qwMemorySize");
                if (desc is null || mem is null) continue;
                long bytes = mem switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                    int i => unchecked((uint)i),
                    _ => 0,
                };
                if (bytes > 0) map[desc] = bytes;
            }
            catch { }
        }
        return map;
    }

    private static void Ram(HardwareReport r)
    {
        foreach (var row in Wmi.Query(@"root\cimv2",
                     "SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory",
                     "DeviceLocator", "Capacity", "Speed", "ConfiguredClockSpeed", "Manufacturer", "PartNumber", "SMBIOSMemoryType"))
        {
            r.Ram.Add(new RamModule
            {
                Slot = row.Str("DeviceLocator") ?? "",
                CapacityBytes = row.Long("Capacity"),
                RatedMhz = (int)row.Long("Speed"),
                ConfiguredMhz = (int)row.Long("ConfiguredClockSpeed"),
                Manufacturer = row.Str("Manufacturer"),
                PartNumber = row.Str("PartNumber"),
                Type = row.Long("SMBIOSMemoryType") switch
                {
                    24 => "DDR3", 26 => "DDR4", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "?",
                },
            });
        }
        r.RamTotalBytes = r.Ram.Sum(m => m.CapacityBytes);
        if (r.RamTotalBytes == 0 && Native.TryGetMemory(out var total, out _)) r.RamTotalBytes = (long)total;
    }

    private static void Board(HardwareReport r)
    {
        var mb = Wmi.Query(@"root\cimv2", "SELECT Manufacturer, Product FROM Win32_BaseBoard", "Manufacturer", "Product").FirstOrDefault();
        if (mb is not null) r.Motherboard = $"{mb.Str("Manufacturer")} {mb.Str("Product")}".Trim();
        var bios = Wmi.Query(@"root\cimv2", "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", "SMBIOSBIOSVersion", "ReleaseDate").FirstOrDefault();
        if (bios is not null) { r.Bios = bios.Str("SMBIOSBIOSVersion"); r.BiosDate = bios.CimDate("ReleaseDate"); }

        var chassis = Wmi.Query(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure", "ChassisTypes")
                         .SelectMany(x => x.IntArray("ChassisTypes")).ToHashSet();
        var portable = new[] { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
        var hasBattery = Wmi.Query(@"root\cimv2", "SELECT Name FROM Win32_Battery", "Name").Count > 0;
        r.IsLaptop = hasBattery || chassis.Overlaps(portable);
    }

    private static void Disks(HardwareReport r)
    {
        const string ns = @"root\Microsoft\Windows\Storage";
        var rel = Wmi.Query(ns, "SELECT DeviceId, Temperature, Wear FROM MSFT_StorageReliabilityCounter", "DeviceId", "Temperature", "Wear")
                     .Where(x => x.Str("DeviceId") is not null)
                     .GroupBy(x => x.Str("DeviceId")!).ToDictionary(g => g.Key, g => g.First());

        var letters = new Dictionary<string, List<string>>();
        foreach (var p in Wmi.Query(ns, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition", "DiskNumber", "DriveLetter"))
        {
            var letter = DriveLetter(p.TryGetValue("DriveLetter", out var dl) ? dl : null);
            if (letter is null) continue;
            var disk = p.Long("DiskNumber").ToString();
            if (!letters.TryGetValue(disk, out var l)) letters[disk] = l = new();
            l.Add(letter + ":");
        }

        foreach (var row in Wmi.Query(ns, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk",
                     "DeviceId", "FriendlyName", "MediaType", "BusType", "Size", "HealthStatus"))
        {
            var id = row.Str("DeviceId") ?? "";
            var d = new DiskInfo
            {
                DeviceId = id,
                Model = row.Str("FriendlyName") ?? "Disco",
                MediaType = row.Long("MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "?" },
                Bus = row.Long("BusType") switch { 17 => "NVMe", 11 => "SATA", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 1 => "SCSI", _ => "?" },
                SizeBytes = row.Long("Size"),
                Health = row.Long("HealthStatus") switch { 0 => "Buena", 1 => "Advertencia", 2 => "Mala", _ => "?" },
            };
            if (d.Bus == "NVMe" && d.MediaType == "?") d.MediaType = "SSD";
            if (rel.TryGetValue(id, out var rr))
            {
                var t = rr.Long("Temperature");
                if (t > 0 && t < 120) d.TemperatureC = (int)t;
                if (rr.TryGetValue("Wear", out var w) && w is not null) d.WearPercent = (int)rr.Long("Wear");
            }
            if (letters.TryGetValue(id, out var ls)) d.Letters = ls.OrderBy(x => x).ToList();
            d.HasSystem = d.Letters.Contains(r.SystemDrive, StringComparer.OrdinalIgnoreCase);
            r.Disks.Add(d);
        }

        foreach (var v in Wmi.Query(@"root\cimv2", "SELECT DeviceID, VolumeName, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3",
                     "DeviceID", "VolumeName", "Size", "FreeSpace"))
        {
            r.Volumes.Add(new VolumeInfo
            {
                Letter = v.Str("DeviceID") ?? "",
                Label = v.Str("VolumeName"),
                SizeBytes = v.Long("Size"),
                FreeBytes = v.Long("FreeSpace"),
            });
        }
    }

    private static string? DriveLetter(object? v) => v switch
    {
        null => null,
        string s when s.Length > 0 && char.IsLetter(s[0]) => s[0].ToString().ToUpperInvariant(),
        string => null,
        char c when char.IsLetter(c) => c.ToString().ToUpperInvariant(),
        IConvertible n when Convert.ToInt32(n) is var code && code >= 'A' && code <= 'z' && char.IsLetter((char)code)
            => ((char)code).ToString().ToUpperInvariant(),
        _ => null,
    };

    private static bool? Trim()
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("fsutil.exe"), "behavior query DisableDeleteNotify", 15_000);
        var m = Regex.Match(r.Output, @"NTFS\s+DisableDeleteNotify\s*=\s*(\d)");
        return m.Success ? m.Groups[1].Value == "0" : null;
    }

    private static void Vbs(HardwareReport r)
    {
        var row = Wmi.Query(@"root\Microsoft\Windows\DeviceGuard",
            "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard",
            "VirtualizationBasedSecurityStatus", "SecurityServicesRunning").FirstOrDefault();
        if (row is null) return;
        r.VbsRunning = row.Long("VirtualizationBasedSecurityStatus") == 2;
        r.HvciRunning = row.IntArray("SecurityServicesRunning").Contains(2);
    }

    private void GamingState(HardwareReport r)
    {
        var hags = _b.GetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        r.HagsEnabled = hags is null ? null : hags.Number == 2;

        r.WindowedOptimizations = DxSettings.Get(DxSettings.Read(_b), "SwapEffectUpgradeEnable") == "1";

        var gm = _b.GetValue("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
        r.GameMode = gm is null || gm.Number != 0; // por defecto está activado

        var hib = _b.GetValue("HKLM", @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
        r.HibernationEnabled = hib is not null ? hib.Number != 0 : File.Exists(Path.Combine(r.SystemDrive + "\\", "hiberfil.sys"));

        var pc = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), "/getactivescheme", 15_000);
        var gm2 = Regex.Match(pc.Output, @"([0-9a-fA-F-]{36})\s*\(([^)]*)\)");
        if (gm2.Success)
        {
            r.PowerSchemeGuid = gm2.Groups[1].Value.ToLowerInvariant();
            r.PowerSchemeName = gm2.Groups[2].Value.Trim();
        }
    }
}

[SupportedOSPlatform("windows")]
internal static class Displays
{
    public static List<DisplayInfo> Query()
    {
        var list = new List<DisplayInfo>();
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++, dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>())
        {
            const int ATTACHED = 0x1, PRIMARY = 0x4;
            if ((dd.StateFlags & ATTACHED) == 0) continue;

            var cur = NewDevMode();
            if (!EnumDisplaySettings(dd.DeviceName, -1 /* ENUM_CURRENT_SETTINGS */, ref cur)) continue;

            int max = cur.dmDisplayFrequency;
            var mode = NewDevMode();
            for (int m = 0; EnumDisplaySettings(dd.DeviceName, m, ref mode); m++)
            {
                if (mode.dmPelsWidth == cur.dmPelsWidth && mode.dmPelsHeight == cur.dmPelsHeight && mode.dmDisplayFrequency > max)
                    max = mode.dmDisplayFrequency;
            }

            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            var monitorName = EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0) ? mon.DeviceString : dd.DeviceString;

            list.Add(new DisplayInfo
            {
                DeviceName = dd.DeviceName,
                MonitorName = string.IsNullOrWhiteSpace(monitorName) ? dd.DeviceName : monitorName,
                Width = cur.dmPelsWidth,
                Height = cur.dmPelsHeight,
                CurrentHz = cur.dmDisplayFrequency,
                MaxHz = max,
                IsPrimary = (dd.StateFlags & PRIMARY) != 0,
            });
        }
        return list.OrderByDescending(d => d.IsPrimary).ToList();
    }

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
}

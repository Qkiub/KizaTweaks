using System.Runtime.InteropServices;

namespace KizaTweaks.Core.Platform;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    public static bool TryGetMemory(out ulong total, out ulong available)
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            total = m.ullTotalPhys;
            available = m.ullAvailPhys;
            return true;
        }
        total = available = 0;
        return false;
    }

    /// <summary>% de uso total de CPU medido durante el intervalo indicado.</summary>
    public static double CpuUsage(int sampleMs)
    {
        if (!GetSystemTimes(out var i1, out var k1, out var u1)) return 0;
        Thread.Sleep(sampleMs);
        if (!GetSystemTimes(out var i2, out var k2, out var u2)) return 0;
        var idle = i2 - i1;
        var total = (k2 - k1) + (u2 - u1); // kernel incluye idle
        return total <= 0 ? 0 : Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }
}

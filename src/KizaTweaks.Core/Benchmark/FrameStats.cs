using System.Globalization;

namespace KizaTweaks.Core.Benchmark;

public sealed class FpsResult
{
    public string Label { get; set; } = "";
    public string Game { get; set; } = "";
    public DateTime Time { get; set; }
    public int Seconds { get; set; }
    public int Frames { get; set; }
    public double AvgFps { get; set; }
    /// <summary>FPS promedio del 1 % de frames más lentos.</summary>
    public double Low1 { get; set; }
    /// <summary>FPS promedio del 0,1 % de frames más lentos.</summary>
    public double Low01 { get; set; }
    public double AvgFrameTimeMs { get; set; }
    /// <summary>Percentil 99 del tiempo de frame (ms): mientras más bajo, menos tirones.</summary>
    public double P99FrameTimeMs { get; set; }
}

public static class FrameStats
{
    public static FpsResult Compute(IReadOnlyList<double> frameTimesMs)
    {
        var ft = frameTimesMs.Where(x => x > 0 && x < 5000).ToList();
        if (ft.Count == 0) return new FpsResult();
        var total = ft.Sum();
        var sorted = ft.OrderByDescending(x => x).ToList(); // los más lentos primero
        double LowAvg(double fraction)
        {
            var n = Math.Max(1, (int)Math.Ceiling(sorted.Count * fraction));
            return 1000.0 / sorted.Take(n).Average();
        }
        var asc = ft.OrderBy(x => x).ToList();
        var p99 = asc[Math.Min(asc.Count - 1, (int)Math.Ceiling(asc.Count * 0.99) - 1)];
        return new FpsResult
        {
            Frames = ft.Count,
            AvgFps = Math.Round(ft.Count / (total / 1000.0), 1),
            Low1 = Math.Round(LowAvg(0.01), 1),
            Low01 = Math.Round(LowAvg(0.001), 1),
            AvgFrameTimeMs = Math.Round(total / ft.Count, 2),
            P99FrameTimeMs = Math.Round(p99, 2),
        };
    }

    /// <summary>
    /// Lee el CSV de PresentMon. Usa MsBetweenPresents (1.x y 2.x); si no está, prueba otras columnas.
    /// Filtra por nombre de proceso si se indica.
    /// </summary>
    public static List<double> ParsePresentMonCsv(IEnumerable<string> lines, string? processName = null)
    {
        var result = new List<double>();
        string[]? header = null;
        int col = -1, appCol = -1;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split(',');
            if (header is null)
            {
                header = cells.Select(c => c.Trim()).ToArray();
                foreach (var candidate in new[] { "MsBetweenPresents", "FrameTime", "MsBetweenAppStart", "MsBetweenDisplayChange" })
                {
                    col = Array.FindIndex(header, h => h.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                    if (col >= 0) break;
                }
                appCol = Array.FindIndex(header, h => h.Equals("Application", StringComparison.OrdinalIgnoreCase));
                if (col < 0) throw new FormatException("El CSV de PresentMon no tiene una columna de tiempo de frame reconocida.");
                continue;
            }
            if (cells.Length <= col) continue;
            if (processName is not null && appCol >= 0 && appCol < cells.Length &&
                !cells[appCol].Trim().Equals(processName, StringComparison.OrdinalIgnoreCase)) continue;
            if (double.TryParse(cells[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                result.Add(v);
        }
        return result;
    }
}

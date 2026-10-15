using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using KizaTweaks.Core.Platform;

namespace KizaTweaks.Core.Benchmark;

/// <summary>
/// Medición de FPS. El componente de captura se descarga de su repositorio oficial
/// solo si el usuario lo acepta.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PresentMonService
{
    private const string LatestApi = "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest";
    private static readonly Regex AssetRx = new(@"^PresentMon-[\d.]+-x64\.exe$", RegexOptions.IgnoreCase);

    /// <summary>Nombre del recurso incrustado en KizaTweaks.exe (lo agrega el script de compilación).</summary>
    public const string EmbeddedResource = "KizaTweaks.Tools.PresentMon.exe";
    private const string EmbeddedFileName = "PresentMon-kiza-x64.exe";

    /// <summary>¿Viene incluido dentro del .exe? Si es así, no hace falta descargar nada.</summary>
    public static bool IsEmbedded =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetManifestResourceInfo(EmbeddedResource) is not null;

    public string? FindExe()
    {
        var embedded = ExtractEmbedded();
        if (embedded is not null) return embedded;
        if (!Directory.Exists(AppPaths.ToolsDir)) return null;
        return Directory.GetFiles(AppPaths.ToolsDir, "PresentMon-*-x64.exe")
                        .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>Copia el componente incluido en el .exe a la carpeta de herramientas (solo la primera vez o si cambió).</summary>
    private static string? ExtractEmbedded()
    {
        try
        {
            using var s = System.Reflection.Assembly.GetEntryAssembly()?.GetManifestResourceStream(EmbeddedResource);
            if (s is null) return null;
            Directory.CreateDirectory(AppPaths.ToolsDir);
            var dest = Path.Combine(AppPaths.ToolsDir, EmbeddedFileName);
            if (File.Exists(dest) && new FileInfo(dest).Length == s.Length) return dest;
            var tmp = dest + ".part";
            using (var f = File.Create(tmp)) s.CopyTo(f);
            File.Move(tmp, dest, overwrite: true);
            return dest;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo preparar el medidor de FPS incluido: {ex.Message}");
            return null;
        }
    }

    public async Task<string> DownloadLatestAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KizaTweaks", "1.0"));
        progress?.Report("Buscando la última versión de PresentMon…");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi, ct));
        string? url = null, name = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString() ?? "";
            if (AssetRx.IsMatch(n))
            {
                name = n;
                url = a.GetProperty("browser_download_url").GetString();
                break;
            }
        }
        if (url is null || name is null) throw new InvalidOperationException("No se encontró el ejecutable de PresentMon en la última versión.");
        if (!url.StartsWith("https://github.com/GameTechDev/PresentMon/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("URL de descarga inesperada; se cancela por seguridad.");

        Directory.CreateDirectory(AppPaths.ToolsDir);
        var dest = Path.Combine(AppPaths.ToolsDir, name);
        progress?.Report($"Descargando {name}…");
        var tmp = dest + ".part";
        await using (var s = await http.GetStreamAsync(url, ct))
        await using (var f = File.Create(tmp))
            await s.CopyToAsync(f, ct);
        File.Move(tmp, dest, overwrite: true);
        Log.Info($"PresentMon descargado: {dest}");
        return dest;
    }

    /// <summary>Captura frames del proceso indicado durante N segundos y devuelve las estadísticas.</summary>
    public async Task<FpsResult> CaptureAsync(string processExeName, int seconds, string label, CancellationToken ct = default)
    {
        var exe = FindExe() ?? throw new InvalidOperationException("PresentMon no está descargado.");
        Directory.CreateDirectory(AppPaths.SnapshotsDir);
        var csv = Path.Combine(AppPaths.SnapshotsDir, $"presentmon-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var proc = processExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processExeName : processExeName + ".exe";
        var args = $"--process_name \"{proc}\" --timed {seconds} --terminate_after_timed --output_file \"{csv}\" " +
                   "--no_console_stats --stop_existing_session --session_name KizaTweaks";
        Log.Info($"PresentMon: {args}");
        var r = await Task.Run(() => ProcessRunner.Run(exe, args, (seconds + 30) * 1000), ct);
        if (!File.Exists(csv))
            throw new InvalidOperationException($"PresentMon no generó resultados (código {r.ExitCode}). ¿El juego está abierto y renderizando? {r.Output.Trim()}");

        var frames = FrameStats.ParsePresentMonCsv(File.ReadLines(csv), proc);
        if (frames.Count < 10)
            throw new InvalidOperationException("Se capturaron muy pocos frames. Asegúrate de que el juego esté en primer plano y no en pausa.");
        var res = FrameStats.Compute(frames);
        res.Label = label;
        res.Game = proc;
        res.Seconds = seconds;
        res.Time = DateTime.Now;
        return res;
    }
}

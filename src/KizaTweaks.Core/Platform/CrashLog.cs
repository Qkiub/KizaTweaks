using System.Globalization;
using System.Xml.Linq;

namespace KizaTweaks.Core.Platform;

/// <summary>Un cierre inesperado registrado por Windows (Visor de eventos, Aplicación, id 1000).</summary>
public sealed record CrashEntry(DateTime Time, string App, string Module, string ExceptionCode)
{
    public string Hint => CrashLog.Explain(Module);
}

/// <summary>Lee del registro de eventos por qué se cerraron los programas.</summary>
public static class CrashLog
{
    public static List<CrashEntry> Recent(int max = 60)
    {
        if (!OperatingSystem.IsWindows()) return new();
        try
        {
            var r = ProcessRunner.Run(ProcessRunner.System32("wevtutil.exe"),
                $"qe Application \"/q:*[System[(EventID=1000)]]\" /c:{max} /rd:true /f:xml", 30_000);
            return r.ExitCode == 0 ? Parse(r.Output) : new();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo leer el registro de cierres: " + ex.Message);
            return new();
        }
    }

    public static List<CrashEntry> Parse(string xml)
    {
        var list = new List<CrashEntry>();
        if (string.IsNullOrWhiteSpace(xml)) return list;
        XDocument doc;
        try { doc = XDocument.Parse("<root>" + xml + "</root>"); }
        catch (Exception ex)
        {
            Log.Warn("Registro de cierres ilegible: " + ex.Message);
            return list;
        }
        foreach (var ev in doc.Root!.Elements().Where(e => e.Name.LocalName == "Event"))
        {
            var time = ev.Descendants().FirstOrDefault(e => e.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
            var data = ev.Descendants().Where(e => e.Name.LocalName == "Data").ToList();
            string Field(string name, int index) =>
                data.FirstOrDefault(d => (string?)d.Attribute("Name") == name)?.Value
                ?? (index < data.Count ? data[index].Value : "");
            var app = Field("AppName", 0);
            if (string.IsNullOrWhiteSpace(app)) continue;
            var when = DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var t)
                ? t.ToLocalTime() : DateTime.MinValue;
            list.Add(new CrashEntry(when, app.Trim(), Field("ModuleName", 3).Trim(), Field("ExceptionCode", 6).Trim()));
        }
        return list;
    }

    /// <summary>Explica en palabras simples qué suele significar el módulo que falló.</summary>
    public static string Explain(string module)
    {
        var m = module.ToLowerInvariant();
        bool Any(params string[] parts) => parts.Any(m.StartsWith);
        if (Any("nvoglv", "nvwgf2um", "nvd3dum", "nvcuda", "nvldumd", "nvgpucomp"))
            return "Driver de NVIDIA. Actualízalo (instalación limpia). Si empezó al optimizar, deja apagada la programación de GPU (HAGS).";
        if (Any("atio6", "atiumd", "amdxx", "amdihk", "atig6", "amdvlk"))
            return "Driver de AMD. Actualízalo desde la web de AMD. Si empezó al optimizar, deja apagada la programación de GPU (HAGS).";
        if (Any("igd", "igxe", "igc64", "ig9icd", "igdumd", "intelocl"))
            return "Driver de gráficos Intel. Actualízalo desde la web de Intel.";
        if (Any("d3d11", "d3d12", "dxgi", "d3d9", "dxcore"))
            return "DirectX. Protege la app en Modo FPS y actualiza el driver de video.";
        if (m.EndsWith(".8bf") || m.Contains("plug"))
            return "Un plugin de la app. Desactívalo o actualízalo.";
        if (Any("ntdll", "kernelbase", "ucrtbase", "msvcp", "vcruntime", "combase"))
            return "Error interno de la app (genérico). Actualízala o restablece sus preferencias.";
        if (m.Length == 0 || m == "unknown")
            return "Windows no registró el módulo.";
        return $"Módulo {module}: busca ese nombre para ver a qué programa pertenece.";
    }
}

using System.Diagnostics;
using System.Text;
using KizaTweaks.Core.Engine;

namespace KizaTweaks.Core.Platform;

public static class ProcessRunner
{
    /// <summary>Ejecuta un programa oculto y devuelve código de salida + salida combinada.</summary>
    public static CommandResult Run(string file, string args, int timeoutMs = 120_000, Encoding? encoding = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };
        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        var gate = new object();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (gate) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (gate) sb.AppendLine(e.Data); };
        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, $"No se pudo iniciar {file}: {ex.Message}");
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return new CommandResult(-2, $"Tiempo de espera agotado ({timeoutMs / 1000}s): {file} {args}");
        }
        p.WaitForExit(); // vacía los buffers asíncronos
        lock (gate) return new CommandResult(p.ExitCode, sb.ToString());
    }

    /// <summary>
    /// Ejecuta un programa y entrega cada línea de salida en vivo (para mostrarla en la consola de la app).
    /// </summary>
    public static CommandResult RunStreaming(string file, string args, Action<string> onLine,
                                             int timeoutMs = 3_600_000, Encoding? encoding = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding ?? ConsoleEncoding(),
            StandardErrorEncoding = encoding ?? ConsoleEncoding(),
        };
        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        var gate = new object();
        void Handle(string? data)
        {
            if (data is null) return;
            // sfc y algunas herramientas escriben con caracteres nulos o retornos de carro de progreso
            var line = data.Replace("\0", "").TrimEnd('\r');
            if (line.Contains('\r')) line = line[(line.LastIndexOf('\r') + 1)..];
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (gate) sb.AppendLine(line);
            onLine(line);
        }
        p.OutputDataReceived += (_, e) => Handle(e.Data);
        p.ErrorDataReceived += (_, e) => Handle(e.Data);
        try { p.Start(); }
        catch (Exception ex) { return new CommandResult(-1, $"No se pudo iniciar {file}: {ex.Message}"); }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var sw = Stopwatch.StartNew();
        while (!p.WaitForExit(250))
        {
            if (ct.IsCancellationRequested || sw.ElapsedMilliseconds > timeoutMs)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return new CommandResult(-2, ct.IsCancellationRequested ? "Cancelado." : "Tiempo de espera agotado.");
            }
        }
        p.WaitForExit();
        lock (gate) return new CommandResult(p.ExitCode, sb.ToString());
    }

    /// <summary>Codificación OEM de la consola (p. ej. 850 en Windows en español), para leer bien tildes y ñ.</summary>
    public static Encoding ConsoleEncoding()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return Encoding.UTF8;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch { return Encoding.UTF8; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    public static string System32(string exe) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);

    /// <summary>
    /// Ejecuta un script de Windows PowerShell 5.1 (siempre presente en Windows 10/11).
    /// El script se pasa codificado para evitar problemas de comillas.
    /// </summary>
    public static CommandResult PowerShell(string script, int timeoutMs = 300_000)
    {
        var full = "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                               "WindowsPowerShell", "v1.0", "powershell.exe");
        return Run(exe, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                   timeoutMs, Encoding.UTF8);
    }

    /// <summary>Escapa un texto para usarlo entre comillas simples en PowerShell.</summary>
    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    public static void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"No se pudo abrir {target}", ex); }
    }
}

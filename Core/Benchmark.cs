using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Aeropeek.Core;

/// <summary>Résumé d'une capture, conservé pour comparer avant et après.</summary>
public sealed class BenchRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset When { get; set; } = DateTimeOffset.Now;
    public string Label { get; set; } = "";
    public string ProcessName { get; set; } = "cs2";
    public int Frames { get; set; }
    public double DurationS { get; set; }
    public double AvgFps { get; set; }
    public double Low1Fps { get; set; }
    public double Low01Fps { get; set; }
    public double MedianMs { get; set; }
    public double MaxMs { get; set; }
    public int Stutters { get; set; }

    /// <summary>
    /// Courbe des temps d'image, ré-échantillonnée. On garde le MAXIMUM de chaque
    /// intervalle et non la moyenne : c'est le seul moyen de conserver les pics,
    /// qui sont précisément ce qu'on cherche à voir.
    /// </summary>
    public List<double> Series { get; set; } = new();
}

public sealed class BenchHistory
{
    public int Version { get; set; } = 1;
    public List<BenchRun> Runs { get; set; } = new();

    static string Path => System.IO.Path.Combine(Journal.Directory, "benchmarks.json");

    public static BenchHistory Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<BenchHistory>(File.ReadAllText(Path)) ?? new BenchHistory();
        }
        catch { }
        return new BenchHistory();
    }

    public void Add(BenchRun run)
    {
        Runs.Add(run);
        try
        {
            Directory.CreateDirectory(Journal.Directory);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public static class Benchmark
{
    public static string ToolPath => Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");
    public static bool ToolAvailable => File.Exists(ToolPath);

    public static bool IsRunning(string processName) =>
        Process.GetProcessesByName(processName).Length > 0;

    /// <summary>
    /// Capture les temps d'image du jeu pendant N secondes via PresentMon.
    /// Purement passif : PresentMon lit une trace ETW du système, il n'entre
    /// jamais dans le processus du jeu.
    /// </summary>
    /// <summary>
    /// Délai avant le début de l'enregistrement, le temps que le joueur revienne
    /// dans le jeu. Sans lui, les images de l'Alt+Tab dominent les 0,1% lows.
    /// </summary>
    public const int DelaySeconds = 6;

    public static async Task<BenchRun> CaptureAsync(string processName, int seconds, string label,
                                                    CancellationToken ct = default)
    {
        if (!ToolAvailable)
            throw new FileNotFoundException("PresentMon est introuvable à côté de l'application.", ToolPath);
        if (!IsRunning(processName))
            throw new InvalidOperationException($"{processName}.exe n'est pas lancé.");

        var dir = Path.Combine(Journal.Directory, "captures");
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        var psi = new ProcessStartInfo(ToolPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in new[]
        {
            "--process_name", processName + ".exe",
            "--output_file", csv,
            "--delay", DelaySeconds.ToString(CultureInfo.InvariantCulture),
            "--timed", seconds.ToString(CultureInfo.InvariantCulture),
            "--terminate_after_timed",
            "--stop_existing_session",
            "--no_console_stats",
            "--v2_metrics"
        }) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("PresentMon n'a pas démarré.");
        var err = proc.StandardError.ReadToEndAsync(ct);
        var outp = proc.StandardOutput.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            throw;
        }

        if (!File.Exists(csv))
        {
            var why = Shorten(await err);
            if (string.IsNullOrEmpty(why)) why = Shorten(await outp);
            throw new InvalidOperationException(
                "PresentMon n'a produit aucune donnée." + (why.Length > 0 ? "\n\n" + why : ""));
        }

        var times = ParseFrameTimes(csv);
        if (times.Count < 30)
            throw new InvalidOperationException(
                $"Trop peu d'images capturées ({times.Count}). Le jeu était-il bien au premier plan et en train de rendre ?");

        return Summarise(times, processName, label);
    }

    static string Shorten(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" : s.Trim().Split('\n')[0].Trim();

    /// <summary>
    /// Lit la colonne des temps d'image par son NOM, pas par sa position :
    /// PresentMon 2.x écrit « FrameTime », la 1.x « msBetweenPresents ».
    /// </summary>
    static List<double> ParseFrameTimes(string csvPath)
    {
        var result = new List<double>();
        using var reader = new StreamReader(csvPath);

        var header = reader.ReadLine();
        if (header == null) return result;

        var cols = header.Split(',');
        int idx = -1;
        foreach (var name in new[] { "FrameTime", "msBetweenPresents", "MsBetweenPresents" })
        {
            idx = Array.FindIndex(cols, c => c.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) break;
        }
        if (idx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split(',');
            if (parts.Length <= idx) continue;
            if (double.TryParse(parts[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                && ms > 0 && ms < 1000)
                result.Add(ms);
        }
        return result;
    }

    static List<double> Downsample(List<double> times, int points)
    {
        if (times.Count <= points) return new List<double>(times);

        var result = new List<double>(points);
        double step = (double)times.Count / points;
        for (int i = 0; i < points; i++)
        {
            int from = (int)(i * step);
            int to = Math.Min(times.Count, (int)((i + 1) * step));
            double peak = 0;
            for (int j = from; j < to; j++) if (times[j] > peak) peak = times[j];
            result.Add(peak);
        }
        return result;
    }

    static BenchRun Summarise(List<double> times, string processName, string label)
    {
        var sorted = times.OrderBy(t => t).ToList();
        double total = times.Sum();
        double median = sorted[sorted.Count / 2];

        // 1% low : moyenne des 1 % d'images les plus lentes, convertie en fps.
        double LowFps(double fraction)
        {
            int n = Math.Max(1, (int)(sorted.Count * fraction));
            double meanMs = sorted.Skip(sorted.Count - n).Average();
            return meanMs > 0 ? 1000.0 / meanMs : 0;
        }

        // Une saccade : plus du double de la médiane ET au-delà d'un plancher absolu.
        // Sans ce plancher, à 500 fps une image de 4 ms serait comptée comme une
        // saccade alors qu'elle est parfaitement imperceptible.
        const double FloorMs = 8.0;   // 8 ms = 125 fps
        int stutters = times.Count(t => t > median * 2 && t > FloorMs);

        return new BenchRun
        {
            Label = label,
            Series = Downsample(times, 300),
            ProcessName = processName,
            Frames = times.Count,
            DurationS = total / 1000.0,
            AvgFps = total > 0 ? times.Count / (total / 1000.0) : 0,
            Low1Fps = LowFps(0.01),
            Low01Fps = LowFps(0.001),
            MedianMs = median,
            MaxMs = sorted[^1],
            Stutters = stutters
        };
    }
}

using System.Diagnostics;
using System.Security.Principal;

namespace Aeropeek.Core;

public sealed record AppCandidate(
    string Process,
    string Label,
    string Category,
    bool Running,
    int Instances,
    long MemoryBytes,
    double CpuPercent,
    string? Path)
{
    /// <summary>A montré une activité processeur réelle pendant l'échantillonnage.</summary>
    public bool Active => CpuPercent >= 0.8;

    public string MemoryText => MemoryBytes >= 1073741824
        ? $"{MemoryBytes / 1073741824.0:0.0} Go"
        : $"{MemoryBytes / 1048576.0:0} Mo";
}

public static class AppOps
{
    /// <summary>
    /// Jamais fermés. CS2 a besoin de Steam ; les anticheats et l'Explorateur ne se
    /// relancent pas proprement ; et Aeropeek ne se tire pas une balle dans le pied.
    /// </summary>
    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2", "steam", "steamservice", "steamwebhelper", "explorer", "Aeropeek",
        "FACEIT", "faceitclient", "vgc", "vgtray", "RiotClientServices",
        "EasyAntiCheat", "BEService", "csrss", "winlogon", "services", "lsass", "dwm"
    };

    /// <summary>
    /// Applications proposées à la fermeture pendant une partie. Liste blanche
    /// stricte : rien d'autre n'est jamais touché.
    /// </summary>
    public static readonly (string Process, string Label, string Category)[] Candidates =
    {
        ("chrome",              "Google Chrome",        "Navigateur"),
        ("msedge",              "Microsoft Edge",       "Navigateur"),
        ("firefox",             "Mozilla Firefox",      "Navigateur"),
        ("opera",               "Opera",                "Navigateur"),
        ("brave",               "Brave",                "Navigateur"),
        ("vivaldi",             "Vivaldi",              "Navigateur"),

        ("EpicGamesLauncher",   "Epic Games",           "Lanceur"),
        ("Battle.net",          "Battle.net",           "Lanceur"),
        ("GalaxyClient",        "GOG Galaxy",           "Lanceur"),
        ("EADesktop",           "EA App",               "Lanceur"),
        ("UbisoftConnect",      "Ubisoft Connect",      "Lanceur"),
        ("upc",                 "Ubisoft Connect",      "Lanceur"),

        ("Discord",             "Discord",              "Messagerie"),
        ("Teams",               "Microsoft Teams",      "Messagerie"),
        ("ms-teams",            "Microsoft Teams",      "Messagerie"),
        ("Slack",               "Slack",                "Messagerie"),
        ("WhatsApp",            "WhatsApp",             "Messagerie"),
        ("Telegram",            "Telegram",             "Messagerie"),

        ("Spotify",             "Spotify",              "Autre"),
        ("OneDrive",            "OneDrive",             "Autre"),
        ("Code",                "Visual Studio Code",   "Autre"),
        ("obs64",               "OBS Studio",           "Autre"),
        ("wallpaper64",         "Wallpaper Engine",     "Autre")
    };

    public static bool IsAllowed(string process) => !Protected.Contains(process);

    // ---------- privilèges ----------

    public static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ---------- mesure ----------

    public static Task<List<AppCandidate>> SampleAsync(int seconds = 4, CancellationToken ct = default) =>
        Task.Run(async () =>
    {
        // Même raison que pour les services : Process.GetProcessesByName parcourt
        // toute la table des processus, et on le fait deux fois par candidat.
        // Sur le fil d'interface, cela figeait la fenêtre au lancement.
        var seen = new Dictionary<string, (string Label, string Cat)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (proc, label, cat) in Candidates) seen[proc] = (label, cat);

        var t0 = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in seen.Keys) t0[name] = TotalCpu(name);

        await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);

        var list = new List<AppCandidate>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (proc, label, cat) in Candidates)
        {
            if (!done.Add(label)) continue;             // Teams apparaît sous deux noms
            var procs = Process.GetProcessesByName(proc);
            if (procs.Length == 0) { foreach (var p in procs) p.Dispose(); continue; }

            long mem = 0; string? path = null;
            foreach (var p in procs)
            {
                try { mem += p.WorkingSet64; } catch { }
                try { path ??= p.MainModule?.FileName; } catch { }
                p.Dispose();
            }

            double cpu = (TotalCpu(proc) - t0.GetValueOrDefault(proc)).TotalSeconds
                         / seconds / Environment.ProcessorCount * 100.0;

            list.Add(new AppCandidate(proc, label, cat, true, procs.Length, mem,
                Math.Round(Math.Max(0, cpu), 2), path));
        }

        return list.OrderByDescending(a => a.Active)
                   .ThenByDescending(a => a.MemoryBytes)
                   .ToList();
    }, ct);

    static TimeSpan TotalCpu(string name)
    {
        var total = TimeSpan.Zero;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { total += p.TotalProcessorTime; } catch { }
            finally { p.Dispose(); }
        }
        return total;
    }

    // ---------- fermeture ----------

    /// <summary>
    /// Ferme une application : demande d'abord poliment, puis force au bout de
    /// trois secondes. Le chemin de l'exécutable est retenu pour pouvoir la
    /// relancer quand la partie se termine.
    /// </summary>
    public static OpRecord? Close(string processName, string label)
    {
        if (!IsAllowed(processName)) throw new InvalidOperationException($"Application protégée : {processName}");

        var procs = Process.GetProcessesByName(processName);
        if (procs.Length == 0) return null;

        string? path = null;
        foreach (var p in procs)
        {
            try { path ??= p.MainModule?.FileName; } catch { }
        }

        foreach (var p in procs)
        {
            try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
        }

        foreach (var p in procs)
        {
            try
            {
                if (!p.WaitForExit(3000) && !p.HasExited) p.Kill(entireProcessTree: true);
            }
            catch { }
            finally { p.Dispose(); }
        }

        return new OpRecord
        {
            Kind = "app-closed",
            TweakId = "mode-match",
            Description = "Application fermée : " + label,
            ServiceName = processName,
            WasRunning = true,
            BackupPath = path ?? ""
        };
    }

    /// <summary>
    /// Relance l'application par l'Explorateur, afin qu'elle reparte avec les
    /// droits de l'utilisateur et non ceux, élevés, d'Aeropeek.
    /// </summary>
    public static void Revert(OpRecord rec)
    {
        if (rec.Kind != "app-closed" || !rec.WasRunning) return;
        if (string.IsNullOrEmpty(rec.BackupPath) || !System.IO.File.Exists(rec.BackupPath)) return;
        if (Process.GetProcessesByName(rec.ServiceName).Length > 0) return;   // déjà relancée

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{rec.BackupPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    // ---------- priorité du jeu ----------

    public static OpRecord? SetGamePriority(string processName, ProcessPriorityClass target)
    {
        ProcessPriorityClass? previous = null;
        int changed = 0;

        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                previous ??= p.PriorityClass;
                p.PriorityClass = target;
                changed++;
            }
            catch { }
            finally { p.Dispose(); }
        }

        if (changed == 0 || previous == null) return null;

        return new OpRecord
        {
            Kind = "priority",
            TweakId = "mode-match",
            Description = $"Priorité de {processName}.exe : {target}",
            ServiceName = processName,
            PreviousValue = previous.ToString(),
            NewValue = target.ToString()
        };
    }

    public static void RevertPriority(OpRecord rec)
    {
        if (rec.Kind != "priority") return;
        if (!Enum.TryParse<ProcessPriorityClass>(rec.PreviousValue, out var previous)) return;

        foreach (var p in Process.GetProcessesByName(rec.ServiceName))
        {
            try { p.PriorityClass = previous; } catch { }
            finally { p.Dispose(); }
        }
    }
}

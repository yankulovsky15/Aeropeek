using System.IO;
using Microsoft.Win32;

namespace Aeropeek.Core;

public enum CleanRisk { Safe, Check, NoReturn }

public sealed record CleanTarget(
    string Id,
    string Group,
    string Title,
    string Description,
    CleanRisk Risk,
    bool DefaultOn,
    Func<IEnumerable<string>> Paths);

public sealed record CleanScan(CleanTarget Target, long Bytes, int Files, bool Available)
{
    public string Size => Human(Bytes);

    public static string Human(long b) => b switch
    {
        >= 1073741824 => $"{b / 1073741824.0:0.0} Go",
        >= 1048576 => $"{b / 1048576.0:0.0} Mo",
        >= 1024 => $"{b / 1024.0:0} Ko",
        _ => $"{b} o"
    };
}

/// <summary>
/// Nettoyage de fichiers. Contrairement au registre ou aux services, une
/// suppression de fichiers n'est PAS réversible : déplacer des dizaines de Go en
/// quarantaine n'aurait aucun sens. Le journal en garde la trace, mais « Tout
/// annuler » ne peut rien restaurer — l'interface doit le dire.
/// </summary>
public static class Cleanup
{
    static string Env(string v) => Environment.ExpandEnvironmentVariables(v);

    static string? SteamPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return k?.GetValue("SteamPath") as string;
        }
        catch { return null; }
    }

    static IEnumerable<string> Existing(params string?[] paths) =>
        paths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))!.Cast<string>();

    public static readonly CleanTarget[] Targets =
    {
        new("shaders-nvidia", "Game-related", "Cache de shaders NVIDIA",
            "Rebuilds itself. It is also the fix when stutter comes from a damaged cache.",
            CleanRisk.Safe, true,
            () => Existing(Env(@"%LOCALAPPDATA%\NVIDIA\DXCache"), Env(@"%LOCALAPPDATA%\NVIDIA\GLCache"),
                           Env(@"%LOCALAPPDATA%\NVIDIA Corporation\NV_Cache"))),

        new("shaders-steam", "Game-related", "Cache de shaders Steam",
            "Also holds shaders for uninstalled games. Re-downloaded when needed.",
            CleanRisk.Safe, true,
            () => Existing(SteamPath() is { } s ? Path.Combine(s, "steamapps", "shadercache") : null)),

        new("temp", "System", "Fichiers temporaires",
            "Windows and per-session Temp folders. Files in use are skipped.",
            CleanRisk.Safe, true,
            () => Existing(Env(@"%TEMP%"), Env(@"%SystemRoot%\Temp"))),

        new("thumbnails", "System", "Vignettes de l'Explorateur",
            "Cached thumbnails. Rebuilt the next time they're shown, a little more slowly.",
            CleanRisk.Safe, false,
            () => Existing(Env(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer"))),

        new("crashdumps", "System", "Error reports and memory dumps",
            "Traces of past crashes. Of no use once the problem is solved.",
            CleanRisk.Safe, true,
            () => Existing(Env(@"%LOCALAPPDATA%\CrashDumps"),
                           Env(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue"),
                           Env(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"))),

        new("windows-update", "System", "Cache de Windows Update",
            "Installers already applied. Windows re-downloads them if an update is pending.",
            CleanRisk.Check, false,
            () => Existing(Env(@"%SystemRoot%\SoftwareDistribution\Download"))),

        new("delivery", "System", "Update sharing cache",
            "Chunks of updates kept to be uploaded to other PCs.",
            CleanRisk.Safe, false,
            () => Existing(Env(@"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"))),

        new("windows-old", "System", "Ancienne version de Windows",
            "The Windows.old folder. Deleting it permanently prevents rolling back to the previous version.",
            CleanRisk.NoReturn, false,
            () => Existing(Env(@"%SystemDrive%\Windows.old")))
    };

    // ---------- analyse ----------

    public static async Task<List<CleanScan>> ScanAsync(CancellationToken ct = default)
    {
        var jobs = Targets.Select(t => Task.Run(() =>
        {
            long bytes = 0; int files = 0; bool any = false;
            foreach (var dir in t.Paths())
            {
                any = true;
                Measure(dir, ref bytes, ref files, ct, 0);
            }
            return new CleanScan(t, bytes, files, any);
        }, ct));

        return (await Task.WhenAll(jobs)).ToList();
    }

    /// <summary>
    /// Taille d'un dossier. On ne suit JAMAIS un point de jonction : cela ferait
    /// compter — et plus tard supprimer — des fichiers situés ailleurs.
    /// </summary>
    static void Measure(string dir, ref long bytes, ref int files, CancellationToken ct, int depth)
    {
        if (ct.IsCancellationRequested || depth > 24) return;

        try
        {
            var info = new DirectoryInfo(dir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;

            foreach (var f in info.EnumerateFiles())
            {
                try { bytes += f.Length; files++; } catch { }
            }
            foreach (var d in info.EnumerateDirectories())
                Measure(d.FullName, ref bytes, ref files, ct, depth + 1);
        }
        catch { /* accès refusé : ce sous-dossier ne sera ni compté ni supprimé */ }
    }

    // ---------- suppression ----------

    public sealed record CleanOutcome(int Deleted, int Skipped, long Freed);

    public static CleanOutcome Delete(CleanTarget target, CancellationToken ct = default)
    {
        int deleted = 0, skipped = 0; long freed = 0;
        foreach (var dir in target.Paths())
            Purge(dir, ref deleted, ref skipped, ref freed, ct, 0, root: true);
        return new CleanOutcome(deleted, skipped, freed);
    }

    static void Purge(string dir, ref int deleted, ref int skipped, ref long freed,
                      CancellationToken ct, int depth, bool root)
    {
        if (ct.IsCancellationRequested || depth > 24) return;

        DirectoryInfo info;
        try
        {
            info = new DirectoryInfo(dir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        }
        catch { return; }

        try
        {
            foreach (var f in info.EnumerateFiles())
            {
                try
                {
                    long size = f.Length;
                    f.Attributes = FileAttributes.Normal;
                    f.Delete();
                    deleted++; freed += size;
                }
                catch { skipped++; }
            }
        }
        catch { }

        try
        {
            foreach (var d in info.EnumerateDirectories())
                Purge(d.FullName, ref deleted, ref skipped, ref freed, ct, depth + 1, root: false);
        }
        catch { }

        // On vide le dossier racine sans le supprimer : Windows attend qu'il existe.
        if (!root)
        {
            try { info.Delete(false); } catch { }
        }
    }

    public static OpRecord Record(CleanTarget target, CleanOutcome outcome) => new()
    {
        Kind = "files-deleted",
        TweakId = "nettoyage",
        Description = "Nettoyage : " + target.Title,
        SubKey = string.Join(" · ", target.Paths()),
        PreviousValue = CleanScan.Human(outcome.Freed),
        NewValue = $"{outcome.Deleted} file(s) deleted, {outcome.Skipped} skipped"
    };
}

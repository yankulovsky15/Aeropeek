using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Aeropeek.Core;

public enum Cs2Verdict { Bon, AAmeliorer, Info, Inconnu }

/// <summary>Un point de configuration du jeu, lu dans ses propres fichiers.</summary>
public sealed record Cs2Finding(
    string Id,
    string Label,
    string Value,
    Cs2Verdict Verdict,
    string Note,
    string Advice = "");

/// <summary>Une option de lancement Steam, telle qu'elle est ecrite, et ce qu'elle fait vraiment.</summary>
public sealed record LaunchToken(string Token, string Meaning, bool Effective);

/// <summary>
/// Lecture de la configuration réelle de CS2 — fichiers du jeu et options de
/// lancement Steam — plutôt que de la configuration supposée.
/// <para>
/// Le principe est le même que pour NVIDIA : on n'interprète que les clés dont
/// la signification est établie. Les autres sont ignorées plutôt que devinées.
/// </para>
/// </summary>
public static class Cs2
{
    public const string AppId = "730";

    // ---------- emplacements ----------

    public static string? SteamPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var p = k?.GetValue("SteamPath") as string;
            return string.IsNullOrEmpty(p) ? null : p.Replace('/', '\\');
        }
        catch { return null; }
    }

    /// <summary>
    /// Dossier du compte Steam utilisé pour CS2. Plusieurs comptes peuvent avoir
    /// servi sur la machine : on retient celui dont les fichiers du jeu ont été
    /// modifiés le plus récemment, pas le premier de la liste.
    /// </summary>
    public static string? UserDataDir()
    {
        var steam = SteamPath();
        if (steam == null) return null;

        var root = Path.Combine(steam, "userdata");
        if (!Directory.Exists(root)) return null;

        string? best = null;
        DateTime bestTime = DateTime.MinValue;

        try
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var game = Path.Combine(dir, AppId);
                if (!Directory.Exists(game)) continue;

                var stamp = Directory.GetLastWriteTimeUtc(game);
                if (stamp > bestTime) { bestTime = stamp; best = dir; }
            }
        }
        catch { }

        return best;
    }

    public static string? VideoConfigPath()
    {
        var user = UserDataDir();
        if (user == null) return null;
        var p = Path.Combine(user, AppId, "local", "cfg", "cs2_video.txt");
        return File.Exists(p) ? p : null;
    }

    public static string? LocalConfigPath()
    {
        var user = UserDataDir();
        if (user == null) return null;
        var p = Path.Combine(user, "config", "localconfig.vdf");
        return File.Exists(p) ? p : null;
    }

    /// <summary>Dossier cfg de l'installation du jeu, où vit autoexec.cfg.</summary>
    public static string? CfgDir()
    {
        var install = Storage.GamePath();
        if (install == null) return null;
        var p = Path.Combine(install, "game", "csgo", "cfg");
        return Directory.Exists(p) ? p : null;
    }

    public static string? AutoexecPath()
    {
        var dir = CfgDir();
        return dir == null ? null : Path.Combine(dir, "autoexec.cfg");
    }

    public static bool SteamRunning =>
        System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;

    public static bool GameRunning =>
        System.Diagnostics.Process.GetProcessesByName("cs2").Length > 0;

    // ---------- lecture des fichiers KeyValues ----------

    static readonly Regex Pair = new("\"([^\"]+)\"\\s+\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>Paires clé/valeur d'un fichier KeyValues, à plat.</summary>
    public static Dictionary<string, string> ReadKeyValues(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Match m in Pair.Matches(File.ReadAllText(path)))
                map[m.Groups[1].Value] = m.Groups[2].Value;
        }
        catch { }
        return map;
    }

    static int Int(Dictionary<string, string> map, string key, int fallback = -1) =>
        map.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : fallback;

    // ---------- réglages vidéo ----------

    /// <summary>
    /// Réglages vidéo du jeu. Seules les clés dont la signification est établie
    /// sont interprétées ; le reste du fichier est laissé de côté.
    /// </summary>
    public static List<Cs2Finding> VideoFindings(SystemProfile sys)
    {
        var list = new List<Cs2Finding>();
        var path = VideoConfigPath();
        if (path == null) return list;

        var v = ReadKeyValues(path);
        if (v.Count == 0) return list;

        // --- mode d'affichage ---
        int full = Int(v, "setting.fullscreen");
        int borderless = Int(v, "setting.nowindowborder");

        if (full == 1 && borderless == 0)
            list.Add(new Cs2Finding("mode", "Display mode", "Exclusive fullscreen", Cs2Verdict.Bon,
                "The game owns the display: the shortest path between a rendered frame and a shown frame."));
        else if (full == 1 && borderless == 1)
            list.Add(new Cs2Finding("mode", "Display mode", "Borderless fullscreen", Cs2Verdict.AAmeliorer,
                "Your frames pass through the Windows compositor before reaching the screen. That typically adds one frame of latency.",
                "Settings → Video → Display Mode → Fullscreen. You lose instant alt-tab; that is the trade."));
        else if (full == 0)
            list.Add(new Cs2Finding("mode", "Display mode", "Windowed", Cs2Verdict.AAmeliorer,
                "Windowed mode adds compositor latency and does not reserve the display for the game.",
                "Settings → Video → Display Mode → Fullscreen."));

        // --- synchronisation verticale ---
        int vsync = Int(v, "setting.mat_vsync");
        if (vsync == 0)
            list.Add(new Cs2Finding("vsync", "Synchronisation verticale", "Off", Cs2Verdict.Bon,
                "No frame waits for the display scan-out."));
        else if (vsync == 1)
            list.Add(new Cs2Finding("vsync", "Synchronisation verticale", "On", Cs2Verdict.AAmeliorer,
                "Every frame waits for the display scan-out. It is the costliest source of latency among the video settings.",
                "Settings → Video → Wait for Vertical Sync → Disabled."));

        // --- NVIDIA Reflex : n'a de sens que sur une carte NVIDIA ---
        bool nvidia = sys.GpuNames.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        int reflex = Int(v, "setting.r_low_latency");
        if (nvidia && reflex >= 0)
        {
            list.Add(reflex switch
            {
                0 => new Cs2Finding("reflex", "NVIDIA Reflex", "Off", Cs2Verdict.AAmeliorer,
                        "Reflex stops the frame queue from filling up ahead of the GPU. It is the single biggest latency reduction in CS2, and it costs no frames.",
                        "Settings → Video → NVIDIA Reflex Low Latency → Enabled."),
                1 => new Cs2Finding("reflex", "NVIDIA Reflex", "On", Cs2Verdict.Bon,
                        "The frame queue stays short: this is the right setting."),
                2 => new Cs2Finding("reflex", "NVIDIA Reflex", "On + Boost", Cs2Verdict.Bon,
                        "Boost holds GPU clocks up while it waits on the processor. It draws more power for nothing when the GPU isn't the limiting factor — which in CS2 is most of the time."),
                _ => new Cs2Finding("reflex", "NVIDIA Reflex", $"valeur {reflex}", Cs2Verdict.Inconnu,
                        "Undocumented value: Aeropeek does not interpret it.")
            });
        }

        // --- fréquence d'affichage demandée par le jeu ---
        int num = Int(v, "setting.refreshrate_numerator");
        int den = Int(v, "setting.refreshrate_denominator", 1);
        if (num > 0 && den > 0)
        {
            int gameHz = (int)Math.Round(num / (double)den);
            var screen = sys.Displays.FirstOrDefault(d => d.IsPrimary) ?? sys.Displays.FirstOrDefault();

            if (screen != null && screen.RefreshHz > 0 && gameHz < screen.RefreshHz - 1)
                list.Add(new Cs2Finding("hz", "Refresh rate requested by the game", $"{gameHz} Hz", Cs2Verdict.AAmeliorer,
                    $"The game asks for {gameHz} Hz while your display runs at {screen.RefreshHz} Hz on the desktop.",
                    "Settings → Video → Refresh Rate → the highest value."));
            else
                list.Add(new Cs2Finding("hz", "Refresh rate requested by the game", $"{gameHz} Hz", Cs2Verdict.Bon,
                    screen == null ? "" : $"Identique au bureau ({screen.RefreshHz} Hz)."));
        }

        // --- résolution ---
        int w = Int(v, "setting.defaultres");
        int h = Int(v, "setting.defaultresheight");
        if (w > 0 && h > 0)
        {
            var screen = sys.Displays.FirstOrDefault(d => d.IsPrimary) ?? sys.Displays.FirstOrDefault();
            string note = screen != null && (w != screen.Width || h != screen.Height)
                ? $"Below the display's native resolution ({screen.Width}×{screen.Height}). A common choice in CS2: fewer pixels to render, and wider models if the driver stretches the image."
                : "The display's native resolution.";
            list.Add(new Cs2Finding("res", "Resolution", $"{w} × {h}", Cs2Verdict.Info, note));
        }

        // --- anticrénelage ---
        int msaa = Int(v, "setting.msaa_samples");
        if (msaa >= 0)
        {
            string label = msaa switch { 0 => "Off", 2 => "2×", 4 => "4×", 8 => "8×", _ => $"{msaa}×" };
            list.Add(new Cs2Finding("msaa", "Anti-aliasing (MSAA)", label, Cs2Verdict.Info,
                msaa >= 2
                    ? "Costs frames only when the graphics card is the limiting factor. In CS2 the processor almost always is: check the Benchmark tab before touching it."
                    : "No graphics cost."));
        }

        // --- comportement à l'alt-tab ---
        int minimize = Int(v, "setting.fullscreen_min_on_focus_loss");
        if (minimize >= 0)
            list.Add(new Cs2Finding("alttab", "Minimise on alt-tab", minimize == 1 ? "On" : "Off",
                Cs2Verdict.Info,
                minimize == 1
                    ? "The game minimises when you switch windows. Necessary in exclusive fullscreen, annoying on two monitors."
                    : "The game stays on screen when you switch windows."));

        return list;
    }

    // ---------- options de lancement ----------

    /// <summary>
    /// Ce que chaque option fait réellement dans CS2. Beaucoup d'options
    /// recopiées de guides datent de CS:GO et n'ont plus aucun effet sur
    /// Source 2 : elles sont signalées ici, pas supprimées d'office.
    /// </summary>
    static readonly Dictionary<string, (string Meaning, bool Effective)> Known =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["-novid"]                      = ("Skips the Valve intro video", true),
        ["-console"]                    = ("Opens the console at startup", true),
        ["-high"]                       = ("Starts the game at high priority", true),
        ["-fullscreen"]                 = ("Forces fullscreen", true),
        ["-windowed"]                   = ("Forces windowed mode", true),
        ["-w"]                          = ("Forces the frame width", true),
        ["-h"]                          = ("Forces the frame height", true),
        ["-language"]                   = ("Forces the game language", true),
        ["-allow_third_party_software"] = ("Allows external overlays to hook into the game", true),
        ["-insecure"]                   = ("Disables VAC — no official server reachable", true),
        ["-vulkan"]                     = ("Uses Vulkan rendering instead of Direct3D", true),
        ["-tools"]                      = ("Opens the developer tools instead of the game", true),
        ["-sw"]                         = ("Forces windowed mode", true),
        ["-noborder"]                   = ("Borderless window", true),

        // Héritées de CS:GO. Le jeu les accepte sans rien en faire.
        ["-nojoy"]          = ("No effect in CS2 — the option disappeared with Source 2", false),
        ["-forcenovsync"]   = ("No effect in CS2 — vertical sync is set in the video options", false),
        ["-threads"]        = ("No effect in CS2 — Source 2 spreads its threads itself", false),
        ["-tickrate"]       = ("No effect in CS2 — servers run on sub-tick", false),
        ["-d3d9ex"]         = ("No effect in CS2 — Direct3D 9 no longer exists in Source 2", false),
        ["-disable_d3d9ex"] = ("No effect in CS2 — Direct3D 9 no longer exists in Source 2", false),
        ["-nod3d9ex"]       = ("No effect in CS2 — Direct3D 9 no longer exists in Source 2", false),
        ["-softparticles"]  = ("No effect in CS2 — a Source 1 option", false),
        ["-freq"]           = ("No effect in CS2 — the refresh rate is set in the video options", false),
        ["-refresh"]        = ("No effect in CS2 — the refresh rate is set in the video options", false),
        ["-processheap"]    = ("No effect in CS2 — a Source 1 option", false),
        ["-limitvsconst"]   = ("No effect in CS2 — a Source 1 option", false),
        ["-noaafonts"]      = ("No effect in CS2 — a Source 1 option", false)
    };

    /// <summary>Options de lancement actuellement enregistrées par Steam pour CS2.</summary>
    public static string? LaunchOptions()
    {
        var path = LocalConfigPath();
        if (path == null) return null;
        try
        {
            var m = Regex.Match(File.ReadAllText(path), "\"LaunchOptions\"\\s+\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch { return null; }
    }

    /// <summary>Décompose la chaîne d'options en jetons, avec ce que chacun fait.</summary>
    public static List<LaunchToken> AuditLaunchOptions(string options)
    {
        var list = new List<LaunchToken>();
        if (string.IsNullOrWhiteSpace(options)) return list;

        foreach (var raw in options.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // +exec autoexec, +fps_max 0 : les commandes console sont exécutées telles quelles
            if (raw.StartsWith('+'))
            {
                list.Add(new LaunchToken(raw, "Console command run at startup", true));
                continue;
            }

            if (Known.TryGetValue(raw, out var k))
            {
                list.Add(new LaunchToken(raw, k.Meaning, k.Effective));
                continue;
            }

            // Valeur d'une option précédente (-w 1280) plutôt qu'une option isolée
            if (!raw.StartsWith('-'))
            {
                list.Add(new LaunchToken(raw, "Value of the preceding option", true));
                continue;
            }

            list.Add(new LaunchToken(raw, "Option unknown to Aeropeek: its effect is unverified", true));
        }

        return list;
    }

    /// <summary>
    /// Réécrit les options de lancement dans le fichier de Steam.
    /// <para>
    /// Steam garde ce fichier en mémoire et le réécrit en se fermant : toute
    /// modification faite pendant qu'il tourne serait perdue. On refuse donc
    /// plutôt que d'écrire dans le vide.
    /// </para>
    /// </summary>
    public static OpRecord SetLaunchOptions(string value)
    {
        if (SteamRunning)
            throw new InvalidOperationException(
                "Steam must be closed: it rewrites this file on exit and would wipe the change.");

        var path = LocalConfigPath()
            ?? throw new InvalidOperationException("Steam configuration file not found.");

        if (value.Contains('"'))
            throw new InvalidOperationException("Quotation marks are not accepted in launch options.");

        var text = File.ReadAllText(path);
        var m = Regex.Match(text, "\"LaunchOptions\"\\s+\"([^\"]*)\"");

        string previous;
        string updated;

        if (m.Success)
        {
            previous = m.Groups[1].Value;
            updated = text.Remove(m.Index, m.Length).Insert(m.Index, $"\"LaunchOptions\"\t\t\"{value}\"");
        }
        else
        {
            // Aucune option n'a jamais été définie : Steam n'écrit la clé qu'à
            // la première utilisation. On l'insère en tête du bloc du jeu.
            previous = "";
            var block = Regex.Match(text, "\"" + AppId + "\"\\s*\\r?\\n\\s*\\{");
            if (!block.Success)
                throw new InvalidOperationException("CS2 block not found in the Steam configuration.");

            int at = block.Index + block.Length;
            updated = text.Insert(at, $"\r\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"{value}\"");
        }

        var backup = BackupTo(path, "localconfig");
        File.WriteAllText(path, updated, new UTF8Encoding(false));

        return new OpRecord
        {
            Kind = "cs2-launch",
            TweakId = "cs2-launch",
            Description = "CS2 launch options",
            BackupPath = backup,
            PreviousValue = previous,
            NewValue = value,
            Existed = m.Success
        };
    }

    // ---------- autoexec ----------

    /// <summary>
    /// Bloc géré par Aeropeek à l'intérieur d'autoexec.cfg. Tout ce que
    /// l'utilisateur a écrit lui-même en dehors des balises est conservé mot
    /// pour mot : le fichier lui appartient, l'application n'en occupe qu'une
    /// section.
    /// </summary>
    const string BlockStart = "// >>> Aeropeek";
    const string BlockEnd = "// <<< Aeropeek";

    public static string? ReadAutoexec()
    {
        var p = AutoexecPath();
        if (p == null || !File.Exists(p)) return null;
        try { return File.ReadAllText(p); } catch { return null; }
    }

    /// <summary>Contenu du bloc Aeropeek seul, sans les balises.</summary>
    public static string ReadManagedBlock()
    {
        var text = ReadAutoexec();
        if (text == null) return "";
        int a = text.IndexOf(BlockStart, StringComparison.Ordinal);
        int b = text.IndexOf(BlockEnd, StringComparison.Ordinal);
        if (a < 0 || b <= a) return "";
        int from = a + BlockStart.Length;
        return text[from..b].Trim('\r', '\n');
    }

    /// <summary>Écrit le bloc géré en préservant tout le reste du fichier.</summary>
    public static OpRecord WriteManagedBlock(string body)
    {
        var path = AutoexecPath()
            ?? throw new InvalidOperationException("CS2 cfg folder not found.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        bool existed = File.Exists(path);
        string original = existed ? File.ReadAllText(path) : "";
        string backup = existed ? BackupTo(path, "autoexec") : "";

        string block = BlockStart + "\r\n" + body.Trim('\r', '\n') + "\r\n" + BlockEnd;

        string updated;
        int a = original.IndexOf(BlockStart, StringComparison.Ordinal);
        int b = original.IndexOf(BlockEnd, StringComparison.Ordinal);

        if (a >= 0 && b > a)
            updated = original.Remove(a, b + BlockEnd.Length - a).Insert(a, block);
        else
            updated = original.Length == 0
                ? block + "\r\n"
                : original.TrimEnd('\r', '\n') + "\r\n\r\n" + block + "\r\n";

        File.WriteAllText(path, updated, new UTF8Encoding(false));

        return new OpRecord
        {
            Kind = "cs2-file",
            TweakId = "cs2-autoexec",
            Description = "CS2 autoexec.cfg",
            BackupPath = backup,
            SubKey = path,
            Existed = existed
        };
    }

    // ---------- sauvegardes et annulation ----------

    static string BackupTo(string path, string label)
    {
        var dir = Path.Combine(Journal.Directory, "backups");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"{label}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        File.Copy(path, dest, overwrite: true);
        return dest;
    }

    /// <summary>Remet les options de lancement telles qu'elles étaient.</summary>
    public static void RevertLaunch(OpRecord rec)
    {
        if (rec.Kind != "cs2-launch") return;
        if (SteamRunning)
            throw new InvalidOperationException("Steam must be closed to undo this change.");

        var path = LocalConfigPath();
        if (path == null) return;

        var text = File.ReadAllText(path);
        var m = Regex.Match(text, "\"LaunchOptions\"\\s+\"([^\"]*)\"");
        if (!m.Success) return;

        // La clé n'existait pas avant : on la retire plutôt que d'écrire une
        // chaîne vide, qui n'est pas la même chose pour Steam.
        string restored = rec.Existed
            ? text.Remove(m.Index, m.Length).Insert(m.Index, $"\"LaunchOptions\"\t\t\"{rec.PreviousValue}\"")
            : text.Remove(m.Index, m.Length);

        File.WriteAllText(path, restored, new UTF8Encoding(false));
    }

    /// <summary>Restaure un fichier de configuration du jeu depuis sa sauvegarde.</summary>
    public static void RevertFile(OpRecord rec)
    {
        if (rec.Kind != "cs2-file" || string.IsNullOrEmpty(rec.SubKey)) return;

        if (!rec.Existed)
        {
            try { if (File.Exists(rec.SubKey)) File.Delete(rec.SubKey); } catch { }
            return;
        }

        if (!string.IsNullOrEmpty(rec.BackupPath) && File.Exists(rec.BackupPath))
            File.Copy(rec.BackupPath, rec.SubKey, overwrite: true);
    }
}

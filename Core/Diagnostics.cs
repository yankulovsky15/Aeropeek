using System.Management;
using Microsoft.Win32;

namespace Aeropeek.Core;

public enum Severity { Ok, Info, Warn, Probleme, Inconnu }

public sealed record CheckResult(
    string Id,
    string Title,
    Severity Severity,
    string Value,
    string Detail,
    string Advice = "",
    string? ActionId = null,
    string? ActionLabel = null);

/// <summary>
/// Lecture et réglage de l'affinité d'un processus. Purement externe : on ne
/// touche ni à la mémoire ni aux fichiers du jeu, exactement comme le fait le
/// Gestionnaire des tâches.
/// </summary>
public static class GameAffinity
{
    public const string ProcessName = "cs2";

    public static ulong? Current()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(ProcessName))
        {
            try { return (ulong)(long)p.ProcessorAffinity; }
            catch { }
            finally { p.Dispose(); }
        }
        return null;
    }

    public static int Apply(ulong mask)
    {
        int n = 0;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(ProcessName))
        {
            try { p.ProcessorAffinity = (IntPtr)(long)mask; n++; }
            catch { }
            finally { p.Dispose(); }
        }
        return n;
    }

    public static ulong AllCores(int logical) =>
        logical >= 64 ? ulong.MaxValue : (1UL << logical) - 1;
}

public static class Diagnostics
{
    public static List<CheckResult> RunAll(SystemProfile sys)
    {
        var checks = new Func<SystemProfile, CheckResult>[]
        {
            MemoryIntegrity, VbsHypervisor, MemorySpeed, RefreshRate, DisplayAdapter,
            GraphicsDriver, GameDrive, NvidiaSettings, CpuTopology, PowerPlan, GameOverlays
        };

        var results = new List<CheckResult>();
        foreach (var c in checks)
        {
            try { results.Add(c(sys)); }
            catch (Exception ex)
            {
                results.Add(new CheckResult(c.Method.Name, c.Method.Name, Severity.Inconnu,
                    "Undetermined", "The check failed: " + ex.Message));
            }
        }
        return results.OrderBy(r => r.Severity switch
        {
            Severity.Probleme => 0, Severity.Warn => 1, Severity.Inconnu => 2,
            Severity.Info => 3, _ => 4
        }).ToList();
    }

    // ---------- 1. Intégrité de la mémoire (VBS / HVCI) ----------

    const uint HVCI = 2;   // « intégrité du code appliquée par l'hyperviseur »

    static CheckResult MemoryIntegrity(SystemProfile sys)
    {
        // Deux états distincts : ce qui tourne maintenant, et ce qui est prévu au
        // prochain démarrage. Les confondre fait croire qu'une désactivation a échoué.
        bool? running = null, configured = null;
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\DeviceGuard",
                "SELECT SecurityServicesRunning, SecurityServicesConfigured FROM Win32_DeviceGuard");
            foreach (ManagementObject mo in s.Get())
            {
                if (mo["SecurityServicesRunning"] is uint[] r) running = r.Contains(HVCI);
                if (mo["SecurityServicesConfigured"] is uint[] c) configured = c.Contains(HVCI);
            }
        }
        catch { }

        var reg = RegistryOps.ReadValue("HKLM",
            @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
        if (reg != null) configured ??= Convert.ToInt32(reg) == 1;

        if (running == null && configured == null)
            return new CheckResult("hvci", "Memory integrity", Severity.Inconnu,
                "Undetermined", "Could not read the state of virtualization-based security.");

        // Désactivée mais encore active jusqu'au redémarrage
        if (running == true && configured == false)
            return new CheckResult("hvci", "Memory integrity", Severity.Warn,
                "Restart required",
                "You turned it off, but HVCI is still running: the kernel stays under the hypervisor until Windows next boots.",
                "Restart for the gain to take effect, then run the scan again.");

        // Activée mais pas encore chargée
        if (running == false && configured == true)
            return new CheckResult("hvci", "Memory integrity", Severity.Warn,
                "Enabled at next boot",
                "HVCI is not running right now, but it is set to start at the next reboot.",
                "If you don't want it, turn it off now: Windows Security → Device security → Core isolation.");

        if (running == true || (running == null && configured == true))
            return new CheckResult("hvci", "Memory integrity", Severity.Probleme,
                "Enabled",
                "Memory integrity (HVCI) runs the kernel under a hypervisor. The cost is permanent and shows up mostly on the 1% lows.",
                "This is the biggest software gain available: 5 to 10%. Windows Security → Device security → Core isolation. "
                + (sys.AntiCheats.Contains("FACEIT Anti-Cheat")
                    ? "FACEIT requires Secure Boot and TPM, not HVCI: you can turn it off without breaking the anti-cheat."
                    : "Check that your anti-cheat does not require it."),
                "ouvrir-isolation", "Open the setting");

        return new CheckResult("hvci", "Memory integrity", Severity.Ok,
            "Disabled", "No hypervisor overhead on kernel execution.");
    }

    // ---------- 1 bis. Hyperviseur chargé sans utilisateur ----------

    /// <summary>
    /// Scénarios VBS déclarés sous DeviceGuard\Scenarios. Un seul activé suffit à
    /// maintenir l'hyperviseur chargé, y compris quand hypervisorlaunchtype vaut off.
    /// </summary>
    static readonly (string Scenario, string Label)[] VbsScenarios =
    {
        ("HypervisorEnforcedCodeIntegrity", "memory integrity"),
        ("WindowsHello",                    "Windows Hello (enhanced secure sign-in)"),
        ("KernelShadowStacks",              "kernel stack protection"),
        ("KeyGuard",                        "KeyGuard"),
        ("CredentialGuard",                 "Credential Guard")
    };

    /// <summary>Services dont la présence justifie de garder VBS actif.</summary>
    static readonly (string Service, string Label)[] VbsConsumers =
    {
        ("vmcompute",   "Hyper-V / Docker / Windows Sandbox"),
        ("vmms",        "Hyper-V"),
        ("LxssManager", "WSL")
    };

    static CheckResult VbsHypervisor(SystemProfile sys)
    {
        int status = -1;
        uint[] configured = Array.Empty<uint>();
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\DeviceGuard",
                "SELECT VirtualizationBasedSecurityStatus, SecurityServicesConfigured FROM Win32_DeviceGuard");
            foreach (ManagementObject mo in s.Get())
            {
                if (mo["VirtualizationBasedSecurityStatus"] is { } v) status = Convert.ToInt32(v);
                if (mo["SecurityServicesConfigured"] is uint[] c) configured = c;
            }
        }
        catch
        {
            return new CheckResult("vbs", "Hypervisor (VBS)", Severity.Inconnu,
                "Undetermined", "The state of virtualization-based security could not be read.");
        }

        if (status != 2)
            return new CheckResult("vbs", "Hypervisor (VBS)", Severity.Ok,
                "Inactive", "Windows is not running under a hypervisor: no virtualization overhead.");

        // Le tableau contient 0 quand aucun service n'est actif : ce n'est pas un identifiant.
        var running = configured.Where(v => v > 0).ToArray();

        // Qui maintient l'hyperviseur chargé ?
        var users = new List<string>();
        foreach (var (scenario, label) in VbsScenarios)
        {
            var v = RegistryOps.ReadValue("HKLM",
                $@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\{scenario}", "Enabled");
            if (v != null && Convert.ToInt32(v) == 1 && !users.Contains(label)) users.Add(label);
        }
        foreach (var (svc, label) in VbsConsumers)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{svc}");
                if (k != null && !users.Contains(label)) users.Add(label);
            }
            catch { }
        }

        // Cas coûteux : des services de sécurité tournent réellement sous hyperviseur.
        if (running.Length > 0)
            return new CheckResult("vbs", "Hypervisor (VBS)", Severity.Warn,
                "Active with services",
                "Security services are running under the hypervisor" +
                (users.Count > 0 ? " : " + string.Join(", ", users) + "." : "."),
                "This is where the real cost sits. See the “Memory integrity” card, which is its main source.");

        // Cas résiduel : l'hyperviseur est chargé mais aucun service de sécurité ne tourne dedans.
        if (users.Count > 0)
            return new CheckResult("vbs", "Hypervisor (VBS)", Severity.Info,
                "Loaded, no active service",
                "No security service is running under the hypervisor — most of the cost is already gone. " +
                "It stays loaded because something requires it: " + string.Join(", ", users) + ".",
                "The residual cost is small, a few percent at most. Removing it would mean giving up those features: " +
                "rarely a good trade, particularly for Windows Hello.");

        return new CheckResult("vbs", "Hypervisor (VBS)", Severity.Warn,
            "Loaded with no identified user",
            "The hypervisor is loaded although no security service runs inside it and no known feature requires it.",
            "Tweaks tab → “Virtualization-based security”. Modest gain. Put it back before installing WSL, Docker or Windows Sandbox.");
    }

    // ---------- 2. Vitesse mémoire (EXPO / XMP) ----------

    static readonly Dictionary<string, uint[]> JedecBase = new()
    {
        ["DDR4"] = new uint[] { 2133, 2400, 2666 },
        ["DDR5"] = new uint[] { 4000, 4400, 4800, 5600 }
    };

    /// <summary>
    /// Déduit la fréquence certifiée depuis la référence du module. Les fabricants
    /// l'encodent dans leur référence, ce qui évite d'avoir à lire le SPD — lequel
    /// demanderait un pilote noyau.
    /// </summary>
    public static uint RatedFromPartNumber(string partNumber)
    {
        var pn = (partNumber ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        if (pn.Length < 6) return 0;

        // Kingston FURY : KF5 56 C40-16  ->  DDR5, 5600 MT/s, CL40
        var m = System.Text.RegularExpressions.Regex.Match(pn, @"^KF[45](\d{2})C\d{2}");
        if (m.Success) return uint.Parse(m.Groups[1].Value) * 100;

        // G.Skill : F5-6000J3038F16G  ->  DDR5, 6000 MT/s
        m = System.Text.RegularExpressions.Regex.Match(pn, @"^F[45]-(\d{4})");
        if (m.Success) return uint.Parse(m.Groups[1].Value);

        // Corsair : CMK32GX5M2B6000C36
        m = System.Text.RegularExpressions.Regex.Match(pn, @"^CM\w{1,3}\d+GX[45]M\d+[A-Z]?(\d{4})C\d{2}");
        if (m.Success) return uint.Parse(m.Groups[1].Value);

        // Crucial : CT16G56C46U5  ->  5600 MT/s
        m = System.Text.RegularExpressions.Regex.Match(pn, @"^CT\d+G(\d{2})C\d{2}");
        if (m.Success) return uint.Parse(m.Groups[1].Value) * 100;

        return 0;
    }

    static CheckResult MemorySpeed(SystemProfile sys)
    {
        if (sys.Memory.Count == 0)
            return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Inconnu,
                "Undetermined", "The memory modules could not be read.");

        var m = sys.Memory[0];
        uint conf = m.ConfiguredMhz;
        string kind = string.IsNullOrEmpty(m.Kind) ? "" : m.Kind + " ";
        string value = conf > 0 ? $"{kind}{conf} MT/s" : "Undetermined";

        if (conf == 0)
            return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Inconnu,
                value, "The configured speed could not be read.");

        if (m.RatedMhz > 0 && conf < m.RatedMhz)
            return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Probleme,
                value,
                $"Your modules are rated for {m.RatedMhz} MT/s and are running at {conf} MT/s.",
                "Enable the EXPO (AMD) or XMP (Intel) profile in the BIOS. On CS2, which is very sensitive to memory latency, that is 10 to 15% on the 1% lows.");

        // La référence du module donne la fréquence certifiée : c'est la réponse
        // fiable, sans avoir à interroger le SPD ni à demander à l'utilisateur.
        uint fromPart = RatedFromPartNumber(m.PartNumber);
        if (fromPart > 0)
        {
            string pn = string.IsNullOrWhiteSpace(m.PartNumber) ? "" : $" ({m.PartNumber})";
            if (conf < fromPart)
                return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Probleme,
                    value,
                    $"Your sticks{pn} are certified for {fromPart} MT/s and are running at {conf} MT/s.",
                    "Enable EXPO (AMD) or XMP (Intel) in the BIOS. On CS2, which is very sensitive to memory latency, "
                    + "that is 10 to 15% on the 1% lows — more than every software tweak put together.");

            return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Ok,
                value,
                $"Your sticks{pn} are certified for {fromPart} MT/s and are indeed running at that speed. "
                + $"{sys.Memory.Count} module(s) installed.");
        }

        // Déjà confirmé par l'utilisateur : on ne le harcèle plus.
        if (Settings.Current.ConfirmedMemoryMhz == conf)
            return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Ok,
                value, $"You confirmed that {conf} MT/s is indeed the certified speed of your sticks.");

        if (JedecBase.TryGetValue(m.Kind, out var bases) && bases.Contains(conf))
            return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Warn,
                value,
                $"{conf} MT/s is also a standard JEDEC speed for {m.Kind}. Windows does not expose the certified "
                + "speed of your sticks — it lives in their SPD, which only a kernel driver could read. "
                + "So Aeropeek cannot tell a 5600 kit with its profile on from a faster kit with its profile off.",
                "Look in the BIOS, or on the label of the sticks. If the speed shown here matches the one "
                + "you bought them for, confirm it once and this check will stop asking.",
                "confirmer-memoire", "That is the right speed");

        return new CheckResult("ram", "Memory profile (EXPO / XMP)", Severity.Ok,
            value, "Memory is running above the base JEDEC speeds: a performance profile is active.");
    }

    // ---------- 3. Fréquence de rafraîchissement ----------

    static CheckResult RefreshRate(SystemProfile sys)
    {
        var primary = sys.Displays.FirstOrDefault(d => d.IsPrimary) ?? sys.Displays.FirstOrDefault();
        if (primary == null)
            return new CheckResult("hz", "Refresh rate", Severity.Inconnu, "Undetermined", "No display detected.");

        int max = MaxRefreshFor(primary.DeviceName, primary.Width, primary.Height);
        string value = $"{primary.RefreshHz} Hz";

        if (max > 0 && primary.RefreshHz < max)
            return new CheckResult("hz", "Refresh rate", Severity.Probleme,
                value,
                $"Your display accepts {max} Hz at {primary.Width}×{primary.Height} and Windows is running it at {primary.RefreshHz} Hz.",
                $"Settings → System → Display → Advanced display → Refresh rate → {max} Hz. Windows regularly falls back to 60 Hz after a driver update.");

        return new CheckResult("hz", "Refresh rate", Severity.Ok,
            value, $"The display is running at its maximum refresh rate at {primary.Width}×{primary.Height}.");
    }

    static int MaxRefreshFor(string deviceName, int w, int h)
    {
        int max = 0;
        var mode = new Native.DEVMODE { dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
        for (int i = 0; Native.EnumDisplaySettings(deviceName, i, ref mode); i++)
        {
            if (mode.dmPelsWidth == (uint)w && mode.dmPelsHeight == (uint)h)
                max = Math.Max(max, (int)mode.dmDisplayFrequency);
            mode = new Native.DEVMODE { dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
        }
        return max;
    }

    // ---------- 4. Écran branché sur la bonne carte ----------

    static readonly string[] Dedicated = { "NVIDIA", "GeForce", "RTX", "GTX", "Radeon", "AMD", "Arc" };

    // Les circuits graphiques integres d'AMD s'annoncent eux aussi « Radeon » :
    // sans cette liste, un APU passait pour une carte dediee.
    static readonly string[] Integrated =
    {
        "Intel(R) UHD", "Intel(R) HD", "Intel(R) Iris",
        "Radeon(TM) Graphics", "Radeon Graphics", "Radeon(TM) Vega", "Vega Graphics"
    };

    static bool IsIntegrated(string name) =>
        Integrated.Any(i => name.Contains(i, StringComparison.OrdinalIgnoreCase));

    static CheckResult DisplayAdapter(SystemProfile sys)
    {
        var primary = sys.Displays.FirstOrDefault(d => d.IsPrimary) ?? sys.Displays.FirstOrDefault();
        if (primary == null)
            return new CheckResult("gpu-sortie", "Video output", Severity.Inconnu, "Undetermined", "No display detected.");

        // Une puce ne compte comme dediee que si son nom n'est PAS celui d'un
        // circuit integre. Sans cette exclusion, « AMD Radeon(TM) Graphics » —
        // le circuit integre d'un APU — cochait « AMD » et se faisait passer
        // pour une carte dediee. La machine se voyait alors reprocher d'avoir
        // l'ecran branche sur la carte mere, avec pour conseil de rebrancher le
        // cable sur une carte qui n'existe pas.
        bool hasDedicated = sys.GpuNames.Any(
            g => Dedicated.Any(d => g.Contains(d, StringComparison.OrdinalIgnoreCase)) && !IsIntegrated(g));
        bool onIntegrated = IsIntegrated(primary.AdapterName);

        // Sans aucune carte dediee, il n'y a ni probleme ni merite : c'est un
        // fait. L'ancien code tombait ici dans la conclusion « pilote par la
        // carte graphique dediee », qui etait fausse.
        if (!hasDedicated)
            return new CheckResult("gpu-sortie", "Video output", Severity.Info,
                primary.AdapterName,
                "This machine has no dedicated graphics card: display is handled by the chip integrated into the processor.",
                "There is nothing to replug. On CS2, that chip is what will cap your frame rate before anything else does.");

        if (hasDedicated && onIntegrated && !sys.IsLaptop)
            return new CheckResult("gpu-sortie", "Video output", Severity.Probleme,
                primary.AdapterName,
                "Your main display is being driven by the integrated graphics while a dedicated card is present.",
                "Move the cable to the graphics card, not the motherboard. It is the single biggest gain on this list.");

        if (sys.IsLaptop && onIntegrated)
            return new CheckResult("gpu-sortie", "Video output", Severity.Info,
                primary.AdapterName,
                "Hybrid display: the screen goes through the integrated chip, which is normal on a laptop.",
                "If your laptop has a MUX switch, dedicated-card mode lowers latency.");

        return new CheckResult("gpu-sortie", "Video output", Severity.Ok,
            primary.AdapterName, "The main display is driven by the dedicated graphics card.");
    }

    // ---------- 4 bis. Âge du pilote graphique ----------

    static CheckResult GraphicsDriver(SystemProfile sys)
    {
        var d = Drivers.Display();
        if (d == null || d.Date == null)
            return new CheckResult("gpu-driver", "Graphics driver", Severity.Inconnu,
                "Undetermined", "The driver date could not be read.");

        int months = d.AgeMonths ?? 0;
        bool nvidia = d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
        string version = nvidia ? Drivers.Pretty(d.Version) : d.Version;
        string value = $"{version} · {d.Date:MM/yyyy}";
        string installed = $"Installed version: {version}, released {d.Date:yyyy-MM} — {months} months ago.";

        // Une recherche a été faite : on compare des faits, pas des indices.
        if (Drivers.Latest is { } latest)
        {
            int cmp = Drivers.Compare(version, latest.Version);

            if (cmp < 0)
                return new CheckResult("gpu-driver", "Graphics driver", Severity.Probleme,
                    $"{version} → {latest.Version}",
                    $"{installed} NVIDIA publie la {latest.Version}"
                    + (latest.Date.Length > 0 ? $", released {latest.Date}." : "."),
                    "An out-of-date driver sometimes costs more than every tweak on this list put together, "
                    + "especially after a game's major update.",
                    "telecharger-pilote", "Download the update");

            return new CheckResult("gpu-driver", "Graphics driver", Severity.Ok, value,
                $"{installed} This is the latest version NVIDIA has published.",
                "", "chercher-pilote", "Check again");
        }

        // Aucune recherche : on énonce ce qu'on sait, sans rien supposer.
        var severity = Severity.Info;
        string detail = installed + " Aeropeek does not know whether a newer version exists: "
                      + "you have to ask it to, because that means querying NVIDIA.";

        if (!nvidia)
        {
            detail = installed + " Automatic lookup is only available for NVIDIA cards.";
            return new CheckResult("gpu-driver", "Graphics driver", severity, value, detail);
        }

        if (Drivers.LastLookupError is { } err)
            detail = installed + " " + err;

        return new CheckResult("gpu-driver", "Graphics driver", severity, value, detail,
            "", "chercher-pilote", "Check for an update");
    }

    // ---------- 4 ter. Disque du jeu ----------

    static CheckResult GameDrive(SystemProfile sys)
    {
        var path = Storage.GamePath();
        if (path == null)
            return new CheckResult("game-drive", "Game disk", Severity.Inconnu,
                "CS2 not found", "The installation could not be located through Steam.");

        char letter = char.ToUpperInvariant(path[0]);
        var disk = Storage.DiskFor(letter);
        var all = Storage.Disks();

        if (disk == null)
            return new CheckResult("game-drive", "Game disk", Severity.Inconnu,
                $"Lecteur {letter}:", $"Installed in {path}, but the physical disk could not be identified.");

        string value = $"{letter}: · {disk.Label}";
        var fastest = all.OrderByDescending(d => d.Rank).FirstOrDefault();

        if (!disk.IsSsd)
            return new CheckResult("game-drive", "Game disk", Severity.Probleme, value,
                $"CS2 is installed on a {disk.MediaType} ({disk.Name}). Load times and shader "
                + "compilation suffer for it, and stutters can appear the first time you cross an area.",
                fastest is { IsSsd: true }
                    ? $"You have a {fastest.Label} in this machine ({fastest.Name}). Move the game onto it: "
                      + "Steam → Properties → Installed Files → Move install folder."
                    : "An SSD is the only real remedy here.");

        if (fastest != null && fastest.Rank > disk.Rank)
            return new CheckResult("game-drive", "Game disk", Severity.Warn, value,
                $"CS2 sits on a {disk.Label} while this machine has a {fastest.Label} ({fastest.Name}).",
                "The difference shows up mostly in load times, little in game. Worth moving if you have the room.");

        return new CheckResult("game-drive", "Game disk", Severity.Ok, value,
            $"CS2 is installed on the fastest disk in the machine ({disk.Name}).");
    }

    // ---------- 4 quater. Réglages du pilote NVIDIA ----------

    static CheckResult NvidiaSettings(SystemProfile sys)
    {
        bool nvidia = sys.GpuNames.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        if (!nvidia)
            return new CheckResult("nvidia", "Graphics driver settings", Severity.Info,
                "Not applicable", "This check only concerns NVIDIA cards.");

        var settings = Nvidia.ReadSettings();
        if (settings.Count == 0)
            return new CheckResult("nvidia", "Graphics driver settings", Severity.Inconnu,
                "Unreadable", "The driver's global profile could not be read.");

        var lines = settings.Select(s => $"· {s.Name}: {s.Reading}");
        var power = settings.FirstOrDefault(s => s.Id == Nvidia.PowerModeId);

        string detail = string.Join(Environment.NewLine, lines);
        string advice = "Of the five settings it reads, Aeropeek writes only one: power management, "
                      + "whose value table is documented and whose effect is measurable. For the others, the driver "
                      + "gives the option's name but not the meaning of its values — the application does not guess, "
                      + "and points you to the NVIDIA panel.";

        if (power is { Optimal: false })
            return new CheckResult("nvidia", "Graphics driver settings", Severity.Warn,
                power.Reading, detail,
                "Power management set to “Prefer maximum performance” stops the card from "
                + "dropping its clocks between two frames. " + advice,
                "nvidia-performances", "Prefer performance");

        return new CheckResult("nvidia", "Graphics driver settings", Severity.Ok,
            $"{settings.Count} settings read", detail, advice,
            "ouvrir-nvidia", "Open the NVIDIA panel");
    }

    // ---------- 5. Topologie processeur ----------

    static CheckResult CpuTopology(SystemProfile sys)
    {
        var t = sys.Cpu;
        if (t.PhysicalCores == 0)
            return new CheckResult("cpu", "Processor cores", Severity.Inconnu, "Undetermined", "Topology unreadable.");

        if (!t.IsHybrid)
            return new CheckResult("cpu", "Processor cores", Severity.Ok,
                $"{t.PhysicalCores} cores / {t.LogicalCores} threads",
                "Uniform architecture: no affinity tweak is needed.");

        string range = MaskToRange(t.PerformanceMask);
        var live = GameAffinity.Current();

        // CS2 n'est pas lancé : c'est un fait sur ta machine, pas un défaut à corriger.
        if (live == null)
            return new CheckResult("cpu", "Processor cores", Severity.Info,
                $"{t.PerformanceCores} P-cores + {t.EfficiencyCores} E-cores",
                $"Hybrid processor. When CS2 threads land on the efficiency cores, the 1% lows drop.",
                $"Start CS2 then run the scan again: Aeropeek will be able to pin the game to processors {range} in one click.");

        if (live == t.PerformanceMask)
            return new CheckResult("cpu", "Processor cores", Severity.Ok,
                "Pinned to the P-cores",
                $"CS2 runs on processors {range} only: no thread lands on the efficiency cores.",
                "Affinity is lost when the game closes — you will have to apply it again next session.",
                "affinite-tous", "Give back every core");

        return new CheckResult("cpu", "Processor cores", Severity.Info,
            $"{t.PerformanceCores} P-cores + {t.EfficiencyCores} E-cores",
            "CS2 runs on every core, efficiency ones included. That is the normal behaviour: Windows spreads the threads itself.",
            $"Pinning to processors {range} helps on some configurations and changes nothing on many others — "
            + "on recent machines, the Windows scheduler already makes the right call. "
            + "Only keep it if the Benchmark tab shows a real difference: measure without, pin, measure again.",
            "affinite-pcores", "Pin CS2");
    }

    static string MaskToRange(ulong mask)
    {
        var bits = new List<int>();
        for (int i = 0; i < 64; i++) if ((mask & (1UL << i)) != 0) bits.Add(i);
        if (bits.Count == 0) return "performance";
        return bits.Count == bits[^1] - bits[0] + 1 ? $"{bits[0]} to {bits[^1]}" : string.Join(", ", bits);
    }

    // ---------- 6. Plan d'alimentation ----------

    static readonly Dictionary<string, (string Name, Severity Sev)> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["381b4222-f694-41f0-9685-ff5bb260df2e"] = ("Normal use", Severity.Warn),
        ["a1841308-3541-4fab-bc81-f71556f20b4a"] = ("Power saver", Severity.Probleme),
        ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = ("High performance", Severity.Ok),
        ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = ("Maximum performance", Severity.Ok)
    };

    // Sous-groupes et paramètres d'alimentation, identifiants stables de Windows.
    const string SUB_PROCESSOR = "54533251-82be-4824-96c1-47b60b740d00";
    const string PROCTHROTTLEMIN = "893dee8e-2bef-41e0-89c6-b55d0929964c";  // état minimal du processeur
    const string CPMINCORES = "0cc5b647-c1df-4637-891a-dec35c318583";       // parking de cœurs
    const string SUB_USB = "2a737441-1930-4402-8d77-b2bebba308a3";
    const string USB_SUSPEND = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";      // suspension sélective USB

    /// <summary>
    /// Interroge un paramètre du plan actif. La sortie de powercfg est traduite,
    /// mais les valeurs sont toujours en hexadécimal : les deux dernières sont
    /// l'index secteur puis l'index batterie.
    /// </summary>
    static int? PowerSetting(string subgroup, string setting)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powercfg.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in new[] { "/query", "SCHEME_CURRENT", subgroup, setting }) psi.ArgumentList.Add(a);

            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            var hex = System.Text.RegularExpressions.Regex.Matches(output, @"0x([0-9a-fA-F]{8})");
            if (hex.Count < 2) return null;
            return Convert.ToInt32(hex[^2].Groups[1].Value, 16);   // avant-dernière = secteur
        }
        catch { return null; }
    }

    static string ActivePlanName()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powercfg.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("/getactivescheme");
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return "";
            string s = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var m = System.Text.RegularExpressions.Regex.Match(s, @"\(([^)]+)\)\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }
        catch { return ""; }
    }

    static CheckResult PowerPlan(SystemProfile sys)
    {
        var v = RegistryOps.ReadValue("HKLM", @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme");
        string guid = v?.ToString() ?? "";
        string name = ActivePlanName();
        if (string.IsNullOrEmpty(name))
            name = Schemes.TryGetValue(guid, out var known) ? known.Name : "Custom plan";

        // On juge ce que le plan FAIT, pas comment il s'appelle.
        int? minState = PowerSetting(SUB_PROCESSOR, PROCTHROTTLEMIN);
        int? minCores = PowerSetting(SUB_PROCESSOR, CPMINCORES);
        int? usbSuspend = PowerSetting(SUB_USB, USB_SUSPEND);

        if (minState == null && minCores == null && usbSuspend == null)
            return new CheckResult("power", "Power plan", Severity.Inconnu, name,
                "The plan's settings could not be read.");

        var facts = new List<string>();
        if (minState != null) facts.Add($"minimum processor state {minState}%");
        if (minCores != null) facts.Add($"minimum active cores {minCores}%");
        if (usbSuspend != null) facts.Add("USB suspend " + (usbSuspend == 1 ? "enabled" : "disabled"));
        string detail = "On mains power: " + string.Join(", ", facts) + ".";

        var issues = new List<string>();
        if (minCores is > 0 and < 100) issues.Add("core parking is active");
        if (usbSuspend == 1) issues.Add("USB selective suspend is enabled");

        if (issues.Count == 0)
            return new CheckResult("power", "Power plan", Severity.Ok, name,
                detail + " No core is parked and the USB ports stay powered.",
                "", "power-panel", "Manage plans");

        if (sys.IsLaptop)
            return new CheckResult("power", "Power plan", Severity.Info, name,
                detail + " On a laptop, those savings prevent overheating and throttling.",
                "Only change it while on mains power, and watch the temperatures.",
                "power-panel", "Manage plans");

        return new CheckResult("power", "Power plan", Severity.Warn, name,
            detail + " On a desktop, " + string.Join(" and ", issues) + " with no real benefit.",
            "Modest effect — a few frames on short transitions, and some mouse latency if USB suspend "
            + "affects your receiver.",
            "power-panel", "Manage plans");
    }

    // ---------- 7. Surcouches accrochées au jeu ----------

    static readonly (string Fragment, string Label)[] KnownOverlays =
    {
        ("discordhook",           "Discord"),
        ("rtsshooks",             "RivaTuner / MSI Afterburner"),
        ("gameoverlayrenderer",   "Steam overlay"),
        ("nvspcap",               "NVIDIA / GeForce Experience"),
        ("nvcamera",              "NVIDIA ShadowPlay"),
        ("rzchromasdk",           "Razer Chroma"),
        ("razerhook",             "Razer Synapse"),
        ("icue",                  "Corsair iCUE"),
        ("graphics-hook",         "OBS"),
        ("medal",                 "Medal"),
        ("overwolf",              "Overwolf"),
        ("wallpaper",             "Wallpaper Engine")
    };

    /// <summary>
    /// Applications connues pour s'accrocher aux jeux. On les détecte par leur propre
    /// processus : contrairement aux modules du jeu, elles restent toujours lisibles,
    /// anticheat ou non.
    /// </summary>
    static readonly (string Process, string Label)[] OverlayApps =
    {
        ("Discord",         "Discord"),
        ("DiscordCanary",   "Discord"),
        ("DiscordPTB",      "Discord"),
        ("RTSS",            "RivaTuner Statistics Server"),
        ("MSIAfterburner",  "MSI Afterburner"),
        ("NVIDIA Share",    "NVIDIA overlay"),
        ("NVIDIA Overlay",  "NVIDIA overlay"),
        ("Overwolf",        "Overwolf"),
        ("Medal",           "Medal"),
        ("iCUE",            "Corsair iCUE"),
        ("RazerSynapse",    "Razer Synapse"),
        ("RazerCentralService", "Razer Synapse"),
        ("obs64",           "OBS"),
        ("wallpaper64",     "Wallpaper Engine"),
        ("wallpaper32",     "Wallpaper Engine")
    };

    static List<string> RunningOverlayApps()
    {
        var found = new List<string>();
        foreach (var (proc, label) in OverlayApps)
        {
            if (found.Contains(label)) continue;
            try
            {
                var ps = System.Diagnostics.Process.GetProcessesByName(proc);
                if (ps.Length > 0) found.Add(label);
                foreach (var p in ps) p.Dispose();
            }
            catch { }
        }
        return found;
    }

    static CheckResult GameOverlays(SystemProfile sys)
    {
        var (access, modules) = SystemProfile.LoadedModules("cs2");
        var apps = RunningOverlayApps();

        // Cas idéal : on lit les modules du jeu et on sait exactement qui s'y est accroché.
        if (access == SystemProfile.ModuleAccess.Ok)
        {
            var hooked = new List<string>();
            foreach (var (frag, label) in KnownOverlays)
                if (modules.Any(m => m.Contains(frag, StringComparison.OrdinalIgnoreCase)) && !hooked.Contains(label))
                    hooked.Add(label);

            if (hooked.Count == 0)
                return new CheckResult("overlays", "Overlays", Severity.Ok,
                    "None hooked into the game",
                    $"{modules.Count} modules loaded into CS2, none of them a known overlay.");

            return new CheckResult("overlays", "Overlays", hooked.Count >= 3 ? Severity.Probleme : Severity.Warn,
                string.Join(", ", hooked),
                $"{hooked.Count} overlay(s) hooked into the CS2 process. Each one costs frames and can cause stutter.",
                "Turn off in-game overlays in the applications concerned. Discord's is switched off under Settings → Game Overlay.");
        }

        // Sinon on ne peut pas interroger le jeu : on regarde les surcouches elles-mêmes.
        if (apps.Count == 0)
            return new CheckResult("overlays", "Overlays", Severity.Ok,
                "None running",
                "None of the applications known to hook into games is running: no Discord, no RivaTuner, "
                + "ni Overwolf, ni iCUE, ni Razer Synapse.");

        string why = access == SystemProfile.ModuleAccess.Denied
            ? "Cannot verify which ones are actually hooked into CS2: reading its modules is blocked by "
              + (sys.AntiCheats.Count > 0 ? string.Join(" and ", sys.AntiCheats) : "your anti-cheat")
            : "With CS2 not running, there is no way to tell which ones will hook into it";

        return new CheckResult("overlays", "Overlays", apps.Count >= 3 ? Severity.Warn : Severity.Info,
            string.Join(", ", apps),
            $"{apps.Count} application(s) able to hook into the game are running right now. {why}.",
            "Each costs frames while its overlay is active. Switch off the ones you don't need in game: "
            + "Discord → Settings → Game Overlay.");
    }
}

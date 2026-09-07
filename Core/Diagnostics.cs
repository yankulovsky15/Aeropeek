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
                    "Non déterminé", "La vérification a échoué : " + ex.Message));
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
            return new CheckResult("hvci", "Intégrité de la mémoire", Severity.Inconnu,
                "Non déterminé", "Impossible de lire l'état de la sécurité basée sur la virtualisation.");

        // Désactivée mais encore active jusqu'au redémarrage
        if (running == true && configured == false)
            return new CheckResult("hvci", "Intégrité de la mémoire", Severity.Warn,
                "Redémarrage requis",
                "Tu l'as désactivée, mais HVCI continue de tourner : le noyau reste sous hyperviseur jusqu'au prochain démarrage de Windows.",
                "Redémarre pour que le gain soit effectif, puis relance l'analyse.");

        // Activée mais pas encore chargée
        if (running == false && configured == true)
            return new CheckResult("hvci", "Intégrité de la mémoire", Severity.Warn,
                "Activée au prochain démarrage",
                "HVCI ne tourne pas actuellement, mais il est configuré pour démarrer au prochain redémarrage.",
                "Si tu ne le veux pas, désactive-le maintenant : Sécurité Windows → Sécurité des appareils → Isolation du noyau.");

        if (running == true || (running == null && configured == true))
            return new CheckResult("hvci", "Intégrité de la mémoire", Severity.Probleme,
                "Activée",
                "L'intégrité de la mémoire (HVCI) fait tourner le noyau sous hyperviseur. Le coût est permanent et se voit surtout sur les 1% lows.",
                "C'est le plus gros gain logiciel disponible : 5 à 10 %. Sécurité Windows → Sécurité des appareils → Isolation du noyau. "
                + (sys.AntiCheats.Contains("FACEIT Anti-Cheat")
                    ? "FACEIT exige Secure Boot et TPM, pas HVCI : tu peux le désactiver sans casser l'anticheat."
                    : "Vérifie que ton anticheat ne l'exige pas."),
                "ouvrir-isolation", "Ouvrir le réglage");

        return new CheckResult("hvci", "Intégrité de la mémoire", Severity.Ok,
            "Désactivée", "Aucun surcoût d'hyperviseur sur l'exécution du noyau.");
    }

    // ---------- 1 bis. Hyperviseur chargé sans utilisateur ----------

    /// <summary>
    /// Scénarios VBS déclarés sous DeviceGuard\Scenarios. Un seul activé suffit à
    /// maintenir l'hyperviseur chargé, y compris quand hypervisorlaunchtype vaut off.
    /// </summary>
    static readonly (string Scenario, string Label)[] VbsScenarios =
    {
        ("HypervisorEnforcedCodeIntegrity", "intégrité de la mémoire"),
        ("WindowsHello",                    "Windows Hello (connexion sécurisée renforcée)"),
        ("KernelShadowStacks",              "protection de la pile noyau"),
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
            return new CheckResult("vbs", "Hyperviseur (VBS)", Severity.Inconnu,
                "Non déterminé", "L'état de la sécurité basée sur la virtualisation n'a pas pu être lu.");
        }

        if (status != 2)
            return new CheckResult("vbs", "Hyperviseur (VBS)", Severity.Ok,
                "Inactif", "Windows ne s'exécute pas sous hyperviseur : aucun surcoût de virtualisation.");

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
            return new CheckResult("vbs", "Hyperviseur (VBS)", Severity.Warn,
                "Actif avec services",
                "Des services de sécurité s'exécutent sous hyperviseur" +
                (users.Count > 0 ? " : " + string.Join(", ", users) + "." : "."),
                "C'est là qu'est le coût réel. Voir la carte « Intégrité de la mémoire », qui en est la principale source.");

        // Cas résiduel : l'hyperviseur est chargé mais aucun service de sécurité ne tourne dedans.
        if (users.Count > 0)
            return new CheckResult("vbs", "Hyperviseur (VBS)", Severity.Info,
                "Chargé, sans service actif",
                "Aucun service de sécurité ne tourne sous hyperviseur — le gros du coût a déjà disparu. " +
                "Il reste chargé parce que quelque chose l'exige : " + string.Join(", ", users) + ".",
                "Le coût résiduel est faible, quelques pourcents au plus. Le supprimer demanderait de renoncer à ces fonctionnalités : " +
                "rarement un bon échange, en particulier pour Windows Hello.");

        return new CheckResult("vbs", "Hyperviseur (VBS)", Severity.Warn,
            "Chargé sans utilisateur identifié",
            "L'hyperviseur est chargé alors qu'aucun service de sécurité ne tourne dedans et qu'aucune fonctionnalité connue ne l'exige.",
            "Onglet Réglages → « Sécurité basée sur la virtualisation ». Gain modeste. À remettre avant d'installer WSL, Docker ou Windows Sandbox.");
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
            return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Inconnu,
                "Non déterminé", "Les modules mémoire n'ont pas pu être lus.");

        var m = sys.Memory[0];
        uint conf = m.ConfiguredMhz;
        string kind = string.IsNullOrEmpty(m.Kind) ? "" : m.Kind + " ";
        string value = conf > 0 ? $"{kind}{conf} MT/s" : "Non déterminé";

        if (conf == 0)
            return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Inconnu,
                value, "La fréquence configurée n'a pas pu être lue.");

        if (m.RatedMhz > 0 && conf < m.RatedMhz)
            return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Probleme,
                value,
                $"Tes modules sont donnés pour {m.RatedMhz} MT/s et tournent à {conf} MT/s.",
                "Active le profil EXPO (AMD) ou XMP (Intel) dans le BIOS. Sur CS2, très sensible à la latence mémoire, c'est 10 à 15 % sur les 1% lows.");

        // La référence du module donne la fréquence certifiée : c'est la réponse
        // fiable, sans avoir à interroger le SPD ni à demander à l'utilisateur.
        uint fromPart = RatedFromPartNumber(m.PartNumber);
        if (fromPart > 0)
        {
            string pn = string.IsNullOrWhiteSpace(m.PartNumber) ? "" : $" ({m.PartNumber})";
            if (conf < fromPart)
                return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Probleme,
                    value,
                    $"Tes barrettes{pn} sont certifiées pour {fromPart} MT/s et tournent à {conf} MT/s.",
                    "Active EXPO (AMD) ou XMP (Intel) dans le BIOS. Sur CS2, très sensible à la latence mémoire, "
                    + "c'est 10 à 15 % sur les 1% lows — davantage que tous les réglages logiciels réunis.");

            return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Ok,
                value,
                $"Tes barrettes{pn} sont certifiées pour {fromPart} MT/s et tournent bien à cette fréquence. "
                + $"{sys.Memory.Count} module(s) installé(s).");
        }

        // Déjà confirmé par l'utilisateur : on ne le harcèle plus.
        if (Settings.Current.ConfirmedMemoryMhz == conf)
            return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Ok,
                value, $"Tu as confirmé que {conf} MT/s est bien la fréquence certifiée de tes barrettes.");

        if (JedecBase.TryGetValue(m.Kind, out var bases) && bases.Contains(conf))
            return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Warn,
                value,
                $"{conf} MT/s est aussi une fréquence JEDEC standard pour la {m.Kind}. Windows n'expose pas la fréquence "
                + "certifiée de tes barrettes — elle est dans leur SPD, que seul un pilote noyau pourrait lire. "
                + "Aeropeek ne peut donc pas distinguer un kit 5600 avec profil actif d'un kit plus rapide avec profil éteint.",
                "Regarde dans le BIOS, ou sur l'étiquette des barrettes. Si la fréquence affichée ici correspond à celle "
                + "pour laquelle tu les as achetées, confirme-le une fois : la vérification n'y reviendra plus.",
                "confirmer-memoire", "C'est la bonne fréquence");

        return new CheckResult("ram", "Profil mémoire (EXPO / XMP)", Severity.Ok,
            value, "La mémoire tourne au-dessus des fréquences JEDEC de base : un profil de performance est actif.");
    }

    // ---------- 3. Fréquence de rafraîchissement ----------

    static CheckResult RefreshRate(SystemProfile sys)
    {
        var primary = sys.Displays.FirstOrDefault(d => d.IsPrimary) ?? sys.Displays.FirstOrDefault();
        if (primary == null)
            return new CheckResult("hz", "Fréquence d'écran", Severity.Inconnu, "Non déterminé", "Aucun écran détecté.");

        int max = MaxRefreshFor(primary.DeviceName, primary.Width, primary.Height);
        string value = $"{primary.RefreshHz} Hz";

        if (max > 0 && primary.RefreshHz < max)
            return new CheckResult("hz", "Fréquence d'écran", Severity.Probleme,
                value,
                $"Ton écran accepte {max} Hz en {primary.Width}×{primary.Height} et Windows le fait tourner à {primary.RefreshHz} Hz.",
                $"Paramètres → Système → Affichage → Paramètres avancés → Fréquence d'actualisation → {max} Hz. Windows retombe régulièrement à 60 Hz après une mise à jour de pilote.");

        return new CheckResult("hz", "Fréquence d'écran", Severity.Ok,
            value, $"L'écran tourne à sa fréquence maximale en {primary.Width}×{primary.Height}.");
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
            return new CheckResult("gpu-sortie", "Sortie vidéo", Severity.Inconnu, "Non déterminé", "Aucun écran détecté.");

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
            return new CheckResult("gpu-sortie", "Sortie vidéo", Severity.Info,
                primary.AdapterName,
                "Cette machine n'a pas de carte graphique dédiée : l'affichage est assuré par le circuit intégré au processeur.",
                "Il n'y a rien à rebrancher. Sur CS2, c'est ce circuit qui limitera le nombre d'images avant tout le reste.");

        if (hasDedicated && onIntegrated && !sys.IsLaptop)
            return new CheckResult("gpu-sortie", "Sortie vidéo", Severity.Probleme,
                primary.AdapterName,
                "Ton écran principal est piloté par le circuit graphique intégré alors qu'une carte dédiée est présente.",
                "Rebranche le câble sur la carte graphique, pas sur la carte mère. C'est le plus gros gain possible sur cette liste.");

        if (sys.IsLaptop && onIntegrated)
            return new CheckResult("gpu-sortie", "Sortie vidéo", Severity.Info,
                primary.AdapterName,
                "Affichage hybride : l'écran passe par le circuit intégré, ce qui est normal sur un portable.",
                "Si ton portable a un interrupteur MUX, le mode carte dédiée réduit la latence.");

        return new CheckResult("gpu-sortie", "Sortie vidéo", Severity.Ok,
            primary.AdapterName, "L'écran principal est piloté par la carte graphique dédiée.");
    }

    // ---------- 4 bis. Âge du pilote graphique ----------

    static CheckResult GraphicsDriver(SystemProfile sys)
    {
        var d = Drivers.Display();
        if (d == null || d.Date == null)
            return new CheckResult("gpu-driver", "Pilote graphique", Severity.Inconnu,
                "Non déterminé", "La date du pilote n'a pas pu être lue.");

        int months = d.AgeMonths ?? 0;
        bool nvidia = d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
        string version = nvidia ? Drivers.Pretty(d.Version) : d.Version;
        string value = $"{version} · {d.Date:MM/yyyy}";
        string installed = $"Version installée : {version}, publiée en {d.Date:MM/yyyy} — il y a {months} mois.";

        // Une recherche a été faite : on compare des faits, pas des indices.
        if (Drivers.Latest is { } latest)
        {
            int cmp = Drivers.Compare(version, latest.Version);

            if (cmp < 0)
                return new CheckResult("gpu-driver", "Pilote graphique", Severity.Probleme,
                    $"{version} → {latest.Version}",
                    $"{installed} NVIDIA publie la {latest.Version}"
                    + (latest.Date.Length > 0 ? $", parue le {latest.Date}." : "."),
                    "Un pilote en retard coûte parfois plus que tous les réglages de cette liste réunis, "
                    + "surtout après une mise à jour majeure d'un jeu.",
                    "telecharger-pilote", "Télécharger la mise à jour");

            return new CheckResult("gpu-driver", "Pilote graphique", Severity.Ok, value,
                $"{installed} C'est la dernière version publiée par NVIDIA.",
                "", "chercher-pilote", "Revérifier");
        }

        // Aucune recherche : on énonce ce qu'on sait, sans rien supposer.
        var severity = Severity.Info;
        string detail = installed + " Aeropeek ne sait pas si une version plus récente existe : "
                      + "il faut le lui demander, car cela suppose d'interroger NVIDIA.";

        if (!nvidia)
        {
            detail = installed + " La recherche automatique n'est disponible que pour les cartes NVIDIA.";
            return new CheckResult("gpu-driver", "Pilote graphique", severity, value, detail);
        }

        if (Drivers.LastLookupError is { } err)
            detail = installed + " " + err;

        return new CheckResult("gpu-driver", "Pilote graphique", severity, value, detail,
            "", "chercher-pilote", "Chercher une mise à jour");
    }

    // ---------- 4 ter. Disque du jeu ----------

    static CheckResult GameDrive(SystemProfile sys)
    {
        var path = Storage.GamePath();
        if (path == null)
            return new CheckResult("game-drive", "Disque du jeu", Severity.Inconnu,
                "CS2 introuvable", "L'installation n'a pas été localisée via Steam.");

        char letter = char.ToUpperInvariant(path[0]);
        var disk = Storage.DiskFor(letter);
        var all = Storage.Disks();

        if (disk == null)
            return new CheckResult("game-drive", "Disque du jeu", Severity.Inconnu,
                $"Lecteur {letter}:", $"Installé dans {path}, mais le disque physique n'a pas pu être identifié.");

        string value = $"{letter}: · {disk.Label}";
        var fastest = all.OrderByDescending(d => d.Rank).FirstOrDefault();

        if (!disk.IsSsd)
            return new CheckResult("game-drive", "Disque du jeu", Severity.Probleme, value,
                $"CS2 est installé sur un {disk.MediaType} ({disk.Name}). Les temps de chargement et la compilation "
                + "des shaders en pâtissent, et des saccades peuvent apparaître au premier passage sur une zone.",
                fastest is { IsSsd: true }
                    ? $"Tu as un {fastest.Label} dans cette machine ({fastest.Name}). Déplace le jeu dessus : "
                      + "Steam → Propriétés → Fichiers installés → Déplacer le dossier d'installation."
                    : "Un SSD est le seul vrai remède ici.");

        if (fastest != null && fastest.Rank > disk.Rank)
            return new CheckResult("game-drive", "Disque du jeu", Severity.Warn, value,
                $"CS2 est sur un {disk.Label} alors que cette machine dispose d'un {fastest.Label} ({fastest.Name}).",
                "L'écart se voit surtout sur les temps de chargement, peu en jeu. À déplacer si la place le permet.");

        return new CheckResult("game-drive", "Disque du jeu", Severity.Ok, value,
            $"CS2 est installé sur le disque le plus rapide de la machine ({disk.Name}).");
    }

    // ---------- 4 quater. Réglages du pilote NVIDIA ----------

    static CheckResult NvidiaSettings(SystemProfile sys)
    {
        bool nvidia = sys.GpuNames.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        if (!nvidia)
            return new CheckResult("nvidia", "Réglages du pilote graphique", Severity.Info,
                "Sans objet", "Cette vérification ne concerne que les cartes NVIDIA.");

        var settings = Nvidia.ReadSettings();
        if (settings.Count == 0)
            return new CheckResult("nvidia", "Réglages du pilote graphique", Severity.Inconnu,
                "Illisibles", "Le profil global du pilote n'a pas pu être lu.");

        var lines = settings.Select(s => $"· {s.Name} : {s.Reading}");
        var power = settings.FirstOrDefault(s => s.Id == Nvidia.PowerModeId);

        string detail = string.Join(Environment.NewLine, lines);
        string advice = "Des cinq réglages lus, Aeropeek n'en écrit qu'un : la gestion de l'alimentation, "
                      + "dont la table de valeurs est documentée et l'effet mesurable. Pour les autres, le pilote "
                      + "donne le nom de l'option mais pas le sens de ses valeurs — l'application ne devine pas, "
                      + "et renvoie au panneau NVIDIA.";

        if (power is { Optimal: false })
            return new CheckResult("nvidia", "Réglages du pilote graphique", Severity.Warn,
                power.Reading, detail,
                "La gestion de l'alimentation en « Privilégier les performances maximales » évite que la carte "
                + "redescende en fréquence entre deux images. " + advice,
                "nvidia-performances", "Privilégier les performances");

        return new CheckResult("nvidia", "Réglages du pilote graphique", Severity.Ok,
            $"{settings.Count} réglages lus", detail, advice,
            "ouvrir-nvidia", "Ouvrir le panneau NVIDIA");
    }

    // ---------- 5. Topologie processeur ----------

    static CheckResult CpuTopology(SystemProfile sys)
    {
        var t = sys.Cpu;
        if (t.PhysicalCores == 0)
            return new CheckResult("cpu", "Cœurs du processeur", Severity.Inconnu, "Non déterminé", "Topologie illisible.");

        if (!t.IsHybrid)
            return new CheckResult("cpu", "Cœurs du processeur", Severity.Ok,
                $"{t.PhysicalCores} cœurs / {t.LogicalCores} threads",
                "Architecture homogène : aucun réglage d'affinité n'est nécessaire.");

        string range = MaskToRange(t.PerformanceMask);
        var live = GameAffinity.Current();

        // CS2 n'est pas lancé : c'est un fait sur ta machine, pas un défaut à corriger.
        if (live == null)
            return new CheckResult("cpu", "Cœurs du processeur", Severity.Info,
                $"{t.PerformanceCores} P-cores + {t.EfficiencyCores} E-cores",
                $"Processeur hybride. Quand des threads de CS2 sont placés sur les cœurs efficients, les 1% lows chutent.",
                $"Lance CS2 puis relance l'analyse : Aeropeek pourra épingler le jeu sur les processeurs {range} d'un clic.");

        if (live == t.PerformanceMask)
            return new CheckResult("cpu", "Cœurs du processeur", Severity.Ok,
                "Épinglé sur les P-cores",
                $"CS2 tourne sur les processeurs {range} uniquement : aucun thread ne part sur les cœurs efficients.",
                "L'affinité est perdue à la fermeture du jeu — il faudra la réappliquer à la prochaine session.",
                "affinite-tous", "Rendre tous les cœurs");

        return new CheckResult("cpu", "Cœurs du processeur", Severity.Info,
            $"{t.PerformanceCores} P-cores + {t.EfficiencyCores} E-cores",
            "CS2 tourne sur tous les cœurs, efficients compris. C'est le comportement normal : Windows répartit lui-même les threads.",
            $"L'épinglage sur les processeurs {range} aide sur certaines configurations et ne change rien sur beaucoup d'autres — "
            + "sur les machines récentes, l'ordonnanceur de Windows fait déjà le bon choix. "
            + "À ne garder que si l'onglet Benchmark montre un écart réel : mesure sans, épingle, remesure.",
            "affinite-pcores", "Épingler CS2");
    }

    static string MaskToRange(ulong mask)
    {
        var bits = new List<int>();
        for (int i = 0; i < 64; i++) if ((mask & (1UL << i)) != 0) bits.Add(i);
        if (bits.Count == 0) return "de performance";
        return bits.Count == bits[^1] - bits[0] + 1 ? $"{bits[0]} à {bits[^1]}" : string.Join(", ", bits);
    }

    // ---------- 6. Plan d'alimentation ----------

    static readonly Dictionary<string, (string Name, Severity Sev)> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["381b4222-f694-41f0-9685-ff5bb260df2e"] = ("Utilisation normale", Severity.Warn),
        ["a1841308-3541-4fab-bc81-f71556f20b4a"] = ("Économie d'énergie", Severity.Probleme),
        ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = ("Performances élevées", Severity.Ok),
        ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = ("Performances maximales", Severity.Ok)
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
            name = Schemes.TryGetValue(guid, out var known) ? known.Name : "Plan personnalisé";

        // On juge ce que le plan FAIT, pas comment il s'appelle.
        int? minState = PowerSetting(SUB_PROCESSOR, PROCTHROTTLEMIN);
        int? minCores = PowerSetting(SUB_PROCESSOR, CPMINCORES);
        int? usbSuspend = PowerSetting(SUB_USB, USB_SUSPEND);

        if (minState == null && minCores == null && usbSuspend == null)
            return new CheckResult("power", "Plan d'alimentation", Severity.Inconnu, name,
                "Les réglages du plan n'ont pas pu être lus.");

        var facts = new List<string>();
        if (minState != null) facts.Add($"état minimal du processeur {minState} %");
        if (minCores != null) facts.Add($"cœurs actifs minimum {minCores} %");
        if (usbSuspend != null) facts.Add("suspension USB " + (usbSuspend == 1 ? "activée" : "désactivée"));
        string detail = "Sur secteur : " + string.Join(", ", facts) + ".";

        var issues = new List<string>();
        if (minCores is > 0 and < 100) issues.Add("le parking de cœurs est actif");
        if (usbSuspend == 1) issues.Add("la suspension sélective USB est activée");

        if (issues.Count == 0)
            return new CheckResult("power", "Plan d'alimentation", Severity.Ok, name,
                detail + " Aucun cœur n'est mis en veille et les ports USB restent alimentés.",
                "", "power-panel", "Gérer les plans");

        if (sys.IsLaptop)
            return new CheckResult("power", "Plan d'alimentation", Severity.Info, name,
                detail + " Sur un portable, ces économies évitent la surchauffe et le throttling.",
                "À ne modifier que branché sur secteur, en surveillant les températures.",
                "power-panel", "Gérer les plans");

        return new CheckResult("power", "Plan d'alimentation", Severity.Warn, name,
            detail + " Sur un poste fixe, " + string.Join(" et ", issues) + " sans réel bénéfice.",
            "Effet modeste — quelques images sur les transitions courtes, et un peu de latence souris si la suspension USB "
            + "touche ton récepteur.",
            "power-panel", "Gérer les plans");
    }

    // ---------- 7. Surcouches accrochées au jeu ----------

    static readonly (string Fragment, string Label)[] KnownOverlays =
    {
        ("discordhook",           "Discord"),
        ("rtsshooks",             "RivaTuner / MSI Afterburner"),
        ("gameoverlayrenderer",   "Superposition Steam"),
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
        ("NVIDIA Share",    "Superposition NVIDIA"),
        ("NVIDIA Overlay",  "Superposition NVIDIA"),
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
                return new CheckResult("overlays", "Surcouches", Severity.Ok,
                    "Aucune accrochée au jeu",
                    $"{modules.Count} modules chargés dans CS2, aucune surcouche connue parmi eux.");

            return new CheckResult("overlays", "Surcouches", hooked.Count >= 3 ? Severity.Probleme : Severity.Warn,
                string.Join(", ", hooked),
                $"{hooked.Count} surcouche(s) accrochée(s) au processus de CS2. Chacune coûte des images et peut provoquer des saccades.",
                "Désactive les superpositions en jeu dans les applications concernées. Celle de Discord se coupe dans Paramètres → Superposition de jeu.");
        }

        // Sinon on ne peut pas interroger le jeu : on regarde les surcouches elles-mêmes.
        if (apps.Count == 0)
            return new CheckResult("overlays", "Surcouches", Severity.Ok,
                "Aucune en cours d'exécution",
                "Aucune application connue pour s'accrocher aux jeux ne tourne : ni Discord, ni RivaTuner, "
                + "ni Overwolf, ni iCUE, ni Razer Synapse.");

        string why = access == SystemProfile.ModuleAccess.Denied
            ? "Impossible de vérifier lesquelles sont réellement accrochées à CS2 : la lecture de ses modules est bloquée par "
              + (sys.AntiCheats.Count > 0 ? string.Join(" et ", sys.AntiCheats) : "ton anticheat")
            : "CS2 n'étant pas lancé, impossible de savoir lesquelles s'y accrocheront";

        return new CheckResult("overlays", "Surcouches", apps.Count >= 3 ? Severity.Warn : Severity.Info,
            string.Join(", ", apps),
            $"{apps.Count} application(s) capables de s'accrocher au jeu tournent en ce moment. {why}.",
            "Chacune coûte des images quand sa superposition est active. Coupe celles dont tu n'as pas besoin en jeu : "
            + "Discord → Paramètres → Superposition de jeu.");
    }
}

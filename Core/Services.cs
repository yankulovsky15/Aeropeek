using System.Diagnostics;
using System.Management;
using System.ServiceProcess;

namespace Aeropeek.Core;

/// <summary>
/// Ce que suspendre le service peut réellement changer pendant une partie.
/// </summary>
public enum ServiceImpact
{
    /// <summary>Peut se réveiller et prendre du disque ou du réseau en pleine partie.</summary>
    Reel,
    /// <summary>Au repos de bout en bout. Le suspendre ne rapporte rien.</summary>
    Nul
}

public sealed record ServiceCandidate(
    string Name,
    string Label,
    string Note,
    ServiceImpact Impact,
    bool Running,
    bool AutomaticStart,
    int ProcessId,
    double CpuPercent,
    double IoKoPerSec)
{
    /// <summary>Vrai si le service a montré une activité réelle pendant l'échantillonnage.</summary>
    public bool Active => CpuPercent >= 0.5 || IoKoPerSec >= 200;
}

public static class ServiceOps
{
    /// <summary>
    /// Jamais touchés, quelle que soit la provenance de la demande.
    /// Couper l'un d'eux casse le son, le réseau, la sécurité ou le démarrage.
    /// </summary>
    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "RpcEptMapper", "BFE", "mpssvc", "AudioSrv", "Audiosrv",
        "AudioEndpointBuilder", "CryptSvc", "EventLog", "Power", "Themes", "ProfSvc",
        "WinDefend", "SecurityHealthService", "wscsvc", "LSM", "gpsvc", "SamSs",
        "NVDisplay.ContainerLocalSystem", "nvagent", "Steam Client Service", "SteamService",
        "FACEIT", "vgk", "vgc", "EasyAntiCheat", "BEDaisy", "Schedule", "PlugPlay", "DPS"
    };

    /// <summary>
    /// Les seuls services que le Mode Match peut suspendre. Liste blanche stricte :
    /// un service absent d'ici n'est jamais proposé, même s'il consomme.
    /// </summary>
    /// <remarks>
    /// Deux absences volontaires. <c>XboxGipSvc</c> porte l'entrée des manettes :
    /// le suspendre couperait la manette en pleine partie. Tout ce qui touche au
    /// Bluetooth, à l'audio ou au réseau reste dehors pour la même raison — un
    /// gain nul ne justifie jamais un risque non nul.
    /// </remarks>
    public static readonly (string Name, string Label, string Note, ServiceImpact Impact)[] Candidates =
    {
        // ---- peuvent réellement interrompre une partie ----

        ("DoSvc",     "Partage des mises à jour Windows",
                      "Ton PC envoie des mises à jour à d'autres PC. C'est ce qui pèse le plus sur ton ping.",
                      ServiceImpact.Reel),
        ("WSearch",   "Recherche Windows",
                      "Indexe tes fichiers en arrière-plan, par à-coups imprévisibles.",
                      ServiceImpact.Reel),
        ("wuauserv",  "Windows Update",
                      "Recherche et télécharge les mises à jour. Redémarre seul : suspension temporaire uniquement.",
                      ServiceImpact.Reel),
        ("BITS",      "Transfert en arrière-plan",
                      "Télécharge les mises à jour. Dépendance de Windows Update.",
                      ServiceImpact.Reel),
        ("SysMain",   "SuperFetch",
                      "Précharge tes applications. Peu utile avec un SSD rapide et beaucoup de mémoire.",
                      ServiceImpact.Reel),
        ("DiagTrack", "Télémétrie Windows",
                      "Envoie des données d'usage à Microsoft.",
                      ServiceImpact.Reel),
        ("InstallService", "Installation du Microsoft Store",
                      "Peut télécharger et installer une application en pleine partie, sans rien demander.",
                      ServiceImpact.Reel),
        ("ClickToRunSvc", "Mise à jour d'Office",
                      "Télécharge des paquets de plusieurs centaines de mégaoctets sans prévenir.",
                      ServiceImpact.Reel),
        ("edgeupdate", "Mise à jour d'Edge",
                      "Se réveille pour vérifier et télécharger une nouvelle version du navigateur.",
                      ServiceImpact.Reel),
        ("CDPSvc",    "Plateforme des appareils connectés",
                      "Dialogue en permanence avec les autres appareils de ton compte Microsoft.",
                      ServiceImpact.Reel),
        ("WpnService", "Notifications Windows",
                      "Maintient une connexion ouverte pour les notifications. Tu n'en recevras plus pendant la partie.",
                      ServiceImpact.Reel),
        ("PcaSvc",    "Assistant Compatibilité des programmes",
                      "Écrit dans une base de données à chaque lancement de programme.",
                      ServiceImpact.Reel),

        // ---- au repos : listés pour être complet, sans rien promettre ----

        ("WerSvc",    "Rapport d'erreurs Windows",
                      "Collecte les plantages.",
                      ServiceImpact.Nul),
        ("Spooler",   "Spouleur d'impression",
                      "Nécessaire uniquement si tu imprimes.",
                      ServiceImpact.Nul),
        ("PrintNotify", "Notifications d'impression",
                      "Messages de l'imprimante. Dépend du spouleur.",
                      ServiceImpact.Nul),
        ("MapsBroker", "Cartes téléchargées",
                      "Sert l'application Cartes de Windows.",
                      ServiceImpact.Nul),
        ("lfsvc",     "Service de géolocalisation",
                      "Fournit ta position aux applications qui la demandent.",
                      ServiceImpact.Nul),
        ("TrkWks",    "Suivi de liens distribués",
                      "Répare les raccourcis vers des fichiers déplacés.",
                      ServiceImpact.Nul),
        ("DusmSvc",   "Utilisation des données",
                      "Compte les octets consommés par connexion.",
                      ServiceImpact.Nul),
        ("XblAuthManager", "Xbox Live — authentification",
                      "Sert aux jeux Xbox. Sans effet sur CS2.",
                      ServiceImpact.Nul),
        ("XblGameSave", "Xbox Live — sauvegardes",
                      "Synchronise les sauvegardes des jeux Xbox.",
                      ServiceImpact.Nul),
        ("XboxNetApiSvc", "Xbox Live — réseau",
                      "Services réseau des jeux Xbox.",
                      ServiceImpact.Nul)
    };

    public static bool IsAllowed(string name) => !Protected.Contains(name);

    // ---------- lecture ----------

    static ServiceController? Find(string name)
    {
        try
        {
            var sc = new ServiceController(name);
            _ = sc.Status;              // lève si le service n'existe pas
            return sc;
        }
        catch { return null; }
    }

    static Dictionary<string, int> ServicePids()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Service WHERE State='Running'");
            foreach (ManagementObject mo in s.Get())
            {
                var n = mo["Name"] as string;
                if (n != null && mo["ProcessId"] is { } pid)
                    map[n] = Convert.ToInt32(pid);
            }
        }
        catch { }
        return map;
    }

    /// <summary>
    /// Liste les candidats et mesure leur activité réelle sur quelques secondes.
    /// Un service au repos ne consomme rien : c'est ce que la mesure doit montrer.
    /// </summary>
    public static Task<List<ServiceCandidate>> SampleAsync(int seconds = 5, CancellationToken ct = default) =>
        Task.Run(async () =>
    {
        // Tout ce qui suit est bloquant : ServicePids() interroge WMI, et
        // ReadCounters() le refait une fois PAR service. Marquer la méthode
        // async ne suffisait pas — seul le Task.Delay rendait la main, le reste
        // s'exécutait sur le fil qui appelait, c'est-à-dire celui de l'interface.
        // D'où le gel de plusieurs secondes au lancement.
        var pids = ServicePids();
        var t0 = new Dictionary<string, (TimeSpan Cpu, long Io)>();

        foreach (var (name, _, _, _) in Candidates)
            if (pids.TryGetValue(name, out var pid))
                t0[name] = ReadCounters(pid);

        await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);

        var list = new List<ServiceCandidate>();
        foreach (var (name, label, note, impact) in Candidates)
        {
            using var sc = Find(name);
            if (sc == null) continue;

            bool running = sc.Status == ServiceControllerStatus.Running;
            bool auto = sc.StartType == ServiceStartMode.Automatic;
            pids.TryGetValue(name, out var pid);

            double cpu = 0, io = 0;
            if (running && pid != 0 && t0.TryGetValue(name, out var start))
            {
                var end = ReadCounters(pid);
                cpu = (end.Cpu - start.Cpu).TotalSeconds / seconds / Environment.ProcessorCount * 100.0;
                io = Math.Max(0, end.Io - start.Io) / 1024.0 / seconds;
            }

            list.Add(new ServiceCandidate(name, label, note, impact, running, auto, pid,
                Math.Round(Math.Max(0, cpu), 2), Math.Round(io, 1)));
        }

        return list.OrderByDescending(c => c.Active)
                   .ThenByDescending(c => c.IoKoPerSec + c.CpuPercent * 100)
                   .ToList();
    }, ct);

    static (TimeSpan Cpu, long Io) ReadCounters(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            long io = 0;
            try
            {
                using var s = new ManagementObjectSearcher(
                    $"SELECT ReadTransferCount, WriteTransferCount FROM Win32_PerfRawData_PerfProc_Process WHERE IDProcess={pid}");
                foreach (ManagementObject mo in s.Get())
                {
                    if (mo["ReadTransferCount"] is { } r) io += Convert.ToInt64(r);
                    if (mo["WriteTransferCount"] is { } w) io += Convert.ToInt64(w);
                }
            }
            catch { }
            return (p.TotalProcessorTime, io);
        }
        catch { return (TimeSpan.Zero, 0); }
    }

    // ---------- action ----------

    /// <summary>Arrête un service et renvoie de quoi le relancer à l'identique.</summary>
    public static OpRecord? Stop(string name, string description)
    {
        if (!IsAllowed(name)) throw new InvalidOperationException($"Service protégé : {name}");

        using var sc = Find(name);
        if (sc == null || sc.Status != ServiceControllerStatus.Running) return null;

        sc.Stop();
        try { sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10)); } catch { }

        return new OpRecord
        {
            Kind = "service",
            TweakId = "mode-match",
            Description = description,
            ServiceName = name,
            WasRunning = true
        };
    }

    public static void Revert(OpRecord rec)
    {
        if (rec.Kind != "service" || !rec.WasRunning) return;
        if (!IsAllowed(rec.ServiceName)) return;

        using var sc = Find(rec.ServiceName);
        if (sc == null || sc.Status == ServiceControllerStatus.Running) return;

        try
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
        }
        catch { /* certains services refusent un démarrage manuel : Windows les relancera */ }
    }
}

/// <summary>
/// Suspend les services le temps d'une partie, puis les relance.
/// Rien n'est écrit de façon permanente : le type de démarrage n'est jamais modifié.
/// </summary>
/// <summary>
/// Le Mode Match : priorité du jeu, fermeture des applications gourmandes,
/// suspension des services. Tout passe par le journal, donc tout se restaure —
/// y compris les applications, relancées depuis leur chemin d'origine.
/// </summary>
public sealed class MatchMode
{
    readonly Journal _journal;
    JournalSession? _session;

    public MatchMode(Journal journal) => _journal = journal;

    public bool Active => _session != null;
    public int SuspendedServices => _session?.Records.Count(r => r.Kind == "service") ?? 0;
    public int ClosedApps => _session?.Records.Count(r => r.Kind == "app-closed") ?? 0;

    public sealed record Plan(
        IEnumerable<string> Services,
        IEnumerable<(string Process, string Label)> Apps,
        bool RaisePriority,
        string GameProcess = "cs2");

    /// <summary>
    /// Applique le plan. Chaque service arrêté et chaque application fermée
    /// demande une attente : plusieurs secondes au total. La méthode est donc
    /// faite pour tourner hors du fil d'interface, et rend compte au fur et à
    /// mesure de ce qu'elle est en train de faire.
    /// </summary>
    public (int Services, int Apps, bool Priority) Start(Plan plan, IProgress<string>? progress = null)
    {
        if (Active) return (0, 0, false);
        _session = _journal.Begin("Mode Match");

        int services = 0, apps = 0;
        bool priority = false;

        // 1. priorité : d'abord, elle profite au jeu pendant le reste des opérations
        if (plan.RaisePriority)
        {
            progress?.Report("Priorité du jeu…");
            try
            {
                var rec = AppOps.SetGamePriority(plan.GameProcess, System.Diagnostics.ProcessPriorityClass.High);
                if (rec != null) { _journal.Record(rec); priority = true; }
            }
            catch { }
        }

        // 2. applications
        foreach (var (proc, label) in plan.Apps)
        {
            progress?.Report($"Fermeture de {label}…");
            try
            {
                var rec = AppOps.Close(proc, label);
                if (rec != null) { _journal.Record(rec); apps++; }
            }
            catch { }
        }

        // 3. services
        foreach (var name in plan.Services)
        {
            try
            {
                var meta = ServiceOps.Candidates.FirstOrDefault(
                    c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                string label = string.IsNullOrEmpty(meta.Label) ? name : meta.Label;

                progress?.Report($"Suspension de {label}…");
                var rec = ServiceOps.Stop(name, $"Service suspendu : {label}");
                if (rec != null) { _journal.Record(rec); services++; }
            }
            catch { }
        }

        if (services + apps == 0 && !priority) { _journal.Close(); _session = null; }
        return (services, apps, priority);
    }

    public int Stop(IProgress<string>? progress = null)
    {
        if (_session == null) return 0;
        progress?.Report("Restauration…");
        int n = _journal.RevertSession(_session, progress);
        _journal.Close();
        _session = null;
        return n;
    }
}

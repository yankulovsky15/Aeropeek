using Microsoft.Win32;
using Windows.Management.Deployment;

namespace Aeropeek.Core;

public sealed record StoreApp(
    string FamilyName,
    string FullName,
    string Label,
    string Category,
    string Note,
    bool TouchesGame,
    DateTimeOffset Installed);

/// <summary>
/// Applications préinstallées par Windows, et les clés qui les réinstallent.
/// <para>
/// Une chose doit être dite avant tout le reste : supprimer ces applications ne
/// fait pas gagner d'images. Elles ne tournent pas tant qu'on ne les ouvre pas.
/// Ce qui compte vraiment tient en deux points — la barre de jeu Xbox, qui
/// s'accroche au processus du jeu, et les clés de réinstallation automatique,
/// qui remettent des applications que l'utilisateur a déjà retirées.
/// </para>
/// </summary>
public static class Bloatware
{
    /// <summary>
    /// Jamais proposées à la suppression, quoi qu'il arrive. Retirer l'une d'elles
    /// casse Windows, le Microsoft Store, l'ouverture de session ou le panneau
    /// NVIDIA — des dégâts que l'utilisateur ne saurait pas réparer.
    /// </summary>
    static readonly string[] Untouchable =
    {
        "Microsoft.WindowsStore",
        "Microsoft.StorePurchaseApp",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.VCLibs",
        "Microsoft.NET.Native",
        "Microsoft.UI.Xaml",
        "Microsoft.WindowsAppRuntime",
        "Microsoft.SecHealthUI",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.Windows.ContentDeliveryManager",
        "Microsoft.Windows.Search",
        "Microsoft.Windows.Client",
        "Microsoft.Windows.PeopleExperienceHost",
        "Microsoft.Windows.NarratorQuickStart",
        "Microsoft.Windows.SecureAssessmentBrowser",
        "Microsoft.Windows.OOBENetworkConnectionFlow",
        "Microsoft.Windows.XGpuEjectDialog",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
        "Microsoft.AsyncTextService",
        "Microsoft.CredDialogHost",
        "Microsoft.ECApp",
        "Microsoft.LockApp",
        "Microsoft.Win32WebViewHost",
        "Microsoft.XboxIdentityProvider",     // certains jeux refusent de se lancer sans
        "Microsoft.MicrosoftEdge",
        "Microsoft.WebpImageExtension",
        "Microsoft.HEIFImageExtension",
        "Microsoft.VP9VideoExtensions",
        "Microsoft.AV1VideoExtension",
        "NVIDIACorp.NVIDIAControlPanel",      // le panneau du pilote, version Store
        "AdvancedMicroDevicesInc",
        "RealtekSemiconductorCorp",
        "IntelCorporation"
    };

    /// <summary>
    /// Applications proposées, avec ce que l'on perd en les retirant. Le champ
    /// « touche le jeu » distingue le seul cas où la suppression change quelque
    /// chose aux performances du reste, qui n'est que du rangement.
    /// </summary>
    static readonly (string Prefix, string Label, string Category, string Note, bool Game)[] Catalogue =
    {
        ("Microsoft.XboxGamingOverlay",       "Barre de jeu Xbox", "Jeu",
         "S'accroche au processus du jeu pour la superposition Win+G et l'enregistrement. C'est la seule de cette liste qui coûte des images.", true),
        ("Microsoft.XboxGameOverlay",         "Superposition Xbox", "Jeu",
         "Composant de la barre de jeu. Inutile sans elle.", true),
        ("Microsoft.XboxSpeechToTextOverlay", "Sous-titres Xbox", "Jeu",
         "Transcription vocale de la barre de jeu.", false),
        ("Microsoft.Xbox.TCUI",               "Interface Xbox Live", "Jeu",
         "Fenêtres d'invitation et de profil Xbox Live. Quelques jeux du Store s'en servent.", false),
        ("Microsoft.GamingApp",               "Application Xbox", "Jeu",
         "Nécessaire pour installer et lancer les jeux du Game Pass. À garder si tu y es abonné.", false),

        ("Microsoft.549981C3F5F10",           "Cortana", "Assistant",
         "L'assistant vocal, abandonné par Microsoft.", false),
        ("Microsoft.Copilot",                 "Copilot", "Assistant",
         "L'assistant Copilot de Windows.", false),
        ("Microsoft.Windows.Ai.Copilot",      "Copilot (composant système)", "Assistant",
         "Fournisseur Copilot intégré au système.", false),

        ("Microsoft.BingNews",                "Actualités", "Bing",
         "L'application Actualités et le widget associé.", false),
        ("Microsoft.BingWeather",             "Météo", "Bing",
         "L'application Météo et le widget associé.", false),
        ("Microsoft.BingSearch",              "Recherche web Bing", "Bing",
         "Résultats web dans le menu Démarrer.", false),
        ("Microsoft.BingFinance",             "Finance", "Bing", "Application Finance.", false),
        ("Microsoft.BingSports",              "Sport", "Bing", "Application Sport.", false),

        ("Microsoft.MicrosoftSolitaireCollection", "Solitaire", "Divertissement",
         "La collection Solitaire, avec ses publicités.", false),
        ("Microsoft.ZuneMusic",               "Lecteur multimédia", "Divertissement",
         "Le lecteur de musique de Windows. Retire l'application par défaut pour les fichiers audio.", false),
        ("Microsoft.ZuneVideo",               "Films et TV", "Divertissement",
         "Le lecteur vidéo de Windows. Retire l'application par défaut pour les fichiers vidéo.", false),
        ("Clipchamp.Clipchamp",               "Clipchamp", "Divertissement",
         "L'éditeur vidéo préinstallé.", false),
        ("SpotifyAB.SpotifyMusic",            "Spotify (préinstallé)", "Divertissement",
         "La version du Store installée automatiquement. Sans effet sur une installation faite par toi.", false),

        ("Microsoft.MicrosoftOfficeHub",      "Microsoft 365 (raccourci)", "Bureautique",
         "Le raccourci publicitaire vers Office, pas Office lui-même.", false),
        ("Microsoft.Office.OneNote",          "OneNote (version Store)", "Bureautique",
         "La version Store de OneNote.", false),
        ("Microsoft.OutlookForWindows",       "Nouvel Outlook", "Bureautique",
         "La nouvelle application Courrier de Windows.", false),
        ("Microsoft.Todos",                   "To Do", "Bureautique", "Les listes de tâches Microsoft.", false),
        ("MicrosoftTeams",                    "Teams (personnel)", "Bureautique",
         "La version grand public de Teams, préinstallée. Sans effet sur Teams professionnel.", false),
        ("MSTeams",                           "Teams", "Bureautique",
         "La nouvelle application Teams.", false),
        ("Microsoft.SkypeApp",                "Skype", "Bureautique", "Skype préinstallé.", false),
        ("Microsoft.PowerAutomateDesktop",    "Power Automate", "Bureautique",
         "L'outil d'automatisation de Microsoft.", false),

        ("Microsoft.People",                  "Contacts", "Système",
         "Le carnet d'adresses de Windows.", false),
        ("Microsoft.YourPhone",               "Mobile connecté", "Système",
         "La liaison avec le téléphone. À garder si tu l'utilises.", false),
        ("Microsoft.WindowsMaps",             "Cartes", "Système", "L'application Cartes.", false),
        ("Microsoft.WindowsFeedbackHub",      "Concentrateur de commentaires", "Système",
         "L'outil de retour à Microsoft.", false),
        ("Microsoft.GetHelp",                 "Obtenir de l'aide", "Système",
         "L'assistance Microsoft.", false),
        ("Microsoft.Getstarted",              "Conseils", "Système",
         "Les conseils d'utilisation de Windows.", false),
        ("Microsoft.MixedReality.Portal",     "Réalité mixte", "Système",
         "Le portail de réalité mixte, abandonné.", false),
        ("Microsoft.Wallet",                  "Portefeuille", "Système", "Le portefeuille Microsoft.", false),
        ("Microsoft.WindowsAlarms",           "Horloge", "Système",
         "Alarmes, minuteur et chronomètre.", false),
        ("Microsoft.WindowsSoundRecorder",    "Enregistreur vocal", "Système",
         "L'enregistreur audio de Windows.", false),
        ("Microsoft.QuickAssist",             "Assistance rapide", "Système",
         "La prise en main à distance de Microsoft.", false),
        ("Microsoft.Windows.DevHome",         "Dev Home", "Système",
         "Le tableau de bord destiné aux développeurs.", false)
    };

    public static bool Available
    {
        get { try { _ = new PackageManager(); return true; } catch { return false; } }
    }

    static bool IsUntouchable(string family) =>
        Untouchable.Any(u => family.StartsWith(u, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applications du catalogue réellement installées pour l'utilisateur courant.
    /// Rien n'est proposé qui ne soit présent : une liste de cases à cocher sans
    /// rapport avec la machine ne renseigne sur rien.
    /// </summary>
    public static List<StoreApp> Installed()
    {
        var list = new List<StoreApp>();

        PackageManager pm;
        try { pm = new PackageManager(); } catch { return list; }

        IEnumerable<Windows.ApplicationModel.Package> packages;
        try { packages = pm.FindPackagesForUser(""); } catch { return list; }

        foreach (var p in packages)
        {
            string family;
            string full;
            try
            {
                family = p.Id.FamilyName;
                full = p.Id.FullName;
            }
            catch { continue; }

            if (IsUntouchable(family)) continue;

            // Les paquets système signés par Microsoft comme « framework » sont des
            // dépendances d'autres applications, jamais des applications en soi.
            try { if (p.IsFramework || p.IsResourcePackage) continue; } catch { }

            var match = Catalogue.FirstOrDefault(c =>
                family.StartsWith(c.Prefix, StringComparison.OrdinalIgnoreCase));
            if (match.Prefix == null) continue;

            DateTimeOffset installed = default;
            try { installed = p.InstalledDate; } catch { }

            list.Add(new StoreApp(family, full, match.Label, match.Category, match.Note, match.Game, installed));
        }

        return list
            .GroupBy(a => a.FamilyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => a.TouchesGame)
            .ThenBy(a => a.Category)
            .ThenBy(a => a.Label)
            .ToList();
    }

    /// <summary>
    /// Supprime une application pour l'utilisateur courant, puis retire son
    /// approvisionnement afin qu'elle ne revienne pas sur un nouveau compte.
    /// <para>
    /// Cette opération n'est pas annulable par Aeropeek : le journal en garde la
    /// trace, mais la réinstallation passe par le Microsoft Store. C'est dit à
    /// l'utilisateur avant, pas après.
    /// </para>
    /// </summary>
    public static OpRecord Remove(StoreApp app)
    {
        if (IsUntouchable(app.FamilyName))
            throw new InvalidOperationException($"Application protégée : {app.Label}");

        var pm = new PackageManager();

        var result = pm.RemovePackageAsync(app.FullName, RemovalOptions.None)
                       .AsTask().GetAwaiter().GetResult();

        if (result.ExtendedErrorCode != null)
            throw new InvalidOperationException($"{app.Label} : {result.ErrorText}");

        // Le déprovisionnement échoue sans droits d'administrateur ou quand
        // l'application n'était pas approvisionnée. Ni l'un ni l'autre n'annule
        // la suppression qui vient d'aboutir.
        try
        {
            pm.DeprovisionPackageForAllUsersAsync(app.FamilyName).AsTask().GetAwaiter().GetResult();
        }
        catch { }

        return new OpRecord
        {
            Kind = "appx-removed",
            TweakId = "applications",
            Description = "Application supprimée : " + app.Label,
            ServiceName = app.FamilyName,
            PreviousValue = app.FullName
        };
    }

    // ---------- réinstallation automatique ----------

    const string CdmKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    /// <summary>
    /// Les valeurs qui autorisent Windows à installer des applications de son
    /// propre chef — celles qui réapparaissent dans le menu Démarrer après une
    /// mise à jour, alors qu'on les avait supprimées.
    /// </summary>
    public static readonly (string Value, string Label)[] ReinstallValues =
    {
        ("SilentInstalledAppsEnabled",         "Installation silencieuse d'applications suggérées"),
        ("PreInstalledAppsEnabled",            "Applications préinstallées au premier démarrage"),
        ("OemPreInstalledAppsEnabled",         "Applications préinstallées par le constructeur"),
        ("ContentDeliveryAllowed",             "Contenu proposé par Microsoft"),
        ("SubscribedContent-338388Enabled",    "Suggestions dans le menu Démarrer"),
        ("SubscribedContent-338389Enabled",    "Suggestions de conseils et astuces"),
        ("SubscribedContent-353698Enabled",    "Suggestions dans la chronologie"),
        ("SubscribedContent-310093Enabled",    "Suggestions à l'ouverture de session"),
        ("SystemPaneSuggestionsEnabled",       "Suggestions dans le volet système")
    };

    /// <summary>Nombre de ces valeurs encore actives.</summary>
    public static int ReinstallActive()
    {
        int n = 0;
        foreach (var (value, _) in ReinstallValues)
        {
            var v = RegistryOps.ReadValue("HKCU", CdmKey, value);
            // Absente vaut « activé » : Windows considère ces réglages actifs par défaut.
            if (v == null || Convert.ToInt64(v) != 0) n++;
        }
        return n;
    }

    /// <summary>Coupe la réinstallation automatique. Chaque écriture est annulable.</summary>
    public static List<OpRecord> StopReinstall()
    {
        var records = new List<OpRecord>();
        foreach (var (value, label) in ReinstallValues)
        {
            var current = RegistryOps.ReadValue("HKCU", CdmKey, value);
            if (current != null && Convert.ToInt64(current) == 0) continue;

            records.Add(RegistryOps.Apply("HKCU", CdmKey, value, 0,
                RegistryValueKind.DWord, "applications", label));
        }
        return records;
    }
}

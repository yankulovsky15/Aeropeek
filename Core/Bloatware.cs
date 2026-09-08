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
        ("Microsoft.XboxGamingOverlay",       "Xbox Game Bar", "Gaming",
         "Hooks into the game process for the Win+G overlay and recording. The only one on this list that costs frames.", true),
        ("Microsoft.XboxGameOverlay",         "Xbox overlay", "Gaming",
         "A Game Bar component. Useless without it.", true),
        ("Microsoft.XboxSpeechToTextOverlay", "Xbox speech-to-text", "Gaming",
         "Game Bar voice transcription.", false),
        ("Microsoft.Xbox.TCUI",               "Xbox Live interface", "Gaming",
         "Xbox Live invite and profile windows. A few Store games use them.", false),
        ("Microsoft.GamingApp",               "Xbox app", "Gaming",
         "Required to install and launch Game Pass titles. Keep it if you subscribe.", false),

        ("Microsoft.549981C3F5F10",           "Cortana", "Assistant",
         "The voice assistant, abandoned by Microsoft.", false),
        ("Microsoft.Copilot",                 "Copilot", "Assistant",
         "The Windows Copilot assistant.", false),
        ("Microsoft.Windows.Ai.Copilot",      "Copilot (system component)", "Assistant",
         "The Copilot provider built into the system.", false),

        ("Microsoft.BingNews",                "News", "Bing",
         "The News app and its widget.", false),
        ("Microsoft.BingWeather",             "Weather", "Bing",
         "The Weather app and its widget.", false),
        ("Microsoft.BingSearch",              "Bing web search", "Bing",
         "Web results in the Start menu.", false),
        ("Microsoft.BingFinance",             "Finance", "Bing", "The Finance app.", false),
        ("Microsoft.BingSports",              "Sports", "Bing", "The Sports app.", false),

        ("Microsoft.MicrosoftSolitaireCollection", "Solitaire", "Entertainment",
         "The Solitaire collection, ads included.", false),
        ("Microsoft.ZuneMusic",               "Media Player", "Entertainment",
         "Windows' music player. Removes the default app for audio files.", false),
        ("Microsoft.ZuneVideo",               "Movies & TV", "Entertainment",
         "Windows' video player. Removes the default app for video files.", false),
        ("Clipchamp.Clipchamp",               "Clipchamp", "Entertainment",
         "The preinstalled video editor.", false),
        ("SpotifyAB.SpotifyMusic",            "Spotify (preinstalled)", "Entertainment",
         "The Store version installed automatically. No effect on one you installed yourself.", false),

        ("Microsoft.MicrosoftOfficeHub",      "Microsoft 365 (shortcut)", "Office",
         "The promotional shortcut to Office, not Office itself.", false),
        ("Microsoft.Office.OneNote",          "OneNote (Store version)", "Office",
         "The Store version of OneNote.", false),
        ("Microsoft.OutlookForWindows",       "New Outlook", "Office",
         "The new Windows Mail app.", false),
        ("Microsoft.Todos",                   "To Do", "Office", "Microsoft's task lists.", false),
        ("MicrosoftTeams",                    "Teams (personal)", "Office",
         "The consumer version of Teams, preinstalled. No effect on Teams for work.", false),
        ("MSTeams",                           "Teams", "Office",
         "The new Teams app.", false),
        ("Microsoft.SkypeApp",                "Skype", "Office", "Preinstalled Skype.", false),
        ("Microsoft.PowerAutomateDesktop",    "Power Automate", "Office",
         "Microsoft's automation tool.", false),

        ("Microsoft.People",                  "Contacts", "System",
         "The Windows address book.", false),
        ("Microsoft.YourPhone",               "Phone Link", "System",
         "The link to your phone. Keep it if you use it.", false),
        ("Microsoft.WindowsMaps",             "Maps", "System", "The Maps app.", false),
        ("Microsoft.WindowsFeedbackHub",      "Feedback Hub", "System",
         "The feedback tool for Microsoft.", false),
        ("Microsoft.GetHelp",                 "Get Help", "System",
         "Microsoft's support app.", false),
        ("Microsoft.Getstarted",              "Tips", "System",
         "The Windows tips app.", false),
        ("Microsoft.MixedReality.Portal",     "Mixed Reality", "System",
         "The Mixed Reality portal, abandoned.", false),
        ("Microsoft.Wallet",                  "Wallet", "System", "The Microsoft wallet.", false),
        ("Microsoft.WindowsAlarms",           "Clock", "System",
         "Alarms, timer and stopwatch.", false),
        ("Microsoft.WindowsSoundRecorder",    "Sound Recorder", "System",
         "The Windows sound recorder.", false),
        ("Microsoft.QuickAssist",             "Quick Assist", "System",
         "Microsoft's remote assistance tool.", false),
        ("Microsoft.Windows.DevHome",         "Dev Home", "System",
         "The dashboard aimed at developers.", false)
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
            throw new InvalidOperationException($"Protected application: {app.Label}");

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
            Description = "Application removed: " + app.Label,
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
        ("SilentInstalledAppsEnabled",         "Silent installation of suggested apps"),
        ("PreInstalledAppsEnabled",            "Apps preinstalled on first boot"),
        ("OemPreInstalledAppsEnabled",         "Apps preinstalled by the manufacturer"),
        ("ContentDeliveryAllowed",             "Content suggested by Microsoft"),
        ("SubscribedContent-338388Enabled",    "Suggestions in the Start menu"),
        ("SubscribedContent-338389Enabled",    "Tips and tricks suggestions"),
        ("SubscribedContent-353698Enabled",    "Suggestions in the timeline"),
        ("SubscribedContent-310093Enabled",    "Suggestions at sign-in"),
        ("SystemPaneSuggestionsEnabled",       "Suggestions in the settings pane")
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

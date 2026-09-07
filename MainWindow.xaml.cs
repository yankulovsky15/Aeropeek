using System.Windows.Input;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Aeropeek.Core;

namespace Aeropeek;

// ---------- objets d'affichage ----------

public sealed class CheckVm
{
    public string Title { get; init; } = "";
    public string Value { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Advice { get; init; } = "";
    public string Badge { get; init; } = "";
    public Brush BadgeFg { get; init; } = Brushes.Gray;
    public Brush BadgeBg { get; init; } = Brushes.Transparent;
    public string? ActionId { get; init; }
    public string ActionLabel { get; init; } = "";
    public Visibility AdviceVisibility => string.IsNullOrWhiteSpace(Advice) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ActionVisibility => string.IsNullOrWhiteSpace(ActionId) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class TweakVm
{
    public Tweak Model { get; init; } = new();
    public string Name => Model.Name;
    public string Explanation => Model.Explanation;
    public string Footnote { get; init; } = "";
    public string Badge { get; init; } = "";
    public Brush BadgeFg { get; init; } = Brushes.Gray;
    public Brush BadgeBg { get; init; } = Brushes.Transparent;
    public string ActionLabel { get; init; } = "Appliquer";
    public string StateLabel { get; init; } = "";
    public bool ActionEnabled { get; init; } = true;
    public bool IsOn { get; init; }
    public Verdict Verdict { get; init; }

    public bool CanMeasure { get; init; }
    public Visibility MeasureVisibility => CanMeasure ? Visibility.Visible : Visibility.Collapsed;

    public string Measured { get; init; } = "";
    public Brush MeasuredFg { get; init; } = Brushes.Gray;
    public Brush MeasuredBg { get; init; } = Brushes.Transparent;
    public Visibility MeasuredVisibility => Measured.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class BenchVm
{
    public string Label { get; init; } = "";
    public string When { get; init; } = "";
    public string Fps { get; init; } = "";
    public string Low1 { get; init; } = "";
    public string Low01 { get; init; } = "";
    public string MaxMs { get; init; } = "";
    public string Stutters { get; init; } = "";
    public string Footnote { get; init; } = "";
    public string Delta { get; init; } = "";
    public Brush DeltaFg { get; init; } = Brushes.Gray;
    public Visibility DeltaVisibility => string.IsNullOrWhiteSpace(Delta) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class ServiceVm
{
    public ServiceCandidate Model { get; init; } = null!;
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string Note { get; init; } = "";
    public string Activity { get; init; } = "";
    public Brush ActivityFg { get; init; } = Brushes.Gray;
    public string Impact { get; init; } = "";
    public Brush ImpactFg { get; init; } = Brushes.Gray;
    public Brush ImpactBd { get; init; } = Brushes.Transparent;
    public bool Selected { get; set; }
}

public sealed class AppVm
{
    public AppCandidate Model { get; init; } = null!;
    public string Process => Model.Process;
    public string Label => Model.Label;
    public string Category => Model.Category;
    public string Note { get; init; } = "";
    public string Memory { get; init; } = "";
    public string Cpu { get; init; } = "";
    public Brush ActivityFg { get; init; } = Brushes.Gray;
    public bool Selected { get; set; }
}

public sealed class CleanVm
{
    public CleanTarget Target { get; init; } = null!;
    public string Id => Target.Id;
    public string Title => Target.Title;
    public string Description { get; init; } = "";
    public string Size { get; init; } = "";
    public Brush SizeFg { get; init; } = Brushes.Gray;
    public string RiskLabel { get; init; } = "";
    public Brush RiskFg { get; init; } = Brushes.Gray;
    public Brush RiskBd { get; init; } = Brushes.Transparent;
    public Visibility RiskVisibility => RiskLabel.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public bool Available { get; init; }
    public bool Selected { get; set; }        // modifiable : la case à cocher écrit dedans
}

public sealed class PlanVm
{
    public PowerPlan Model { get; init; } = null!;
    public string Name => Model.Name;
    public bool Active => Model.Active;
    public Visibility ActiveVisibility => Active ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UseVisibility => Active ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DeleteVisibility => Active ? Visibility.Collapsed : Visibility.Visible;
    public Brush DotFill { get; init; } = Brushes.Transparent;
    public Brush NameFill { get; init; } = Brushes.Gray;
}

public sealed class RestoreVm
{
    public RestorePoint Model { get; init; } = null!;
    public string Description => Model.Description.Length > 0 ? Model.Description : "(sans nom)";
    public string Type => Model.TypeLabel;
    public string When => Model.Created == DateTime.MinValue
        ? $"numéro {Model.Sequence}"
        : $"{Model.Created:dddd d MMMM yyyy à HH'h'mm} · numéro {Model.Sequence}";
}

public sealed class JournalVm
{
    public string Title { get; init; } = "";
    public string When { get; init; } = "";
    public string Path { get; init; } = "";
    public string Change { get; init; } = "";

    /// <summary>Les écritures que cette ligne représente, à annuler ensemble.</summary>
    public List<string> Ids { get; init; } = new();

    public bool CanUndo { get; init; }
    public Visibility UndoVisibility => CanUndo ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoUndoVisibility => CanUndo ? Visibility.Collapsed : Visibility.Visible;
}

public partial class MainWindow : Window
{
    static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    /// <summary>Même teinte, moins présente. Sert à doser le poids d'une pastille.</summary>
    static Brush Dim(Brush b, double opacity) =>
        new SolidColorBrush(((SolidColorBrush)b).Color) { Opacity = opacity };

    static readonly Brush OkFg = B("#4FBF8B"), OkBg = B("#132A22");
    static readonly Brush WarnFg = B("#E0A63C"), WarnBg = B("#2B2317");
    static readonly Brush BadFg = B("#F2726A"), BadBg = B("#2C1A1A");
    static readonly Brush InfoFg = B("#6BA6F5"), InfoBg = B("#16223A");
    static readonly Brush MuteFg = B("#7C8FA4"), MuteBg = B("#161F2A");
    static readonly Brush AccFg = B("#2DD4BF"), AccBg = B("#0F2A29");

    SystemProfile _sys = null!;
    Journal _journal = null!;
    Catalogue _catalogue = null!;
    BenchHistory _bench = null!;
    AttributionStore _attributions = null!;
    List<CheckResult> _lastChecks = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += (_, _) =>
        {
            // Volontairement bloquant : si la fenêtre se ferme pendant qu'un Mode
            // Match est actif, les services doivent être relancés AVANT que le
            // processus disparaisse. Le curseur d'attente dit que ce n'est pas un gel.
            if (_match?.Active == true)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _match.Stop();
                Mouse.OverrideCursor = null;
            }
            _journal?.Close();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // La fenêtre n'a pas de cadre standard : Windows ne l'arrondit donc pas
        // de lui-même, il faut le demander une fois la poignée créée.
        WindowEffects.Apply(this);
    }

    // ---------- navigation repliable ----------

    /// <summary>Vrai quand le menu latéral n'affiche que les icônes.</summary>
    public static readonly DependencyProperty CompactNavProperty =
        DependencyProperty.Register(nameof(CompactNav), typeof(bool), typeof(MainWindow),
            new PropertyMetadata(false));

    public bool CompactNav
    {
        get => (bool)GetValue(CompactNavProperty);
        set => SetValue(CompactNavProperty, value);
    }

    const double NavWide = 238, NavNarrow = 66;

    void Burger_Click(object sender, RoutedEventArgs e)
    {
        CompactNav = !CompactNav;

        BrandPanel.Visibility = CompactNav ? Visibility.Collapsed : Visibility.Visible;
        SysPanel.Visibility = CompactNav ? Visibility.Collapsed : Visibility.Visible;
        BtnBurger.ToolTip = CompactNav ? "Afficher les libellés" : "Réduire le menu";

        var anim = new System.Windows.Media.Animation.DoubleAnimation(
            Sidebar.ActualWidth, CompactNav ? NavNarrow : NavWide, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        Sidebar.BeginAnimation(WidthProperty, anim);
    }

    // ---------- barre de titre intégrée ----------

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Avec WindowChrome, une fenêtre agrandie déborde de la zone de travail
        // de l'épaisseur de la bordure de redimensionnement : on la compense.
        RootGrid.Margin = WindowState == WindowState.Maximized
            ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + 4)
            : new Thickness(0);

        if (BtnMax.Content is System.Windows.Shapes.Path p)
        {
            p.Data = System.Windows.Media.Geometry.Parse(
                WindowState == WindowState.Maximized
                    ? "M 2,2 H 10 V 10 H 2 Z M 0,7 V 0 H 7"     // restaurer
                    : "M 0,0 H 10 V 10 H 0 Z");                  // agrandir
            BtnMax.ToolTip = WindowState == WindowState.Maximized ? "Restaurer" : "Agrandir";
        }
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Avant tout le reste : au premier lancement, l'accueil couvre le
        // chargement plutôt que de laisser un écran vide se remplir tout seul.
        if (!Settings.Current.WelcomeSeen) ShowWelcome(animate: false);

        DiagSummary.Text = "Lecture de la configuration…";
        BtnScan.IsEnabled = false;
        Veil(DiagVeil, true);

        // Ces quatre lectures restent ICI, et volontairement synchrones.
        // Ce sont quatre petits fichiers JSON : les passer sur un autre fil ne
        // gagnait rien de mesurable, mais cela ouvrait une fenêtre — courte, et
        // pourtant réelle — où l'interface répondait déjà alors que _journal,
        // _catalogue, _bench, _attributions et _match étaient encore nuls. Dix
        // gestionnaires de clic les touchent. Un clic dans cet intervalle levait
        // une NullReferenceException.
        // Les vrais blocages n'étaient jamais là : ils étaient dans WMI et dans
        // le parcours des processus, qui eux partent bien en arrière-plan.
        _journal = Journal.Load();
        _catalogue = Catalogue.Load();
        _bench = BenchHistory.Load();
        _attributions = AttributionStore.Load();
        _match = new MatchMode(_journal);

        // Loaded se déclenche AVANT la première image. Tout ce qu'on ferait de
        // long ici repousserait d'autant l'apparition de la fenêtre — c'est ce
        // qui donnait l'impression que l'application mettait du temps à s'ouvrir.
        // On rend donc la main au rendu, et on ne commence qu'ensuite.
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await StartupWork();
    }

    /// <summary>
    /// Remplit les huit écrans.
    /// <para>
    /// L'ordre a son importance, et il n'est pas celui de la lecture. Les
    /// mesures longues — quatre secondes pour les applications, cinq pour les
    /// services — ne dépendent de rien : on les lance TOUTES en premier, puis on
    /// remplit les écrans au fur et à mesure qu'elles rendent leur résultat.
    /// Enchaînées l'une après l'autre comme avant, elles mettaient une vingtaine
    /// de secondes à se terminer alors qu'elles passent l'essentiel de ce temps
    /// à ne rien faire qu'attendre.
    /// </para>
    /// </summary>
    async Task StartupWork()
    {
        RefreshBench();
        RefreshJournal();
        RefreshUtils();

        // Toutes lancées d'un coup, aucune attendue pour l'instant.
        var sysJob = Task.Run(SystemProfile.Capture);
        var appsJob = AppOps.SampleAsync(4);
        var servicesJob = ServiceOps.SampleAsync(5);
        var cleanJob = Cleanup.ScanAsync();
        var dnsJob = Task.Run(DnsOps.Adapters);

        // Le diagnostic est l'écran affiché : il passe devant.
        _sys = await sysJob;
        ShowSystemSummary();
        RecoverIfCrashed();

        await RunScan();
        RefreshTweaks();
        BtnScan.IsEnabled = true;

        await RefreshDns(dnsJob);
        await RunCleanScan(cleanJob);
        await RefreshApps(appsJob);
        await RefreshServices(servicesJob);
        StartGameWatcher();
    }

    void ShowSystemSummary()
    {
        SysCpu.Text = Trim(_sys.CpuName, 38);
        SysGpu.Text = _sys.GpuNames.Count > 0 ? Trim(_sys.GpuNames[0], 38) : "Carte graphique inconnue";
        SysOs.Text = $"{_sys.OsName} · {_sys.OsDisplayVersion} ({_sys.OsBuild})";
        SysAc.Text = _sys.AntiCheats.Count > 0
            ? "Anticheat : " + string.Join(", ", _sys.AntiCheats)
            : "Aucun anticheat détecté";
    }

    static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? "—" : (s.Length <= n ? s : s[..(n - 1)] + "…");

    void RecoverIfCrashed()
    {
        var open = _journal.UnclosedSessions().Where(s => s.Records.Count > 0).ToList();
        if (open.Count == 0) return;

        int n = open.Sum(s => s.Records.Count);
        var answer = MessageBox.Show(
            $"Aeropeek s'est fermé sans terminer proprement une session précédente.\n\n" +
            $"{n} modification(s) sont encore appliquées mais non confirmées.\n\n" +
            "Veux-tu les annuler et revenir à l'état d'origine ?",
            "Session interrompue", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
            foreach (var s in open) _journal.RevertSession(s);
        else
            foreach (var s in open) { s.Closed = DateTimeOffset.Now; }

        _journal.Save();
    }

    // ---------- Diagnostic ----------

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        BtnScan.IsEnabled = false;
        Veil(DiagVeil, true);
        _sys = await Task.Run(SystemProfile.Capture);
        ShowSystemSummary();
        await RunScan();
        RefreshTweaks();
        BtnScan.IsEnabled = true;
    }

    async Task RunScan()
    {
        DiagSummary.Text = "Analyse en cours…";
        Veil(DiagVeil, true);
        try
        {
            var sys = _sys;
            _lastChecks = await Task.Run(() => Diagnostics.RunAll(sys));

            ChecksList.ItemsSource = _lastChecks.Select(c =>
            {
                var (fg, bg, label) = c.Severity switch
                {
                    Severity.Probleme => (BadFg, BadBg, "À corriger"),
                    Severity.Warn => (WarnFg, WarnBg, "À vérifier"),
                    Severity.Ok => (OkFg, OkBg, "Correct"),
                    Severity.Info => (InfoFg, InfoBg, "Information"),
                    _ => (MuteFg, MuteBg, "Indéterminé")
                };
                return new CheckVm
                {
                    Title = c.Title, Value = c.Value, Detail = c.Detail, Advice = c.Advice,
                    Badge = label, BadgeFg = fg, BadgeBg = bg,
                    ActionId = c.ActionId, ActionLabel = c.ActionLabel ?? ""
                };
            }).ToList();

            int problems = _lastChecks.Count(c => c.Severity == Severity.Probleme);
            int warns = _lastChecks.Count(c => c.Severity == Severity.Warn);

            DiagSummary.Text = problems == 0 && warns == 0
                ? $"{_lastChecks.Count} vérifications, rien à corriger. Ta machine est bien réglée."
                : $"{_lastChecks.Count} vérifications · {problems} à corriger · {warns} à vérifier";
        }
        finally { Veil(DiagVeil, false); }
    }

    async void CheckAction_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;

        switch (id)
        {
            case "affinite-pcores":
            {
                int n = GameAffinity.Apply(_sys.Cpu.PerformanceMask);
                MessageBox.Show(n > 0
                    ? "CS2 est maintenant épinglé sur les cœurs de performance.\n\n" +
                      "L'affinité est propre au processus : elle disparaît quand tu fermes le jeu. " +
                      "Rien n'a été écrit sur ton système, il n'y a donc rien à annuler."
                    : "CS2 ne semble plus lancé.", "Aeropeek");
                break;
            }
            case "affinite-tous":
            {
                int n = GameAffinity.Apply(GameAffinity.AllCores(_sys.Cpu.LogicalCores));
                MessageBox.Show(n > 0
                    ? "CS2 peut de nouveau utiliser tous les cœurs."
                    : "CS2 ne semble plus lancé.", "Aeropeek");
                break;
            }
            case "confirmer-memoire":
            {
                uint mhz = _sys.Memory.Count > 0 ? _sys.Memory[0].ConfiguredMhz : 0;
                if (mhz == 0) return;
                Settings.Current.ConfirmedMemoryMhz = mhz;
                Settings.Current.Save();
                break;
            }
            case "power-panel":
            {
                var win = new PowerWindow(_journal) { Owner = this };
                win.ShowDialog();
                if (win.Changed) RefreshJournal();
                await RunScan();          // la carte du diagnostic reflète le nouveau plan
                return;
            }
            case "chercher-pilote":
            {
                BtnScan.IsEnabled = false;
                DiagSummary.Text = "Interrogation de NVIDIA…";
                string gpu = _sys.GpuNames.FirstOrDefault(g =>
                    g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) ?? _sys.GpuNames.FirstOrDefault() ?? "";
                await Drivers.CheckLatestAsync(gpu);
                await RunScan();
                BtnScan.IsEnabled = true;
                return;
            }

            case "telecharger-pilote":
                try
                {
                    string url = !string.IsNullOrEmpty(Drivers.Latest?.Url)
                        ? Drivers.Latest!.Url
                        : "https://www.nvidia.com/fr-fr/geforce/drivers/";
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;

            case "nvidia-performances":
                try
                {
                    _journal.Record(Nvidia.SetPowerMode(Nvidia.PowerModeMax));
                    MessageBox.Show(
                        "Gestion de l'alimentation passée à « Privilégier les performances maximales »." +
                        Environment.NewLine + Environment.NewLine +
                        "La valeur précédente est au journal. Relance l'analyse pour la voir appliquée.",
                        "Pilote NVIDIA");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;

            case "ouvrir-nvidia":
                if (!Nvidia.OpenControlPanel())
                    MessageBox.Show(
                        "Le panneau de configuration NVIDIA n'a pas été trouvé." + Environment.NewLine + Environment.NewLine +
                        "Il s'ouvre aussi par un clic droit sur le bureau, ou depuis le Microsoft Store " +
                        "s'il n'est pas installé.",
                        "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Information);
                return;

            case "ouvrir-isolation":
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "windowsdefender://coreisolation") { UseShellExecute = true });
                }
                catch
                {
                    MessageBox.Show("Ouvre Sécurité Windows → Sécurité des appareils → Isolation du noyau.", "Aeropeek");
                }
                return;   // pas de réanalyse : le changement demande un redémarrage
        }

        await RunScan();
    }

    // ---------- Réglages ----------

    void RefreshTweaks()
    {
        if (_sys == null) return;

        TweaksList.ItemsSource = _catalogue.Tweaks.Select(t =>
        {
            var app = t.Check(_sys);
            var (fg, bg, badge) = app.Verdict switch
            {
                Verdict.DejaApplique => (AccFg, AccBg, "Appliqué"),
                Verdict.NonPertinent => (MuteFg, MuteBg, "Sans objet"),
                Verdict.Bloque => (BadFg, BadBg, "Bloqué"),
                _ => t.Category switch
                {
                    "reseau" => (InfoFg, InfoBg, "Réseau"),
                    "confidentialite" => (MuteFg, MuteBg, "Confidentialité"),
                    _ => (WarnFg, WarnBg, "Performance")
                }
            };

            var note = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(t.Gain)) note.Append("Gain attendu : ").Append(t.Gain);
            if (!string.IsNullOrWhiteSpace(t.Consequence))
            {
                if (note.Length > 0) note.Append("  ·  ");
                note.Append("Ce que tu perds : ").Append(t.Consequence);
            }
            if (t.NeedsRestart)
            {
                if (note.Length > 0) note.Append("  ·  ");
                note.Append("Redémarrage nécessaire");
            }
            if (app.Verdict is Verdict.NonPertinent or Verdict.Bloque && !string.IsNullOrEmpty(app.Reason))
                note.Clear().Append(app.Reason);

            // Une mesure réelle prime sur le gain annoncé par le catalogue.
            var measured = _attributions.For(t.Id);
            string measuredText = "";
            Brush mFg = MuteFg, mBg = MuteBg;
            if (measured != null)
            {
                measuredText = measured.Verdict + $" · le {measured.When:dd/MM}";
                if (!measured.Significant) { mFg = MuteFg; mBg = MuteBg; }
                else if (measured.DeltaLow1Pct > 0) { mFg = OkFg; mBg = OkBg; }
                else { mFg = BadFg; mBg = BadBg; }
            }

            return new TweakVm
            {
                Model = t,
                Footnote = note.ToString(),
                CanMeasure = app.Verdict is Verdict.Applicable or Verdict.DejaApplique && !t.NeedsRestart,
                Measured = measuredText,
                MeasuredFg = mFg,
                MeasuredBg = mBg,
                Badge = badge, BadgeFg = fg, BadgeBg = bg,
                Verdict = app.Verdict,
                IsOn = app.Verdict == Verdict.DejaApplique,
                // « Appliqué » et non « Activé » : un réglage nommé « Accélération de
                // la souris » qui affiche « Activé » laisse croire que l'accélération
                // est active, alors que c'est le réglage qui la coupe qui l'est.
                StateLabel = app.Verdict switch
                {
                    Verdict.DejaApplique => "Appliqué",
                    Verdict.NonPertinent => "Sans objet",
                    Verdict.Bloque => "Bloqué",
                    _ => "Non appliqué"
                },
                ActionLabel = app.Verdict == Verdict.DejaApplique ? "Annuler ce réglage" : "Appliquer ce réglage",
                ActionEnabled = app.Verdict is Verdict.Applicable or Verdict.DejaApplique
            };
        }).ToList();
    }

    void Measure_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TweakVm vm }) return;

        var win = new AttributionWindow(vm.Model, _journal, _attributions) { Owner = this };
        win.ShowDialog();

        _attributions = AttributionStore.Load();
        RefreshTweaks();
        if (win.Changed) RefreshJournal();
        RefreshBench();
    }

    void Tweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton { Tag: TweakVm vm }) return;

        try
        {
            if (vm.Verdict == Verdict.DejaApplique)
            {
                int n = _journal.RevertTweak(vm.Model.Id);
                if (n == 0)
                    MessageBox.Show(
                        "Ce réglage était déjà en place avant qu'Aeropeek ne le voie : il n'y a rien à restaurer, " +
                        "car l'application n'a pas enregistré son état d'origine.\n\n" +
                        "Aeropeek n'annule que ce qu'il a lui-même modifié.",
                        "Rien à annuler", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                vm.Model.Apply(_journal);
                if (vm.Model.NeedsRestart)
                    MessageBox.Show("Ce réglage prend effet après un redémarrage de Windows.",
                        "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Opération refusée : " + ex.Message, "Aeropeek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshTweaks();
        RefreshJournal();
    }

    // ---------- Utilitaires ----------

    static readonly (string Id, string Accent)[] UtilTints =
    {
        ("cleanmgr", "#5B9BF8"), ("msinfo32", "#5B9BF8"), ("gpu-restart", "#B98CF7"),
        ("flushdns", "#3BB6E8"), ("sfc", "#E0A63C"), ("dism", "#6BC97F"), ("chkdsk", "#F2A0A0"),
        ("oosu10", "#E86A9B"), ("winutil", "#F0A868")
    };

    void RefreshUtils()
    {
        UtilsList.ItemsSource = Utilities.All.Select(u =>
        {
            string hex = UtilTints.FirstOrDefault(t => t.Id == u.Id).Accent ?? "#8497AC";
            var accent = B(hex);
            var tint = new SolidColorBrush(((SolidColorBrush)accent).Color) { Opacity = 0.14 };

            return new
            {
                Utility = u,
                u.Title,
                u.Description,
                u.Display,
                u.ActionLabel,
                Icon = Geometry.Parse(u.Icon),
                Accent = accent,
                Tint = tint,
                Caution = u.Caution ?? "",
                CautionVisibility = string.IsNullOrEmpty(u.Caution) ? Visibility.Collapsed : Visibility.Visible
            };
        }).ToList();
    }

    void Utility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag == null) return;
        var u = (Utility)b.Tag.GetType().GetProperty("Utility")!.GetValue(b.Tag)!;

        try
        {
            switch (u.Kind)
            {
                case UtilityKind.Launch:
                    Utilities.Launch(u);
                    break;

                case UtilityKind.DriverRestart:
                {
                    var answer = MessageBox.Show(
                        u.Caution + "\n\nRedémarrer le pilote graphique maintenant ?",
                        u.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (answer == MessageBoxResult.Yes) Utilities.RestartGraphicsDriver();
                    break;
                }

                case UtilityKind.Run:
                    new ConsoleWindow(u) { Owner = this }.ShowDialog();
                    break;

                case UtilityKind.External:
                {
                    // Un outil tiers écrit hors du journal : c'est la seule chose
                    // qu'Aeropeek fait sans pouvoir l'annuler, il faut le dire avant.
                    var answer = MessageBox.Show(
                        $"{u.Title} va être téléchargé depuis {u.Display}, puis lancé avec les "
                        + "droits administrateur." + Environment.NewLine + Environment.NewLine
                        + (u.Signer != null
                            ? $"Sa signature sera vérifiée avant exécution : sans une signature valide de "
                              + $"{u.Signer}, rien ne sera lancé."
                            : "Ce fichier n'est pas signé par son auteur : Aeropeek ne peut vérifier que "
                              + "son adresse de téléchargement, pas son contenu.")
                        + Environment.NewLine + Environment.NewLine
                        + "Ce que cet outil modifiera n'entrera pas dans le journal d'Aeropeek et ne "
                        + "pourra pas être annulé depuis cette application." + Environment.NewLine + Environment.NewLine
                        + "Continuer ?",
                        u.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (answer == MessageBoxResult.Yes)
                        new ConsoleWindow(u) { Owner = this }.ShowDialog();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- DNS ----------

    List<NetAdapter> _adapters = new();
    readonly Dictionary<string, long> _dnsPing = new();

    async Task RefreshDns(Task<List<NetAdapter>>? pending = null)
    {
        // Énumérer les cartes réseau passe par WMI : court, mais pas instantané.
        // Au lancement, la mesure a déjà été lancée en parallèle des autres :
        // on récupère alors son résultat au lieu d'en redemander un.
        _adapters = await (pending ?? Task.Run(DnsOps.Adapters));

        // Prise RJ45 pour le filaire, ondes pour le sans-fil.
        const string IconEthernet =
            "M 4,11 H 20 V 17 A 2,2 0 0 1 18,19 H 6 A 2,2 0 0 1 4,17 Z "
          + "M 9.5,11 V 7 A 1,1 0 0 1 10.5,6 H 13.5 A 1,1 0 0 1 14.5,7 V 11 "
          + "M 8,19 V 15 M 12,19 V 15 M 16,19 V 15";
        const string IconWifi =
            "M 2.5,8.6 A 15,15 0 0 1 21.5,8.6 M 5.6,12.1 A 10,10 0 0 1 18.4,12.1 "
          + "M 8.8,15.6 A 5,5 0 0 1 15.2,15.6 M 11.2,19.2 A 0.8,0.8 0 1 0 12.8,19.2 A 0.8,0.8 0 1 0 11.2,19.2";

        AdaptersList.ItemsSource = _adapters.Select(a => new
        {
            a.Name,
            Kind = a.Wireless ? "Sans fil" : "Filaire",
            Icon = Geometry.Parse(a.Wireless ? IconWifi : IconEthernet),
            Servers = a.DnsText,
            Source = a.FromDhcp ? "fournis par la box" : "définis manuellement"
        }).ToList();

        DnsSummary.Text = _adapters.Count switch
        {
            0 => "Aucune connexion réseau active.",
            1 => $"Connexion « {_adapters[0].Name} » · serveurs actuels : {_adapters[0].DnsText}",
            _ => $"{_adapters.Count} connexions actives · les changements s'appliquent à « {_adapters[0].Name} »"
        };

        RenderDnsCards();
    }

    void RenderDnsCards()
    {
        var current = _adapters.FirstOrDefault();
        var currentServers = current?.Dns ?? Array.Empty<string>();
        bool currentIsDhcp = current?.FromDhcp ?? true;

        DnsList.ItemsSource = DnsOps.Providers.Select(p =>
        {
            bool active = p.IsDhcp
                ? currentIsDhcp
                : !currentIsDhcp && currentServers.Contains(p.Primary);

            long ms = _dnsPing.TryGetValue(p.Name, out var v) ? v : -2;
            string latency = ms switch
            {
                -2 => "",                       // pas encore testé
                -1 => "injoignable",
                _ => ms + " ms"
            };
            var latencyFg = ms switch
            {
                -1 => BadFg,
                >= 0 and <= 20 => OkFg,
                > 20 and <= 60 => WarnFg,
                > 60 => BadFg,
                _ => MuteFg
            };

            var accent = B(p.Accent);
            var tint = new SolidColorBrush(((SolidColorBrush)accent).Color) { Opacity = 0.14 };

            return new
            {
                Provider = p,
                p.Name,
                Addresses = p.IsDhcp ? "attribués automatiquement" : $"{p.Primary} · {p.Secondary}",
                p.Note,
                p.Tags,
                Icon = Geometry.Parse(p.Icon),
                Accent = accent,
                Tint = tint,
                Latency = latency,
                LatencyFg = latencyFg,
                Border = active ? accent : B("#1C2836"),
                ActionLabel = active ? "Actif" : "Utiliser",
                CanApply = !active
            };
        }).ToList();
    }

    async void DnsTest_Click(object sender, RoutedEventArgs e)
    {
        BtnDnsTest.IsEnabled = false;
        DnsSummary.Text = "Test des serveurs en cours…";
        Veil(DnsVeil, true);
        try
        {
            var jobs = DnsOps.Providers
                .Where(p => !p.IsDhcp)
                .Select(async p => (p.Name, Ms: await DnsOps.PingAsync(p.Primary)))
                .ToList();

            foreach (var (name, ms) in await Task.WhenAll(jobs))
                _dnsPing[name] = ms;

            RenderDnsCards();

            var best = _dnsPing.Where(kv => kv.Value >= 0).OrderBy(kv => kv.Value).FirstOrDefault();
            DnsSummary.Text = best.Key != null
                ? $"Le plus rapide depuis chez toi : {best.Key} à {best.Value} ms. "
                  + "Un écart de quelques millisecondes ne se ressent que sur la résolution des noms."
                : "Aucun serveur n'a répondu. Certains réseaux bloquent le ping ICMP sans bloquer le DNS.";
        }
        finally { Veil(DnsVeil, false); BtnDnsTest.IsEnabled = true; }
    }

    void CustomDns_Toggle(object sender, RoutedEventArgs e)
    {
        bool open = CustomDnsPanel.Visibility != Visibility.Visible;
        BtnCustomDns.Content = open ? "Masquer" : "Afficher";

        var ease = new System.Windows.Media.Animation.QuinticEase
        { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        if (open)
        {
            // pré-remplissage : si des serveurs manuels sont en place, on les reprend
            var current = _adapters.FirstOrDefault();
            if (current is { FromDhcp: false } && CustomPrimary.Text.Length == 0)
            {
                CustomPrimary.Text = current.Dns.ElementAtOrDefault(0) ?? "";
                CustomSecondary.Text = current.Dns.ElementAtOrDefault(1) ?? "";
            }

            CustomDnsPanel.Visibility = Visibility.Visible;
            CustomDnsPanel.Opacity = 0;
            var slide = new TranslateTransform(0, -8);
            CustomDnsPanel.RenderTransform = slide;
            CustomDnsPanel.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            slide.BeginAnimation(TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(280))
                { EasingFunction = ease });
        }
        else
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(
                1, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) => CustomDnsPanel.Visibility = Visibility.Collapsed;
            CustomDnsPanel.BeginAnimation(OpacityProperty, fade);
        }
    }

    static bool IsIPv4(string s) =>
        System.Net.IPAddress.TryParse(s.Trim(), out var ip)
        && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    async void CustomDns_Apply(object sender, RoutedEventArgs e)
    {
        string primary = CustomPrimary.Text.Trim();
        string secondary = CustomSecondary.Text.Trim();

        if (!IsIPv4(primary))
        {
            CustomDnsHint.Text = "Le serveur principal n'est pas une adresse IPv4 valide.";
            CustomDnsHint.Foreground = BadFg;
            return;
        }
        if (secondary.Length > 0 && !IsIPv4(secondary))
        {
            CustomDnsHint.Text = "Le serveur secondaire n'est pas une adresse IPv4 valide.";
            CustomDnsHint.Foreground = BadFg;
            return;
        }

        var adapter = _adapters.FirstOrDefault();
        if (adapter == null) return;

        try
        {
            var custom = new DnsProvider("Personnalisé", primary, secondary,
                "", Array.Empty<string>(), "#B98CF7", "");
            _journal.Record(DnsOps.Apply(adapter, custom));

            CustomDnsHint.Text = "Appliqué. Annulable depuis le Journal.";
            CustomDnsHint.Foreground = OkFg;
            await RefreshDns();
            RefreshJournal();
        }
        catch (Exception ex)
        {
            CustomDnsHint.Text = ex.Message;
            CustomDnsHint.Foreground = BadFg;
        }
    }

    async void DnsApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag == null) return;
        var provider = (DnsProvider)b.Tag.GetType().GetProperty("Provider")!.GetValue(b.Tag)!;
        var adapter = _adapters.FirstOrDefault();
        if (adapter == null) return;

        try
        {
            _journal.Record(DnsOps.Apply(adapter, provider));
            await RefreshDns();
            RefreshJournal();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Nettoyage ----------

    List<CleanScan> _cleanScans = new();
    readonly HashSet<string> _cleanSelected = new(StringComparer.OrdinalIgnoreCase);
    bool _cleanScanned;

    async Task RunCleanScan(Task<List<CleanScan>>? pending = null)
    {
        BtnCleanScan.IsEnabled = false;
        BtnCleanRun.IsEnabled = false;
        CleanSummary.Text = "Analyse des dossiers en cours…";
        CleanScanVeil.Content = "Analyse des dossiers…";
        Veil(CleanScanVeil, true);
        try
        {
            _cleanScans = await (pending ?? Cleanup.ScanAsync());

            if (!_cleanScanned)
            {
                foreach (var s in _cleanScans.Where(s => s.Available && s.Target.DefaultOn && s.Bytes > 0))
                    _cleanSelected.Add(s.Target.Id);
                _cleanScanned = true;
            }

            long total = _cleanScans.Where(s => s.Available).Sum(s => s.Bytes);
            CleanSummary.Text = $"{CleanScan.Human(total)} récupérables au total · "
                              + $"{_cleanScans.Count(s => s.Available)} emplacements analysés";
            RenderClean();
        }
        finally
        {
            Veil(CleanScanVeil, false);
            BtnCleanScan.IsEnabled = true;
            BtnCleanRun.IsEnabled = true;
        }
    }

    void RenderClean()
    {
        CleanList.ItemsSource = _cleanScans
            .OrderByDescending(s => s.Target.Group == "Lié aux jeux")
            .ThenByDescending(s => s.Bytes)
            .Select(s =>
            {
                var (fg, bg, label) = s.Target.Risk switch
                {
                    CleanRisk.NoReturn => (BadFg, BadBg, "Sans retour"),
                    CleanRisk.Check => (WarnFg, WarnBg, "À vérifier"),
                    _ => (MuteFg, MuteBg, "")
                };

                string desc = s.Available
                    ? s.Target.Description
                    : "Cet emplacement n'existe pas sur ta machine.";

                return new CleanVm
                {
                    Target = s.Target,
                    Description = desc,
                    Size = s.Available ? s.Size : "—",
                    SizeFg = s.Bytes > 0 ? B("#E8EEF5") : MuteFg,
                    RiskLabel = label,
                    RiskFg = fg,
                    RiskBd = label.Length == 0 ? Brushes.Transparent : fg,
                    Available = s.Available,
                    Selected = _cleanSelected.Contains(s.Target.Id)
                };
            }).ToList();

        UpdateCleanTotal();
    }

    void UpdateCleanTotal()
    {
        long sel = _cleanScans.Where(s => _cleanSelected.Contains(s.Target.Id)).Sum(s => s.Bytes);
        CleanSelected.Text = CleanScan.Human(sel);
        BtnCleanRun.IsEnabled = sel > 0;
    }

    void CleanToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: CleanVm vm } cb) return;
        if (cb.IsChecked == true) _cleanSelected.Add(vm.Id); else _cleanSelected.Remove(vm.Id);
        UpdateCleanTotal();
    }

    async void CleanScan_Click(object sender, RoutedEventArgs e) => await RunCleanScan();

    void CleanAll_Click(object sender, RoutedEventArgs e)
    {
        // seuls les emplacements présents et non vides peuvent être sélectionnés
        var usable = _cleanScans.Where(s => s.Available && s.Bytes > 0).ToList();
        bool allOn = usable.Count > 0 && usable.All(s => _cleanSelected.Contains(s.Target.Id));

        _cleanSelected.Clear();
        if (!allOn) foreach (var s in usable) _cleanSelected.Add(s.Target.Id);

        BtnCleanAll.Content = allOn ? "Tout sélectionner" : "Tout désélectionner";
        RenderClean();
    }

    /// <summary>Balayage lumineux pendant l'analyse, en boucle.</summary>
    /// <summary>
    /// Montre ou cache un voile d'attente. Le balayage est declenche par le
    /// gabarit lui-meme quand le voile devient visible : rien a piloter ici.
    /// </summary>
    static void Veil(ContentControl veil, bool on) =>
        veil.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    async void CleanRun_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _cleanScans.Where(s => _cleanSelected.Contains(s.Target.Id) && s.Available).ToList();
        if (chosen.Count == 0) return;

        long total = chosen.Sum(s => s.Bytes);
        bool risky = chosen.Any(s => s.Target.Risk != CleanRisk.Safe);

        var answer = MessageBox.Show(
            $"Supprimer {CleanScan.Human(total)} répartis sur {chosen.Count} emplacement(s) ?\n\n"
            + (risky ? "Ta sélection contient des éléments marqués « À vérifier » ou « Sans retour ».\n\n" : "")
            + "Cette suppression est définitive : elle ne pourra pas être annulée.",
            "Confirmer le nettoyage", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        BtnCleanRun.IsEnabled = false;
        BtnCleanScan.IsEnabled = false;
        CleanSummary.Text = "Suppression en cours…";

        // Supprimer plusieurs gigaoctets prend du temps : le même voile que
        // l'analyse, avec l'emplacement en cours de traitement.
        CleanScanVeil.Content = "Suppression en cours…";
        Veil(CleanScanVeil, true);

        int deleted = 0, skipped = 0; long freed = 0;
        var steps = new Progress<string>(step => CleanScanVeil.Content = step);
        var records = new List<OpRecord>();

        try
        {
            await Task.Run(() =>
            {
                foreach (var s in chosen)
                {
                    ((IProgress<string>)steps).Report(s.Target.Title + "…");
                    var outcome = Cleanup.Delete(s.Target);
                    deleted += outcome.Deleted;
                    skipped += outcome.Skipped;
                    freed += outcome.Freed;

                    // On accumule ici et on journalise au retour : appeler le
                    // fil d'interface à chaque emplacement le bloquait autant
                    // que si tout s'était passé dessus.
                    records.Add(Cleanup.Record(s.Target, outcome));
                }
            });
        }
        finally { Veil(CleanScanVeil, false); }

        foreach (var r in records) _journal.Record(r);

        _cleanSelected.Clear();
        _cleanScanned = false;
        await RunCleanScan();
        RefreshJournal();

        MessageBox.Show(
            $"{CleanScan.Human(freed)} libérés.\n\n{deleted} fichier(s) supprimé(s)"
            + (skipped > 0 ? $", {skipped} ignoré(s) car en cours d'utilisation." : "."),
            "Nettoyage terminé", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- Mode Match ----------

    MatchMode _match = null!;
    bool _showIdleServices;              // les services « Aucun gain » sont repliés par défaut
    readonly HashSet<string> _selectedServices = new(StringComparer.OrdinalIgnoreCase);
    List<ServiceCandidate> _services = new();
    List<AppCandidate> _apps = new();
    readonly HashSet<string> _selectedApps = new(StringComparer.OrdinalIgnoreCase);
    System.Windows.Threading.DispatcherTimer? _gameWatcher;
    bool _gameWasRunning;

    async Task RefreshApps(Task<List<AppCandidate>>? pending = null)
    {
        _apps = await (pending ?? AppOps.SampleAsync(4));
        RenderApps();
    }

    /// <summary>Reconstruit la liste sans refaire de mesure.</summary>
    void RenderApps()
    {
        // Rien n'est coché d'office : fermer une application est plus intrusif
        // que suspendre un service, l'utilisateur doit le décider.
        AppsList.ItemsSource = _apps.Select(a => new AppVm
        {
            Model = a,
            Note = a.Active
                ? $"Consomme du processeur en ce moment. {a.Instances} processus."
                : $"{a.Instances} processus, au repos. La fermer libère de la mémoire, pas du processeur.",
            Memory = a.MemoryText,
            Cpu = a.Active ? $"{a.CpuPercent:0.0} % processeur" : "au repos",
            ActivityFg = a.Active ? WarnFg : MuteFg,
            Selected = _selectedApps.Contains(a.Process)
        }).ToList();

        NoAppsText.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshAppsHeader();
        RefreshMatchHero();
    }

    void App_Toggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: AppVm vm } cb) return;
        if (cb.IsChecked == true) _selectedApps.Add(vm.Process); else _selectedApps.Remove(vm.Process);
        RefreshAppsHeader();
        RefreshMatchHero();
    }

    async Task RefreshServices(Task<List<ServiceCandidate>>? pending = null)
    {
        BtnResample.IsEnabled = false;
        try
        {
            _services = await (pending ?? ServiceOps.SampleAsync(5));

            // Premier passage : on coche ce qui PEUT interrompre une partie, pas ce
            // qui s'est trouvé actif pendant les cinq secondes de mesure. Windows
            // Update ne télécharge pas toutes les cinq secondes ; l'échantillon le
            // manquerait presque toujours.
            if (_selectedServices.Count == 0)
                foreach (var s in _services.Where(s => s.Running && s.Impact == ServiceImpact.Reel))
                    _selectedServices.Add(s.Name);

            RenderServices();
        }
        finally { BtnResample.IsEnabled = true; }
    }

    /// <summary>Reconstruit la liste sans refaire de mesure.</summary>
    void RenderServices()
    {
        // Dix services au repos noyaient les douze qui comptent. Ils restent
        // accessibles d'un clic, mais ne s'imposent plus à l'écran.
        var shown = _showIdleServices
            ? _services
            : _services.Where(s => s.Impact == ServiceImpact.Reel).ToList();

        int idle = _services.Count - _services.Count(s => s.Impact == ServiceImpact.Reel);
        IdleLink.Text = _showIdleServices
            ? "Masquer les services sans effet"
            : $"Afficher les {idle} services sans effet sur le jeu";
        IdleLink.Visibility = idle > 0 ? Visibility.Visible : Visibility.Collapsed;

        ServicesList.ItemsSource = shown.Select(s =>
        {
            string activity;
            Brush fg;
            if (!s.Running) { activity = "arrêté"; fg = MuteFg; }
            else if (s.Active)
            {
                activity = s.IoKoPerSec >= 200
                    ? $"{s.IoKoPerSec / 1024:0.0} Mo/s"
                    : $"{s.CpuPercent:0.0} % processeur";
                fg = WarnFg;
            }
            else { activity = "au repos"; fg = MuteFg; }

            bool reel = s.Impact == ServiceImpact.Reel;

            // « Rien mesuré » ne veut pas dire « rien à gagner » : ces services
            // travaillent par à-coups, et cinq secondes ne les attrapent pas.
            string note = s.Note;
            if (reel && !s.Active) note += " Rien mesuré à l'instant, mais il peut se réveiller en partie.";

            return new ServiceVm
            {
                Model = s, Name = s.Name, Label = s.Label,
                Note = note,
                Activity = activity, ActivityFg = fg,
                Impact = reel ? "Peut gêner" : "Aucun gain",
                ImpactFg = reel ? WarnFg : MuteFg,
                // Un avertissement se voit, un « rien à gagner » ne doit pas
                // attirer l'oeil plus qu'un « Correct » juste au-dessus.
                ImpactBd = reel ? WarnFg : Dim(MuteFg, 0.45),
                Selected = _selectedServices.Contains(s.Name)
            };
        }).ToList();

        // Seuls les services en cours d'exécution peuvent être suspendus.
        var suspendables = _services.Where(s => s.Running).ToList();
        bool allOn = suspendables.Count > 0 && suspendables.All(s => _selectedServices.Contains(s.Name));
        BtnSelectAll.Content = allOn ? "Tout décocher" : "Tout cocher";
        BtnSelectAll.IsEnabled = suspendables.Count > 0;

        ServicesHeader.Text = suspendables.Count == 0
            ? "SERVICES WINDOWS"
            : $"SERVICES WINDOWS — {_selectedServices.Count} SUR {suspendables.Count} COCHÉS";

        RefreshMatchHero();
    }

    /// <summary>Déplie ou replie la séquence d'activation.</summary>
    void How_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool open = HowCard.Visibility != Visibility.Visible;
        HowCard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HowLink.Text = open ? "Masquer" : "Comment ça marche ?";
    }

    /// <summary>Montre ou masque les services que suspendre ne rapporte rien.</summary>
    void ShowIdle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _showIdleServices = !_showIdleServices;
        RenderServices();
    }

    void SelectAllApps_Click(object sender, RoutedEventArgs e)
    {
        bool allOn = _apps.Count > 0 && _apps.All(a => _selectedApps.Contains(a.Process));

        _selectedApps.Clear();
        if (!allOn)
            foreach (var a in _apps) _selectedApps.Add(a.Process);

        RenderApps();
    }

    void RefreshAppsHeader()
    {
        bool allOn = _apps.Count > 0 && _apps.All(a => _selectedApps.Contains(a.Process));
        BtnSelectApps.Content = allOn ? "Tout décocher" : "Tout cocher";
        BtnSelectApps.IsEnabled = _apps.Count > 0;

        AppsHeader.Text = _apps.Count == 0
            ? "APPLICATIONS EN ARRIÈRE-PLAN"
            : $"APPLICATIONS EN ARRIÈRE-PLAN — {_selectedApps.Count} SUR {_apps.Count} COCHÉES";
    }

    /// <summary>Les deux interrupteurs secondaires ne font que rafraîchir le résumé.</summary>
    void Option_Toggle(object sender, RoutedEventArgs e) => RefreshMatchHero();

    void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var suspendables = _services.Where(s => s.Running).ToList();
        bool allOn = suspendables.Count > 0 && suspendables.All(s => _selectedServices.Contains(s.Name));

        _selectedServices.Clear();
        if (!allOn)
            foreach (var s in suspendables) _selectedServices.Add(s.Name);

        RenderServices();
    }

    void RefreshMatchHero()
    {
        int services = _selectedServices.Count, apps = _selectedApps.Count;
        bool priority = ChkPriority?.IsChecked == true;

        if (SwMatch == null) return;   // appelé avant que la vue soit construite

        // L'interrupteur suit l'état réel : une activation qui échoue ne doit pas
        // laisser le bouton allumé sur un mode qui ne tourne pas.
        SwMatch.IsChecked = _match.Active;

        if (_match.Active)
        {
            MatchState.Text = "Actif";
            MatchTitle.Text = "Mode Match actif";

            var done = new List<string>();
            if (_match.ClosedApps > 0) done.Add($"{_match.ClosedApps} application(s) fermée(s)");
            if (_match.SuspendedServices > 0) done.Add($"{_match.SuspendedServices} service(s) suspendu(s)");

            MatchDetail.Text = (done.Count > 0 ? string.Join(" · ", done) + "." : "Aucun élément n'a pu être suspendu.")
                             + " Coupe l'interrupteur pour tout remettre.";
            MatchHero.BorderBrush = AccFg;
            SwMatch.IsEnabled = true;
        }
        else
        {
            MatchState.Text = "En veille";

            var todo = new List<string>();
            if (apps > 0) todo.Add($"{apps} application(s) fermée(s)");
            if (services > 0) todo.Add($"{services} service(s) suspendu(s)");
            if (priority) todo.Add("cs2.exe en priorité haute");

            bool ready = todo.Count > 0;
            MatchTitle.Text = ready ? "Prêt à activer" : "Rien de sélectionné";

            MatchDetail.Text = !ready
                ? "Coche au moins une application, un service, ou la priorité ci-dessous."
                : "À l'activation : " + string.Join(" · ", todo) + "."
                  + (ChkAuto?.IsChecked == true
                        ? " Se déclenchera tout seul au lancement de CS2."
                        : " Utilise l'interrupteur, ou active le déclenchement automatique.");

            MatchHero.BorderBrush = B("#1C2836");
            SwMatch.IsEnabled = ready;
        }
    }

    void Service_Toggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: ServiceVm vm } cb) return;
        if (cb.IsChecked == true) _selectedServices.Add(vm.Name);
        else _selectedServices.Remove(vm.Name);

        // met à jour le libellé du bouton « Tout sélectionner » sans reconstruire la liste
        var suspendables = _services.Where(s => s.Running).ToList();
        bool allOn = suspendables.Count > 0 && suspendables.All(s => _selectedServices.Contains(s.Name));
        BtnSelectAll.Content = allOn ? "Tout décocher" : "Tout cocher";
        ServicesHeader.Text = suspendables.Count == 0
            ? "SERVICES WINDOWS"
            : $"SERVICES WINDOWS — {_selectedServices.Count} SUR {suspendables.Count} COCHÉS";

        RefreshMatchHero();
    }

    bool _matchBusy;

    async void Match_Click(object sender, RoutedEventArgs e) => await ToggleMatch();

    /// <summary>
    /// Arrêter neuf services et fermer trois applications demande plusieurs
    /// secondes : chaque arrêt attend que Windows confirme. Sur le fil
    /// d'interface, la fenêtre cessait de répondre pendant tout ce temps —
    /// c'était le gel ressenti à l'activation.
    /// </summary>
    async Task ToggleMatch()
    {
        if (_matchBusy) return;          // le veilleur de jeu peut appeler pendant qu'on travaille
        _matchBusy = true;

        bool stopping = _match.Active;
        MatchVeil.Content = stopping ? "Restauration en cours…" : "Application en cours…";
        Veil(MatchVeil, true);
        SwMatch.IsEnabled = false;

        // Chaque étape remonte son libellé : on voit ce qui est en train de se
        // faire, pas seulement que quelque chose se fait.
        var progress = new Progress<string>(step => MatchVeil.Content = step);

        try
        {
            if (stopping)
            {
                int n = await Task.Run(() => _match.Stop(progress));
                MatchDetail.Text = $"{n} élément(s) restauré(s).";
            }
            else
            {
                var apps = _apps.Where(a => _selectedApps.Contains(a.Process))
                                .Select(a => (a.Process, a.Label))
                                .ToList();

                var plan = new MatchMode.Plan(_selectedServices.ToList(), apps,
                                              ChkPriority.IsChecked == true);
                var (svc, closed, prio) = await Task.Run(() => _match.Start(plan, progress));

                if (svc + closed == 0 && !prio)
                    MessageBox.Show(
                        "Rien n'a pu être appliqué : les services sélectionnés ne tournaient pas, "
                        + "les applications non plus, et CS2 n'est pas lancé pour la priorité.",
                        "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Veil(MatchVeil, false);
            _matchBusy = false;
        }

        RefreshMatchHero();
        RefreshJournal();
    }

    void StartGameWatcher()
    {
        // Deux secondes, et la lecture se fait ailleurs : détecter le jeu passe
        // par Process.GetProcessesByName, qui parcourt toute la table des
        // processus. Fait sur le fil d'interface toutes les 1,5 s, cela suffisait
        // à hacher le défilement en permanence — un à-coup discret, mais
        // permanent. Le lancement d'une partie n'a pas besoin d'être vu à la
        // demi-seconde près.
        bool busy = false;
        _gameWatcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameWatcher.Tick += async (_, _) =>
        {
            if (busy) return;                        // une lecture traîne encore
            busy = true;
            try
            {
                bool running = await Task.Run(() => Benchmark.IsRunning("cs2"));

                if (ChkAuto.IsChecked != true) { _gameWasRunning = running; return; }

                bool somethingToDo = _selectedServices.Count > 0 || _selectedApps.Count > 0
                                     || ChkPriority.IsChecked == true;

                if (running && !_gameWasRunning && !_match.Active && somethingToDo)
                    _ = ToggleMatch();               // le jeu vient de démarrer
                else if (!running && _gameWasRunning && _match.Active)
                    _ = ToggleMatch();               // le jeu vient de se fermer
                _gameWasRunning = running;
            }
            finally { busy = false; }
        };
        _gameWatcher.Start();
    }

    async void Resample_Click(object sender, RoutedEventArgs e)
    {
        await RefreshApps();
        await RefreshServices();
    }

    // ---------- Benchmark ----------

    async void Capture_Click(object sender, RoutedEventArgs e)
    {
        const int seconds = 60;

        if (!Benchmark.ToolAvailable)
        {
            MessageBox.Show("PresentMon est introuvable à côté de l'application.", "Aeropeek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Benchmark.IsRunning("cs2"))
        {
            MessageBox.Show(
                "CS2 n'est pas lancé.\n\nLance le jeu, mets-toi en jeu — pas dans un menu — puis relance la capture.",
                "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnCapture.IsEnabled = false;
        var label = string.IsNullOrWhiteSpace(BenchLabel.Text) ? "Mesure" : BenchLabel.Text.Trim();

        // Deux phases : on laisse le temps de revenir dans le jeu, puis on enregistre.
        int left = Benchmark.DelaySeconds + seconds;
        void Tick()
        {
            int inDelay = left - seconds;
            BenchStatus.Text = inDelay > 0
                ? $"Retourne dans le jeu — enregistrement dans {inDelay} s"
                : $"Enregistrement — {left} s restantes";
        }
        Tick();

        // On surveille aussi si le jeu reste au premier plan : une mesure prise
        // pendant un Alt+Tab ne veut rien dire, autant le dire plutôt que d'afficher
        // des chiffres faux.
        int awaySeconds = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            left--;
            if (left >= 0) Tick();
            if (left < seconds && !Native.ForegroundProcessName().Equals("cs2", StringComparison.OrdinalIgnoreCase))
                awaySeconds++;
        };
        timer.Start();

        try
        {
            var run = await Benchmark.CaptureAsync("cs2", seconds, label);
            _bench.Add(run);
            BenchStatus.Text = $"Terminé — {run.Frames} images mesurées.";
            BenchLabel.Text = "";
            RefreshBench();

            if (awaySeconds >= 3)
                MessageBox.Show(
                    $"CS2 n'était pas au premier plan pendant {awaySeconds} s de l'enregistrement.\n\n" +
                    "Les images produites en arrière-plan faussent surtout les 1% et 0,1% lows. " +
                    "Refais la mesure en restant dans le jeu jusqu'à la fin.",
                    "Mesure peu fiable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            BenchStatus.Text = "";
            MessageBox.Show(ex.Message, "Capture impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            timer.Stop();
            BtnCapture.IsEnabled = true;
        }
    }

    void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>
    /// Trace les deux dernières mesures superposées. L'échelle s'adapte aux données :
    /// un plafond fixe à 25 ms écraserait une courbe qui vit autour de 2 ms.
    /// </summary>
    void DrawChart()
    {
        ChartArea.Children.Clear();
        ChartAxis.Children.Clear();

        var runs = _bench.Runs.Where(r => r.Series.Count > 1).TakeLast(2).ToList();
        if (runs.Count == 0) { ChartCard.Visibility = Visibility.Collapsed; return; }

        ChartCard.Visibility = Visibility.Visible;
        double w = ChartArea.ActualWidth, h = ChartArea.ActualHeight;
        if (w < 20 || h < 20) return;

        var newest = runs[^1];
        var older = runs.Count > 1 ? runs[0] : null;

        ChartNewLabel.Text = newest.Label;
        ChartOldLabel.Text = older?.Label ?? "";
        ChartOldLabel.Visibility = older == null ? Visibility.Collapsed : Visibility.Visible;

        // plafond : le pic le plus haut des deux courbes, arrondi vers le haut
        double peak = runs.SelectMany(r => r.Series).DefaultIfEmpty(1).Max();
        double ceiling = Math.Max(4, Math.Ceiling(peak / 4) * 4);

        double Y(double ms) => h - Math.Min(ms, ceiling) / ceiling * (h - 6) - 3;

        // repères horizontaux
        for (int i = 0; i <= 3; i++)
        {
            double ms = ceiling * i / 3.0;
            double y = Y(ms);
            ChartArea.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, X2 = w, Y1 = y, Y2 = y,
                Stroke = B("#1A2634"), StrokeThickness = 1
            });
            var lbl = new TextBlock
            {
                Text = $"{ms:0.#} ms", FontSize = 11, Foreground = B("#4E6076"),
                FontFamily = new FontFamily("Cascadia Mono, Consolas")
            };
            Canvas.SetRight(lbl, 8);
            Canvas.SetTop(lbl, y - 8);
            ChartAxis.Children.Add(lbl);
        }

        void Plot(BenchRun run, string colour, double thickness)
        {
            var pts = new PointCollection(run.Series.Count);
            for (int i = 0; i < run.Series.Count; i++)
                pts.Add(new Point(i * w / (run.Series.Count - 1), Y(run.Series[i])));

            ChartArea.Children.Add(new System.Windows.Shapes.Polyline
            {
                Points = pts, Stroke = B(colour), StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round
            });
        }

        if (older != null) Plot(older, "#C27C14", 1.6);
        Plot(newest, "#0EA595", 2);

        ChartFootnote.Text = older == null
            ? $"{newest.Series.Count} points sur {newest.DurationS:0} s. Chaque point retient l'image la plus lente de son intervalle."
            : $"Deux mesures superposées. Chaque point retient l'image la plus lente de son intervalle, "
              + "pour que les pics restent visibles malgré le ré-échantillonnage.";
    }

    void RefreshBench()
    {
        var runs = _bench.Runs;
        var rows = new List<BenchVm>();

        double noise = BenchCompare.NoiseFloor(runs, ChangesBetween);

        for (int i = runs.Count - 1; i >= 0; i--)
        {
            var r = runs[i];
            var prev = i > 0 ? runs[i - 1] : null;

            var cmp = prev == null
                ? new Comparison(CompareVerdict.None, "")
                : BenchCompare.Compare(prev, r, ChangesBetween(prev, r), noise);

            string delta = cmp.Text;
            Brush deltaFg = cmp.Verdict switch
            {
                CompareVerdict.Gain => OkFg,
                CompareVerdict.Loss => BadFg,
                CompareVerdict.NotComparable => WarnFg,
                _ => MuteFg
            };

            rows.Add(new BenchVm
            {
                Label = r.Label,
                When = r.When.ToString("dd/MM HH:mm"),
                Fps = $"{r.AvgFps:0}",
                Low1 = $"{r.Low1Fps:0}",
                Low01 = $"{r.Low01Fps:0}",
                MaxMs = $"{r.MaxMs:0.0} ms",
                Stutters = $"{r.Stutters}",
                Delta = delta,
                DeltaFg = deltaFg,
                Footnote = $"{r.Frames} images sur {r.DurationS:0} s · image médiane {r.MedianMs:0.00} ms · "
                           + "une saccade = une image plus de deux fois plus lente que la médiane"
            });
        }

        BenchList.ItemsSource = rows;
        DrawChart();
        if (rows.Count == 0)
            BenchStatus.Text = "Aucune mesure. Lance CS2, entre en jeu, puis capture.";
    }

    /// <summary>
    /// Ce qui a changé entre deux captures. Renvoie null — et non une liste vide —
    /// quand la période précède la chronologie du journal : « je ne sais pas » et
    /// « rien n'a bougé » ne doivent jamais se confondre.
    /// </summary>
    IReadOnlyList<ChangeMark>? ChangesBetween(BenchRun a, BenchRun b) =>
        _journal.ChangesSince is { } since && a.When >= since
            ? _journal.ChangesBetween(a.When, b.When)
            : null;

    // ---------- Journal ----------

    static string RecordPath(OpRecord r) => r.Kind switch
    {
        "files-deleted" => r.SubKey,
        "service" => $"service {r.ServiceName}",
        _ => $@"{r.Hive}\{r.SubKey}\{r.ValueName}"
    };

    static string RecordChange(OpRecord r) => r.Kind switch
    {
        "files-deleted" => $"{r.NewValue} · {r.PreviousValue} libérés — suppression définitive",
        "service" => r.WasRunning
            ? "était en cours d'exécution, arrêté — l'annulation le relancera"
            : "était déjà arrêté, rien à restaurer",
        _ => r.Existed
            ? $"était « {r.PreviousValue} », mis à « {r.NewValue} »"
            : $"n'existait pas, créé à « {r.NewValue} » — l'annulation le supprimera"
    };

    void RefreshJournal()
    {
        // Un réglage écrit souvent plusieurs valeurs. Les lister une par une
        // laisserait annuler un tiers d'un réglage et en garder deux : on
        // regroupe donc les écritures qui vont ensemble, et on les annule ensemble.
        var all = _journal.Sessions.SelectMany(s => s.Records).OrderByDescending(r => r.When).ToList();

        var rows = all
            .GroupBy(r => (r.TweakId, r.Description))
            .Select(g =>
            {
                var first = g.First();
                bool undo = g.All(r => Ops.CanRevert(r.Kind));
                string change = RecordChange(first);
                if (g.Count() > 1) change += $" · {g.Count()} écritures";

                return new JournalVm
                {
                    Title = first.Description,
                    When = g.Max(r => r.When).ToString("dd/MM HH:mm"),
                    Path = g.Count() > 1
                        ? string.Join("   ·   ", g.Select(RecordPath))
                        : RecordPath(first),
                    Change = change,
                    Ids = g.Select(r => r.Id).ToList(),
                    CanUndo = undo
                };
            })
            .OrderByDescending(v => v.When)
            .ToList();

        JournalList.ItemsSource = rows;

        int fixes = rows.Count(v => v.CanUndo);
        JournalSummary.Text = rows.Count == 0
            ? "Aucune modification. Aeropeek n'a rien changé sur cette machine."
            : fixes == rows.Count
                ? $"{rows.Count} modification(s), toutes réversibles."
                : $"{rows.Count} modification(s), dont {rows.Count - fixes} définitive(s).";
    }

    void JournalUndo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not JournalVm vm) return;

        var answer = MessageBox.Show(
            $"Annuler « {vm.Title} » ?" + Environment.NewLine + Environment.NewLine
            + "L'état exact qui précédait sera remis"
            + (vm.Ids.Count > 1 ? $", pour les {vm.Ids.Count} écritures de ce réglage." : ".")
            + " Rien d'autre n'est touché.",
            "Annuler une modification", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        int done = _journal.RevertRecords(vm.Ids);

        if (done < vm.Ids.Count)
            MessageBox.Show(
                done == 0
                    ? "Rien n'a pu être annulé : Windows a refusé la restauration."
                    : $"{done} écriture(s) sur {vm.Ids.Count} restaurée(s). Les autres ont été refusées.",
                "Annulation", MessageBoxButton.OK, MessageBoxImage.Warning);

        RefreshJournal();
        RefreshTweaks();
    }

    void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        if (_journal.TotalRecords == 0)
        {
            MessageBox.Show("Il n'y a rien à annuler.", "Aeropeek");
            return;
        }

        var answer = MessageBox.Show(
            $"Annuler les {_journal.TotalRecords} modification(s) appliquées par Aeropeek ?\n\n" +
            "Chaque valeur retrouve exactement l'état qu'elle avait avant, y compris si elle n'existait pas.",
            "Tout annuler", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        int n = _journal.RevertEverything();
        RefreshTweaks();
        RefreshJournal();
        MessageBox.Show($"{n} modification(s) annulée(s).", "Aeropeek");
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Aeropeek — export de diagnostic ===");
            sb.AppendLine($"Date : {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("--- Machine ---");
            sb.AppendLine($"OS       : {_sys.OsName} {_sys.OsEdition} {_sys.OsDisplayVersion} (build {_sys.OsBuild})");
            sb.AppendLine($"Châssis  : {(_sys.IsLaptop ? "portable" : "poste fixe")}");
            sb.AppendLine($"CPU      : {_sys.CpuName}");
            sb.AppendLine($"Cœurs    : {_sys.Cpu.PhysicalCores} physiques / {_sys.Cpu.LogicalCores} logiques" +
                          (_sys.Cpu.IsHybrid ? $" ({_sys.Cpu.PerformanceCores} P + {_sys.Cpu.EfficiencyCores} E)" : ""));
            foreach (var g in _sys.GpuNames) sb.AppendLine($"GPU      : {g}");
            foreach (var m in _sys.Memory)
                sb.AppendLine($"Mémoire  : {m.Kind} {m.CapacityBytes / 1024 / 1024 / 1024} Go — configurée {m.ConfiguredMhz} MT/s, annoncée {m.RatedMhz} MT/s");
            foreach (var d in _sys.Displays)
                sb.AppendLine($"Écran    : {d.MonitorName} {d.Width}x{d.Height} @ {d.RefreshHz} Hz via {d.AdapterName}");
            sb.AppendLine($"Anticheat: {(_sys.AntiCheats.Count > 0 ? string.Join(", ", _sys.AntiCheats) : "aucun")}");
            sb.AppendLine();

            sb.AppendLine("--- Diagnostic ---");
            foreach (var c in _lastChecks)
            {
                sb.AppendLine($"[{c.Severity}] {c.Title} : {c.Value}");
                sb.AppendLine($"    {c.Detail}");
                if (!string.IsNullOrWhiteSpace(c.Advice)) sb.AppendLine($"    → {c.Advice}");
            }
            sb.AppendLine();

            sb.AppendLine("--- Modifications appliquées ---");
            if (_journal.TotalRecords == 0) sb.AppendLine("(aucune)");
            foreach (var s in _journal.Sessions)
                foreach (var r in s.Records)
                    sb.AppendLine($"{r.When:yyyy-MM-dd HH:mm}  {r.Description}  |  {r.Hive}\\{r.SubKey}\\{r.ValueName}  |  " +
                                  (r.Existed ? $"{r.PreviousValue} -> {r.NewValue}" : $"(absent) -> {r.NewValue}"));

            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"aeropeek-diagnostic-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            MessageBox.Show("Diagnostic exporté sur le Bureau :\n\n" + path, "Aeropeek");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export impossible : " + ex.Message, "Aeropeek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Restauration ----------

    bool _restoreLoaded;
    List<RestorePoint> _points = new();

    async void RestoreReload_Click(object sender, RoutedEventArgs e) => await LoadRestore();

    /// <summary>
    /// Affiche ou masque un volet en fondu. Sans cela la bascule est un saut sec,
    /// et on ne voit pas que le contenu a changé.
    /// </summary>
    static void Reveal(UIElement pane, bool show)
    {
        if (!show) { pane.Visibility = Visibility.Collapsed; return; }

        pane.Visibility = Visibility.Visible;
        pane.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            });
    }

    /// <summary>
    /// Bascule entre les deux volets. Ils répondent à la même question — revenir
    /// en arrière — mais pas à la même échelle : un point système ramène toute la
    /// machine, le journal défait un réglage précis.
    /// </summary>
    void RestoreTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanePoints == null) return;   // pendant la construction de la vue

        bool points = (sender as RadioButton)?.Tag?.ToString() == "points";
        Reveal(PanePoints, points);
        Reveal(PaneJournal, !points);

        RestoreSub.Text = points
            ? "Les points de restauration de Windows. Ils ramènent toute la machine à un état passé — pilotes, logiciels, registre — et demandent un redémarrage."
            : "Tout ce qu'Aeropeek a modifié, avec l'état exact d'avant. Chaque ligne se défait seule, sans toucher au reste de la machine.";

        if (!points) RefreshJournal();
    }

    /// <summary>
    /// Interroger la restauration système passe par WMI : une à trois secondes
    /// selon le nombre de points. À l'ouverture de l'onglet, cela bloquait la
    /// fenêtre le temps de la réponse.
    /// </summary>
    async Task LoadRestore()
    {
        _restoreLoaded = true;
        RestoreTab_Checked(TabPoints.IsChecked == true ? TabPoints : TabJournal, null!);

        Veil(RestoreVeil, true);
        var (state, points) = await Task.Run(() =>
        {
            var st = Restore.Read();
            return (st, st.Enabled ? Restore.List() : new List<RestorePoint>());
        });
        Veil(RestoreVeil, false);

        _points = points;

        RestoreState.Text = !state.Available ? "Indisponible"
                          : !state.Enabled ? "Protection désactivée"
                          : _points.Count == 0 ? "Protection active, aucun point"
                          : _points.Count == 1 ? "Protection active, 1 point"
                          : $"Protection active, {_points.Count} points";

        RestoreDetail.Text = state.Problem
            ?? $"Windows réserve {state.DiskPercent} % du disque aux points de restauration et supprime "
             + "les plus anciens quand cette part est atteinte.";

        BtnRestoreCreate.IsEnabled = state.Enabled;

        // Deux obstacles courants, chacun avec son bouton plutôt qu'un message
        // qui laisse l'utilisateur chercher où cliquer.
        if (state.Available && !state.Enabled)
        {
            RestoreFix.Visibility = Visibility.Visible;
            RestoreFixText.Text = "La protection du système est coupée : aucun point ne peut être créé "
                                + "ni restauré tant qu'elle le reste.";
            BtnRestoreFix.Content = "Activer la protection";
            BtnRestoreFix.Tag = "activer";
        }
        else if (state.Enabled && state.FrequencyMin > 0)
        {
            RestoreFix.Visibility = Visibility.Visible;
            RestoreFixText.Text = $"Windows refuse un second point avant {state.FrequencyMin / 60} heures. "
                                + "Créer un point juste avant un réglage échouera silencieusement.";
            BtnRestoreFix.Content = "Lever la limite";
            BtnRestoreFix.Tag = "frequence";
        }
        else RestoreFix.Visibility = Visibility.Collapsed;

        RestoreList.ItemsSource = _points.Select(pt => new RestoreVm { Model = pt }).ToList();
        RestoreHeader.Text = _points.Count == 0
            ? "POINTS DISPONIBLES"
            : $"POINTS DISPONIBLES — {_points.Count}";
        DeleteAllLink.Visibility = _points.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RestoreEmpty.Visibility = _points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreEmpty.Text = !state.Enabled
            ? "Active la protection ci-dessus pour commencer à créer des points."
            : "Aucun point pour l'instant. Crées-en un maintenant : c'est le meilleur moment, "
            + "pendant que la machine fonctionne comme tu le veux.";
    }

    async void RestoreFix_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as Button)?.Tag?.ToString() == "activer")
            {
                Restore.Enable();
                MessageBox.Show("La protection du système est activée sur C:.", "Restauration");
            }
            else
            {
                _journal.Record(Restore.AllowFrequentPoints());
                RefreshJournal();
                MessageBox.Show(
                    "Tu peux maintenant créer autant de points que tu veux dans la journée."
                    + Environment.NewLine + Environment.NewLine
                    + "Ce changement figure au journal et s'annule comme les autres réglages.",
                    "Restauration");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Restauration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await LoadRestore();
    }

    async void RestoreCreate_Click(object sender, RoutedEventArgs e)
    {
        BtnRestoreCreate.IsEnabled = false;
        string label = $"Aeropeek — {DateTime.Now:dd/MM/yyyy HH'h'mm}";
        try
        {
            // La création prend plusieurs secondes : hors du fil d'interface.
            await Task.Run(() => Restore.Create(label));
            MessageBox.Show($"Point de restauration créé : « {label} ».", "Restauration");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Restauration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { BtnRestoreCreate.IsEnabled = true; }

        await LoadRestore();
    }

    async void RestoreDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RestoreVm vm) return;

        var answer = MessageBox.Show(
            $"Supprimer le point « {vm.Description} » du {vm.When} ?" + Environment.NewLine + Environment.NewLine
            + "Cette suppression est définitive : tu ne pourras plus revenir à cet état.",
            "Supprimer un point", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (!Restore.Delete(vm.Model.Sequence))
            MessageBox.Show("Windows a refusé la suppression de ce point.",
                            "Restauration", MessageBoxButton.OK, MessageBoxImage.Warning);

        await LoadRestore();
    }

    async void RestoreDeleteAll_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_points.Count == 0) return;

        var answer = MessageBox.Show(
            $"Supprimer les {_points.Count} points de restauration ?" + Environment.NewLine + Environment.NewLine
            + "La machine n'aura plus aucun point de retour, y compris ceux créés par Windows "
            + "avant les mises à jour. C'est définitif.",
            "Tout supprimer", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        Veil(RestoreVeil, true);
        RestoreVeil.Content = "Suppression des points…";
        var (deleted, failed) = await Task.Run(() => Restore.DeleteAll(_points));
        RestoreVeil.Content = "Lecture des points de restauration…";
        Veil(RestoreVeil, false);

        MessageBox.Show(
            failed == 0 ? $"{deleted} point(s) supprimé(s)."
                        : $"{deleted} supprimé(s), {failed} refusé(s) par Windows.",
            "Restauration", MessageBoxButton.OK,
            failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

        await LoadRestore();
    }

    void RestoreApply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RestoreVm vm) return;

        // La seule action de l'application qui touche la machine entière.
        var answer = MessageBox.Show(
            $"Restaurer le système à l'état du {vm.When} ?" + Environment.NewLine + Environment.NewLine
            + "Windows remettra les pilotes, les programmes et le registre dans l'état qu'ils avaient "
            + "à ce moment-là. Les logiciels installés depuis seront perdus ; tes documents ne sont pas touchés."
            + Environment.NewLine + Environment.NewLine
            + "Rien ne se passe avant le redémarrage : la restauration a lieu pendant celui-ci.",
            "Restaurer le système", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            Restore.Apply(vm.Model.Sequence);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Restauration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var reboot = MessageBox.Show(
            "La restauration est programmée. Elle s'effectuera au prochain démarrage."
            + Environment.NewLine + Environment.NewLine
            + "Redémarrer maintenant ?",
            "Restauration", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (reboot == MessageBoxResult.Yes) Restore.Reboot();
    }

    // ---------- navigation ----------

    // ---------- Accueil ----------

    /// <summary>
    /// Montre la présentation. Au premier lancement elle est déjà là ; ensuite
    /// elle arrive en fondu, puisqu'on vient de l'appeler.
    /// </summary>
    void ShowWelcome(bool animate)
    {
        Welcome.Visibility = Visibility.Visible;
        if (!animate) { Welcome.Opacity = 1; return; }

        Welcome.Opacity = 0;
        Welcome.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    void Brand_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowWelcome(animate: true);

    void WelcomeStart_Click(object sender, RoutedEventArgs e)
    {
        // La présentation n'a pas à revenir toute seule une fois lue.
        if (!Settings.Current.WelcomeSeen)
        {
            Settings.Current.WelcomeSeen = true;
            Settings.Current.Save();
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(
            Welcome.Opacity, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => Welcome.Visibility = Visibility.Collapsed;
        Welcome.BeginAnimation(OpacityProperty, fade);

        NavDiag.IsChecked = true;
        if (BtnScan.IsEnabled) Scan_Click(BtnScan, new RoutedEventArgs());
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewDiag == null) return;   // pendant l'initialisation
        var tag = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString();
        ShowView(tag switch
        {
            "1" => ViewTweaks,
            "3" => ViewBench,
            "4" => ViewMatch,
            "5" => ViewRestore,
            "6" => ViewDns,
            "7" => ViewClean,
            "8" => ViewUtils,
            _ => ViewDiag
        });

        // Volontairement non attendu : la navigation ne doit pas attendre la
        // lecture WMI, le voile s'en charge.
        if (tag == "5" && !_restoreLoaded) _ = LoadRestore();
    }

    Grid? _currentView;

    /// <summary>
    /// Bascule d'onglet. L'écran sortant s'efface vers la gauche pendant que le
    /// suivant arrive de la droite : le mouvement suit le sens de la lecture, comme
    /// sur un téléphone, plutôt qu'un simple fondu.
    /// </summary>
    void ShowView(Grid target)
    {
        if (ReferenceEquals(_currentView, target)) return;

        var views = new[] { ViewDiag, ViewTweaks, ViewClean, ViewMatch, ViewRestore, ViewDns, ViewBench, ViewUtils };

        // Un dépassement unique, et non un ressort. ElasticEase oscillait —
        // il passait la cible, revenait, la repassait : de l'élasticité, mais
        // nerveuse. BackEase à faible amplitude dépasse une seule fois et se
        // pose. L'écran garde sa détente sans jamais osciller.
        var spring = new System.Windows.Media.Animation.BackEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            Amplitude = 0.3
        };
        var soft = new System.Windows.Media.Animation.QuadraticEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
        };

        // ---- sortie : l'ancien écran s'efface vers le haut, et vite.
        if (_currentView is { } old)
        {
            var leaving = old;
            var outSlide = new TranslateTransform();
            leaving.RenderTransformOrigin = new Point(0.5, 1);
            leaving.RenderTransform = outSlide;
            Cache(leaving, true);

            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
                1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = soft };
            fadeOut.Completed += (_, _) =>
            {
                Cache(leaving, false);
                if (!ReferenceEquals(_currentView, leaving)) leaving.Visibility = Visibility.Collapsed;
            };
            leaving.BeginAnimation(OpacityProperty, fadeOut);
            outSlide.BeginAnimation(TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, -10,
                    TimeSpan.FromMilliseconds(170)) { EasingFunction = soft });
        }

        foreach (var v in views)
            if (!ReferenceEquals(v, target) && !ReferenceEquals(v, _currentView))
                v.Visibility = Visibility.Collapsed;

        _currentView = target;

        // ---- entrée : l'écran monte depuis le bas ET s'étire en montant.
        // Les deux animations partagent le même ressort et la même durée : c'est
        // ce qui les fait lire comme une seule matière qui se détend, et non
        // comme un déplacement auquel on aurait ajouté un redimensionnement.
        // Le point d'ancrage est en bas au centre, sinon l'étirement partirait
        // du milieu de l'écran et le mouvement perdrait sa direction.
        var rise = new TranslateTransform(0, 30);
        var stretch = new ScaleTransform(1, 0.975);
        var group = new TransformGroup();
        group.Children.Add(stretch);
        group.Children.Add(rise);

        target.RenderTransformOrigin = new Point(0.5, 1);
        target.RenderTransform = group;
        target.Opacity = 0;
        target.Visibility = Visibility.Visible;
        Cache(target, true);

        const double ms = 360;

        var up = new System.Windows.Media.Animation.DoubleAnimation(
            30, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = spring };
        up.Completed += (_, _) => { if (ReferenceEquals(_currentView, target)) Cache(target, false); };
        rise.BeginAnimation(TranslateTransform.YProperty, up);

        stretch.BeginAnimation(ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.975, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = spring });

        // Le fondu accompagne le mouvement au lieu de le devancer. A 190 ms sur
        // 430, l'écran finissait sa course déjà pleinement opaque : on voyait
        // alors un objet solide encore en train de bouger, et c'est exactement
        // ce qui se lisait comme de la brusquerie. Il termine maintenant presque
        // avec lui, sans rebondir pour autant.
        target.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = soft });
    }

    /// <summary>
    /// Fige un écran en texture le temps de son animation.
    /// <para>
    /// C'est LA raison pour laquelle les transitions saccadaient. Animer
    /// l'opacité d'un sous-arbre entier oblige WPF à le redessiner dans une
    /// surface intermédiaire à chaque image — et ces écrans portent des listes
    /// de plusieurs dizaines de lignes. Mis en cache, le sous-arbre est dessiné
    /// UNE fois, puis la carte graphique se contente de déplacer, étirer et
    /// fondre cette texture.
    /// </para>
    /// <para>
    /// On le retire à la fin : gardé, le texte resterait légèrement adouci,
    /// puisqu'il ne serait plus rendu à la résolution réelle de l'écran.
    /// </para>
    /// </summary>
    static void Cache(UIElement e, bool on) =>
        e.CacheMode = on ? new BitmapCache { EnableClearType = false } : null;
}

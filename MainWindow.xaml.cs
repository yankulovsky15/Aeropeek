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
    public string Description => Model.Description.Length > 0 ? Model.Description : "(unnamed)";
    public string Type => Model.TypeLabel;
    public string When => Model.Created == DateTime.MinValue
        ? $"number {Model.Sequence}"
        : $"{Model.Created:dddd d MMMM yyyy 'at' HH:mm} · number {Model.Sequence}";
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
        BtnBurger.ToolTip = CompactNav ? "Show the labels" : "Collapse the menu";

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

        DiagSummary.Text = "Reading your configuration…";
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
        SysGpu.Text = _sys.GpuNames.Count > 0 ? Trim(_sys.GpuNames[0], 38) : "Unknown graphics card";
        SysOs.Text = $"{_sys.OsName} · {_sys.OsDisplayVersion} ({_sys.OsBuild})";
        SysAc.Text = _sys.AntiCheats.Count > 0
            ? "Anti-cheat: " + string.Join(", ", _sys.AntiCheats)
            : "No anti-cheat detected";
    }

    static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? "—" : (s.Length <= n ? s : s[..(n - 1)] + "…");

    void RecoverIfCrashed()
    {
        var open = _journal.UnclosedSessions().Where(s => s.Records.Count > 0).ToList();
        if (open.Count == 0) return;

        int n = open.Sum(s => s.Records.Count);
        var answer = MessageBox.Show(
            $"Aeropeek closed without finishing a previous session cleanly.\n\n" +
            $"{n} change(s) are still applied but unconfirmed.\n\n" +
            "Do you want to undo them and return to the original state?",
            "Interrupted session", MessageBoxButton.YesNo, MessageBoxImage.Question);

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
        DiagSummary.Text = "Scanning…";
        Veil(DiagVeil, true);
        try
        {
            var sys = _sys;
            _lastChecks = await Task.Run(() => Diagnostics.RunAll(sys));

            ChecksList.ItemsSource = _lastChecks.Select(c =>
            {
                var (fg, bg, label) = c.Severity switch
                {
                    Severity.Probleme => (BadFg, BadBg, "Needs fixing"),
                    Severity.Warn => (WarnFg, WarnBg, "Worth checking"),
                    Severity.Ok => (OkFg, OkBg, "Correct"),
                    Severity.Info => (InfoFg, InfoBg, "Information"),
                    _ => (MuteFg, MuteBg, "Undetermined")
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
                ? $"{_lastChecks.Count} checks, nothing to fix. Your machine is well set up."
                : $"{_lastChecks.Count} checks · {problems} to fix · {warns} to look at";
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
                    ? "CS2 is now pinned to the performance cores.\n\n" +
                      "Affinity belongs to the process: it disappears when you close the game. " +
                      "Nothing was written to your system, so there is nothing to undo."
                    : "CS2 no longer seems to be running.", "Aeropeek");
                break;
            }
            case "affinite-tous":
            {
                int n = GameAffinity.Apply(GameAffinity.AllCores(_sys.Cpu.LogicalCores));
                MessageBox.Show(n > 0
                    ? "CS2 can use every core again."
                    : "CS2 no longer seems to be running.", "Aeropeek");
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
                DiagSummary.Text = "Querying NVIDIA…";
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
                        "Power management switched to “Prefer maximum performance”." +
                        Environment.NewLine + Environment.NewLine +
                        "The previous value is in the journal. Run the scan again to see it applied.",
                        "NVIDIA driver");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;

            case "ouvrir-nvidia":
                if (!Nvidia.OpenControlPanel())
                    MessageBox.Show(
                        "The NVIDIA Control Panel was not found." + Environment.NewLine + Environment.NewLine +
                        "It also opens from a right-click on the desktop, or from the Microsoft Store " +
                        "if it isn't installed.",
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
                    MessageBox.Show("Open Windows Security → Device security → Core isolation.", "Aeropeek");
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
                Verdict.DejaApplique => (AccFg, AccBg, "Applied"),
                Verdict.NonPertinent => (MuteFg, MuteBg, "Not applicable"),
                Verdict.Bloque => (BadFg, BadBg, "Blocked"),
                _ => t.Category switch
                {
                    "network" => (InfoFg, InfoBg, "Network"),
                    "privacy" => (MuteFg, MuteBg, "Privacy"),
                    _ => (WarnFg, WarnBg, "Performance")
                }
            };

            var note = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(t.Gain)) note.Append("Expected gain: ").Append(t.Gain);
            if (!string.IsNullOrWhiteSpace(t.Consequence))
            {
                if (note.Length > 0) note.Append("  ·  ");
                note.Append("What you give up: ").Append(t.Consequence);
            }
            if (t.NeedsRestart)
            {
                if (note.Length > 0) note.Append("  ·  ");
                note.Append("Restart required");
            }
            if (app.Verdict is Verdict.NonPertinent or Verdict.Bloque && !string.IsNullOrEmpty(app.Reason))
                note.Clear().Append(app.Reason);

            // Une mesure réelle prime sur le gain annoncé par le catalogue.
            var measured = _attributions.For(t.Id);
            string measuredText = "";
            Brush mFg = MuteFg, mBg = MuteBg;
            if (measured != null)
            {
                measuredText = measured.Verdict + $" · on {measured.When:MM-dd}";
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
                    Verdict.DejaApplique => "Applied",
                    Verdict.NonPertinent => "Not applicable",
                    Verdict.Bloque => "Blocked",
                    _ => "Not applied"
                },
                ActionLabel = app.Verdict == Verdict.DejaApplique ? "Undo this tweak" : "Apply this tweak",
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
                        "This tweak was already in place before Aeropeek ever saw it, so there is nothing to restore: " +
                        "the application never recorded its original state.\n\n" +
                        "Aeropeek only undoes what it changed itself.",
                        "Nothing to undo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                vm.Model.Apply(_journal);
                if (vm.Model.NeedsRestart)
                    MessageBox.Show("This tweak takes effect after Windows restarts.",
                        "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Operation refused: " + ex.Message, "Aeropeek",
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
                        u.Caution + "\n\nRestart the graphics driver now?",
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
                        $"{u.Title} will be downloaded from {u.Display}, then run with the "
                        + "droits administrateur." + Environment.NewLine + Environment.NewLine
                        + (u.Signer != null
                            ? $"Its signature is checked before it runs: without a valid signature from "
                              + $"{u.Signer}, nothing will be launched."
                            : "This file is not signed by its author: Aeropeek can only verify "
                              + "where it was downloaded from, not what it contains.")
                        + Environment.NewLine + Environment.NewLine
                        + "What this tool changes will not enter Aeropeek's journal and "
                        + "cannot be undone from this application." + Environment.NewLine + Environment.NewLine
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
            Kind = a.Wireless ? "Wireless" : "Filaire",
            Icon = Geometry.Parse(a.Wireless ? IconWifi : IconEthernet),
            Servers = a.DnsText,
            Source = a.FromDhcp ? "handed out by the router" : "set manually"
        }).ToList();

        DnsSummary.Text = _adapters.Count switch
        {
            0 => "No active network connection.",
            1 => $"Connection “{_adapters[0].Name}” · current servers: {_adapters[0].DnsText}",
            _ => $"{_adapters.Count} active connections · changes apply to “{_adapters[0].Name}”"
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
                Addresses = p.IsDhcp ? "assigned automatically" : $"{p.Primary} · {p.Secondary}",
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
        DnsSummary.Text = "Testing the servers…";
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
                ? $"Fastest from where you are: {best.Key} at {best.Value} ms. "
                  + "A few milliseconds of difference only shows up in name resolution."
                : "No server answered. Some networks block ICMP ping without blocking DNS.";
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
            CustomDnsHint.Text = "The primary server is not a valid IPv4 address.";
            CustomDnsHint.Foreground = BadFg;
            return;
        }
        if (secondary.Length > 0 && !IsIPv4(secondary))
        {
            CustomDnsHint.Text = "The secondary server is not a valid IPv4 address.";
            CustomDnsHint.Foreground = BadFg;
            return;
        }

        var adapter = _adapters.FirstOrDefault();
        if (adapter == null) return;

        try
        {
            var custom = new DnsProvider("Custom", primary, secondary,
                "", Array.Empty<string>(), "#B98CF7", "");
            _journal.Record(DnsOps.Apply(adapter, custom));

            CustomDnsHint.Text = "Applied. Reversible from the Journal.";
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
        CleanSummary.Text = "Scanning folders…";
        CleanScanVeil.Content = "Scanning folders…";
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
            CleanSummary.Text = $"{CleanScan.Human(total)} recoverable in total · "
                              + $"{_cleanScans.Count(s => s.Available)} locations scanned";
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
            .OrderByDescending(s => s.Target.Group == "Game-related")
            .ThenByDescending(s => s.Bytes)
            .Select(s =>
            {
                var (fg, bg, label) = s.Target.Risk switch
                {
                    CleanRisk.NoReturn => (BadFg, BadBg, "No way back"),
                    CleanRisk.Check => (WarnFg, WarnBg, "Worth checking"),
                    _ => (MuteFg, MuteBg, "")
                };

                string desc = s.Available
                    ? s.Target.Description
                    : "This location does not exist on your machine.";

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

        BtnCleanAll.Content = allOn ? "Select all" : "Deselect all";
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
            $"Delete {CleanScan.Human(total)} spread over {chosen.Count} location(s)?\n\n"
            + (risky ? "Your selection contains items marked “Worth checking” or “No way back”.\n\n" : "")
            + "This deletion is permanent: it cannot be undone.",
            "Confirm the cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        BtnCleanRun.IsEnabled = false;
        BtnCleanScan.IsEnabled = false;
        CleanSummary.Text = "Deleting…";

        // Supprimer plusieurs gigaoctets prend du temps : le même voile que
        // l'analyse, avec l'emplacement en cours de traitement.
        CleanScanVeil.Content = "Deleting…";
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
            $"{CleanScan.Human(freed)} freed.\n\n{deleted} file(s) deleted"
            + (skipped > 0 ? $", {skipped} skipped because they were in use." : "."),
            "Cleanup finished", MessageBoxButton.OK, MessageBoxImage.Information);
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
                ? $"Using processor time right now. {a.Instances} processes."
                : $"{a.Instances} processes, idle. Closing it frees memory, not processor time.",
            Memory = a.MemoryText,
            Cpu = a.Active ? $"{a.CpuPercent:0.0} % processeur" : "idle",
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
            ? "Hide the services with no effect"
            : $"Show the {idle} services with no effect on the game";
        IdleLink.Visibility = idle > 0 ? Visibility.Visible : Visibility.Collapsed;

        ServicesList.ItemsSource = shown.Select(s =>
        {
            string activity;
            Brush fg;
            if (!s.Running) { activity = "stopped"; fg = MuteFg; }
            else if (s.Active)
            {
                activity = s.IoKoPerSec >= 200
                    ? $"{s.IoKoPerSec / 1024:0.0} Mo/s"
                    : $"{s.CpuPercent:0.0} % processeur";
                fg = WarnFg;
            }
            else { activity = "idle"; fg = MuteFg; }

            bool reel = s.Impact == ServiceImpact.Reel;

            // « Rien mesuré » ne veut pas dire « rien à gagner » : ces services
            // travaillent par à-coups, et cinq secondes ne les attrapent pas.
            string note = s.Note;
            if (reel && !s.Active) note += " Nothing measured right now, but it can wake up mid-match.";

            return new ServiceVm
            {
                Model = s, Name = s.Name, Label = s.Label,
                Note = note,
                Activity = activity, ActivityFg = fg,
                Impact = reel ? "Can interfere" : "No gain",
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
        BtnSelectAll.Content = allOn ? "Untick all" : "Tick all";
        BtnSelectAll.IsEnabled = suspendables.Count > 0;

        ServicesHeader.Text = suspendables.Count == 0
            ? "SERVICES WINDOWS"
            : $"WINDOWS SERVICES — {_selectedServices.Count} OF {suspendables.Count} TICKED";

        RefreshMatchHero();
    }

    /// <summary>Déplie ou replie la séquence d'activation.</summary>
    void How_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool open = HowCard.Visibility != Visibility.Visible;
        HowCard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HowLink.Text = open ? "Masquer" : "How does it work?";
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
        BtnSelectApps.Content = allOn ? "Untick all" : "Tick all";
        BtnSelectApps.IsEnabled = _apps.Count > 0;

        AppsHeader.Text = _apps.Count == 0
            ? "BACKGROUND APPLICATIONS"
            : $"BACKGROUND APPLICATIONS — {_selectedApps.Count} OF {_apps.Count} TICKED";
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
            if (_match.ClosedApps > 0) done.Add($"{_match.ClosedApps} application(s) closed");
            if (_match.SuspendedServices > 0) done.Add($"{_match.SuspendedServices} service(s) suspendu(s)");

            MatchDetail.Text = (done.Count > 0 ? string.Join(" · ", done) + "." : "Nothing could be suspended.")
                             + " Flip the switch to put everything back.";
            MatchHero.BorderBrush = AccFg;
            SwMatch.IsEnabled = true;
        }
        else
        {
            MatchState.Text = "Idle";

            var todo = new List<string>();
            if (apps > 0) todo.Add($"{apps} application(s) closed");
            if (services > 0) todo.Add($"{services} service(s) suspendu(s)");
            if (priority) todo.Add("cs2.exe at high priority");

            bool ready = todo.Count > 0;
            MatchTitle.Text = ready ? "Ready to turn on" : "Nothing selected";

            MatchDetail.Text = !ready
                ? "Tick at least one application, one service, or the priority option below."
                : "When turned on: " + string.Join(" · ", todo) + "."
                  + (ChkAuto?.IsChecked == true
                        ? " It will trigger on its own when CS2 starts."
                        : " Use the switch, or turn on the automatic trigger.");

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
        BtnSelectAll.Content = allOn ? "Untick all" : "Tick all";
        ServicesHeader.Text = suspendables.Count == 0
            ? "SERVICES WINDOWS"
            : $"WINDOWS SERVICES — {_selectedServices.Count} OF {suspendables.Count} TICKED";

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
        MatchVeil.Content = stopping ? "Restoring…" : "Applying…";
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
                MatchDetail.Text = $"{n} item(s) restored.";
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
                        "Nothing could be applied: the selected services were not running, "
                        + "neither were the applications, and CS2 isn't running for the priority to apply.",
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
            MessageBox.Show("PresentMon cannot be found next to the application.", "Aeropeek",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Benchmark.IsRunning("cs2"))
        {
            MessageBox.Show(
                "CS2 isn't running.\n\nStart the game, get into a match — not a menu — then start the capture again.",
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
                ? $"Get back in the game — recording in {inDelay} s"
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
            BenchStatus.Text = $"Done — {run.Frames} frames measured.";
            BenchLabel.Text = "";
            RefreshBench();

            if (awaySeconds >= 3)
                MessageBox.Show(
                    $"CS2 was not in the foreground for {awaySeconds} s of the recording.\n\n" +
                    "Frames produced in the background mostly distort the 1% and 0.1% lows. " +
                    "Run it again and stay in the game until the end.",
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
            ? $"{newest.Series.Count} points over {newest.DurationS:0} s. Each point keeps the slowest frame of its interval."
            : $"Two runs overlaid. Each point keeps the slowest frame of its interval, "
              + "so that spikes stay visible despite the resampling.";
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
                Footnote = $"{r.Frames} frames over {r.DurationS:0} s · median frame {r.MedianMs:0.00} ms · "
                           + "a stutter = a frame more than twice as slow as the median"
            });
        }

        BenchList.ItemsSource = rows;
        DrawChart();
        if (rows.Count == 0)
            BenchStatus.Text = "No runs yet. Start CS2, get into a match, then capture.";
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
        "files-deleted" => $"{r.NewValue} · {r.PreviousValue} freed — permanent deletion",
        "service" => r.WasRunning
            ? "was running, stopped — undoing will start it again"
            : "was already stopped, nothing to restore",
        _ => r.Existed
            ? $"was “{r.PreviousValue}”, set to “{r.NewValue}”"
            : $"did not exist, created as “{r.NewValue}” — undoing will delete it"
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
                if (g.Count() > 1) change += $" · {g.Count()} writes";

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
            ? "No changes. Aeropeek has altered nothing on this machine."
            : fixes == rows.Count
                ? $"{rows.Count} change(s), all reversible."
                : $"{rows.Count} change(s), {rows.Count - fixes} of them permanent.";
    }

    void JournalUndo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not JournalVm vm) return;

        var answer = MessageBox.Show(
            $"Undo “{vm.Title}”?" + Environment.NewLine + Environment.NewLine
            + "The exact state that came before will be restored"
            + (vm.Ids.Count > 1 ? $", for all {vm.Ids.Count} writes of this tweak." : ".")
            + " Nothing else is touched.",
            "Undo a change", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        int done = _journal.RevertRecords(vm.Ids);

        if (done < vm.Ids.Count)
            MessageBox.Show(
                done == 0
                    ? "Nothing could be undone: Windows refused the restore."
                    : $"{done} of {vm.Ids.Count} write(s) restored. The rest were refused.",
                "Annulation", MessageBoxButton.OK, MessageBoxImage.Warning);

        RefreshJournal();
        RefreshTweaks();
    }

    void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        if (_journal.TotalRecords == 0)
        {
            MessageBox.Show("There is nothing to undo.", "Aeropeek");
            return;
        }

        var answer = MessageBox.Show(
            $"Undo the {_journal.TotalRecords} change(s) Aeropeek applied?\n\n" +
            "Every value returns to exactly the state it had before — including not existing at all.",
            "Undo everything", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        int n = _journal.RevertEverything();
        RefreshTweaks();
        RefreshJournal();
        MessageBox.Show($"{n} change(s) undone.", "Aeropeek");
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Aeropeek — diagnostics export ===");
            sb.AppendLine($"Date: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("--- Machine ---");
            sb.AppendLine($"OS       : {_sys.OsName} {_sys.OsEdition} {_sys.OsDisplayVersion} (build {_sys.OsBuild})");
            sb.AppendLine($"Chassis : {(_sys.IsLaptop ? "portable" : "poste fixe")}");
            sb.AppendLine($"CPU      : {_sys.CpuName}");
            sb.AppendLine($"Cores   : {_sys.Cpu.PhysicalCores} physical / {_sys.Cpu.LogicalCores} logical" +
                          (_sys.Cpu.IsHybrid ? $" ({_sys.Cpu.PerformanceCores} P + {_sys.Cpu.EfficiencyCores} E)" : ""));
            foreach (var g in _sys.GpuNames) sb.AppendLine($"GPU      : {g}");
            foreach (var m in _sys.Memory)
                sb.AppendLine($"Memory  : {m.Kind} {m.CapacityBytes / 1024 / 1024 / 1024} GB — configured {m.ConfiguredMhz} MT/s, rated {m.RatedMhz} MT/s");
            foreach (var d in _sys.Displays)
                sb.AppendLine($"Display : {d.MonitorName} {d.Width}x{d.Height} @ {d.RefreshHz} Hz via {d.AdapterName}");
            sb.AppendLine($"Anticheat: {(_sys.AntiCheats.Count > 0 ? string.Join(", ", _sys.AntiCheats) : "none")}");
            sb.AppendLine();

            sb.AppendLine("--- Diagnostic ---");
            foreach (var c in _lastChecks)
            {
                sb.AppendLine($"[{c.Severity}] {c.Title}: {c.Value}");
                sb.AppendLine($"    {c.Detail}");
                if (!string.IsNullOrWhiteSpace(c.Advice)) sb.AppendLine($"    → {c.Advice}");
            }
            sb.AppendLine();

            sb.AppendLine("--- Changes applied ---");
            if (_journal.TotalRecords == 0) sb.AppendLine("(none)");
            foreach (var s in _journal.Sessions)
                foreach (var r in s.Records)
                    sb.AppendLine($"{r.When:yyyy-MM-dd HH:mm}  {r.Description}  |  {r.Hive}\\{r.SubKey}\\{r.ValueName}  |  " +
                                  (r.Existed ? $"{r.PreviousValue} -> {r.NewValue}" : $"(absent) -> {r.NewValue}"));

            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"aeropeek-diagnostic-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            MessageBox.Show("Diagnostics exported to the Desktop:\n\n" + path, "Aeropeek");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "Aeropeek",
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
            ? "Windows restore points. They take the whole machine back to a past state — drivers, software, registry — and require a restart."
            : "Everything Aeropeek changed, with the exact prior state. Each line undoes on its own, without touching the rest of the machine.";

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
                          : !state.Enabled ? "Protection disabled"
                          : _points.Count == 0 ? "Protection on, no points"
                          : _points.Count == 1 ? "Protection active, 1 point"
                          : $"Protection active, {_points.Count} points";

        RestoreDetail.Text = state.Problem
            ?? $"Windows reserves {state.DiskPercent}% of the disk for restore points and deletes "
             + "the oldest ones once that share is reached.";

        BtnRestoreCreate.IsEnabled = state.Enabled;

        // Deux obstacles courants, chacun avec son bouton plutôt qu'un message
        // qui laisse l'utilisateur chercher où cliquer.
        if (state.Available && !state.Enabled)
        {
            RestoreFix.Visibility = Visibility.Visible;
            RestoreFixText.Text = "System protection is switched off: no point can be created "
                                + "or restored while it stays that way.";
            BtnRestoreFix.Content = "Enable protection";
            BtnRestoreFix.Tag = "activer";
        }
        else if (state.Enabled && state.FrequencyMin > 0)
        {
            RestoreFix.Visibility = Visibility.Visible;
            RestoreFixText.Text = $"Windows refuses a second point within {state.FrequencyMin / 60} hours. "
                                + "Creating a point just before a tweak will fail silently.";
            BtnRestoreFix.Content = "Lift the limit";
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
            ? "Turn on the protection above to start creating points."
            : "No points yet. Create one now — this is the best moment, "
            + "while the machine works the way you want it to.";
    }

    async void RestoreFix_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as Button)?.Tag?.ToString() == "activer")
            {
                Restore.Enable();
                MessageBox.Show("System protection is enabled on the system drive.", "Restauration");
            }
            else
            {
                _journal.Record(Restore.AllowFrequentPoints());
                RefreshJournal();
                MessageBox.Show(
                    "You can now create as many points as you like in a single day."
                    + Environment.NewLine + Environment.NewLine
                    + "This change is in the journal and undoes like any other tweak.",
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
        string label = $"Aeropeek — {DateTime.Now:yyyy-MM-dd HH:mm}";
        try
        {
            // La création prend plusieurs secondes : hors du fil d'interface.
            await Task.Run(() => Restore.Create(label));
            MessageBox.Show($"Restore point created: “{label}”.", "Restauration");
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
            $"Delete the point “{vm.Description}” from {vm.When}?" + Environment.NewLine + Environment.NewLine
            + "This deletion is permanent: you will not be able to return to that state.",
            "Delete a point", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (!Restore.Delete(vm.Model.Sequence))
            MessageBox.Show("Windows refused to delete this point.",
                            "Restauration", MessageBoxButton.OK, MessageBoxImage.Warning);

        await LoadRestore();
    }

    async void RestoreDeleteAll_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_points.Count == 0) return;

        var answer = MessageBox.Show(
            $"Delete all {_points.Count} restore points?" + Environment.NewLine + Environment.NewLine
            + "The machine will have no way back at all, including the points Windows created "
            + "before updates. This is permanent.",
            "Delete all", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        Veil(RestoreVeil, true);
        RestoreVeil.Content = "Deleting points…";
        var (deleted, failed) = await Task.Run(() => Restore.DeleteAll(_points));
        RestoreVeil.Content = "Reading restore points…";
        Veil(RestoreVeil, false);

        MessageBox.Show(
            failed == 0 ? $"{deleted} point(s) deleted."
                        : $"{deleted} deleted, {failed} refused by Windows.",
            "Restauration", MessageBoxButton.OK,
            failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

        await LoadRestore();
    }

    void RestoreApply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RestoreVm vm) return;

        // La seule action de l'application qui touche la machine entière.
        var answer = MessageBox.Show(
            $"Restore the system to its state on {vm.When}?" + Environment.NewLine + Environment.NewLine
            + "Windows will put drivers, programs and the registry back into the state they were in "
            + "at that moment. Software installed since will be lost; your documents are untouched."
            + Environment.NewLine + Environment.NewLine
            + "Nothing happens before the restart: the restore takes place during it.",
            "Restore the system", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
            "The restore is scheduled. It will run on the next boot."
            + Environment.NewLine + Environment.NewLine
            + "Restart now?",
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

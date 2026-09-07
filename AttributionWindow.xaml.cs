using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Aeropeek.Core;

namespace Aeropeek;

/// <summary>
/// Mesure l'effet réel d'un réglage : une capture sans, on applique, une capture
/// avec. Le protocole est imposé par l'outil pour que les deux mesures soient
/// comparables — c'est précisément ce qu'un utilisateur ne fait jamais à la main.
/// </summary>
public partial class AttributionWindow : Window
{
    const int CaptureSeconds = 60;

    readonly Tweak _tweak;
    readonly Journal _journal;
    readonly AttributionStore _store;

    BenchRun? _baseline;
    BenchRun? _tweaked;
    bool _running;
    bool _tweakApplied;          // état laissé à la fermeture
    int _phase;                  // 0 prêt, 1 mesure sans, 2 mesure avec, 3 terminé

    static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public bool Changed { get; private set; }

    public AttributionWindow(Tweak tweak, Journal journal, AttributionStore store)
    {
        InitializeComponent();
        _tweak = tweak;
        _journal = journal;
        _store = store;

        TweakName.Text = tweak.Name;
        Loaded += (_, _) => { PlayOpen(); Prepare(); };
    }

    void PlayOpen()
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        if (Content is FrameworkElement root && root.RenderTransform is ScaleTransform s)
        {
            s.ScaleX = s.ScaleY = 0.965;
            var grow = new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    void Prepare()
    {
        _tweakApplied = _tweak.IsApplied();

        if (_tweak.NeedsRestart)
        {
            StepText.Text = "Ce réglage ne peut pas être mesuré ainsi.";
            HintText.Text = "Il ne prend effet qu'après un redémarrage de Windows. Une comparaison "
                          + "dans la même session ne mesurerait rien. Il faut mesurer avant, redémarrer, "
                          + "puis mesurer à nouveau — l'onglet Benchmark s'en charge.";
            BtnPrimary.Content = "Fermer";
            BtnSecondary.Visibility = Visibility.Collapsed;
            _phase = 3;
            return;
        }

        if (!Benchmark.IsRunning("cs2"))
        {
            StepText.Text = "CS2 n'est pas lancé.";
            HintText.Text = "Lance le jeu et mets-toi en partie, puis rouvre cette fenêtre. "
                          + "Les deux mesures doivent se faire dans des conditions comparables.";
            BtnPrimary.Content = "Fermer";
            BtnSecondary.Visibility = Visibility.Collapsed;
            _phase = 3;
            return;
        }

        StepText.Text = "Deux mesures de 60 secondes, séparées par l'application du réglage.";
        HintText.Text = _tweakApplied
            ? "Le réglage est actuellement appliqué : il sera d'abord retiré pour établir la référence, "
              + "puis remis. Reste en jeu du début à la fin — environ deux minutes trente."
            : "Reste en jeu du début à la fin, sur la même carte et dans des conditions semblables. "
              + "Environ deux minutes trente au total.";
        FootNote.Text = "Rien n'est conservé si tu annules.";
    }

    // ---------- déroulé ----------

    async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_phase == 3) { Close(); return; }
        if (_running) return;

        _running = true;
        BtnPrimary.IsEnabled = false;
        BtnSecondary.IsEnabled = false;

        try
        {
            // référence : le réglage doit être absent
            if (_tweak.IsApplied())
            {
                StepText.Text = "Retrait temporaire du réglage…";
                HintText.Text = "Nécessaire pour établir la référence.";
                _journal.RevertTweak(_tweak.Id);
                Changed = true;
                await Task.Delay(1200);
            }

            _phase = 1;
            _baseline = await Capture("Mesure 1 sur 2 — sans le réglage");

            StepText.Text = "Application du réglage…";
            HintText.Text = "";
            _tweak.Apply(_journal);
            Changed = true;
            _tweakApplied = true;
            await Task.Delay(1500);

            _phase = 2;
            _tweaked = await Capture("Mesure 2 sur 2 — avec le réglage");

            ShowResult();
        }
        catch (OperationCanceledException)
        {
            StepText.Text = "Mesure interrompue.";
            HintText.Text = "";
            _phase = 3;
            BtnPrimary.Content = "Fermer";
        }
        catch (Exception ex)
        {
            StepText.Text = "La mesure a échoué.";
            HintText.Text = ex.Message;
            _phase = 3;
            BtnPrimary.Content = "Fermer";
        }
        finally
        {
            _running = false;
            BtnPrimary.IsEnabled = true;
            BtnSecondary.IsEnabled = true;
            Bar.Visibility = Visibility.Collapsed;
        }
    }

    async Task<BenchRun> Capture(string title)
    {
        int total = Benchmark.DelaySeconds + CaptureSeconds;
        int left = total;

        StepText.Text = title;
        Bar.Visibility = Visibility.Visible;

        void Tick()
        {
            int inDelay = left - CaptureSeconds;
            HintText.Text = inDelay > 0
                ? $"Retourne dans le jeu — l'enregistrement démarre dans {inDelay} s"
                : $"Enregistrement — {left} s restantes. Ne quitte pas le jeu.";
            BarFill.Width = Math.Max(0, Bar.ActualWidth * (total - left) / total);
        }
        Tick();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { left--; if (left >= 0) Tick(); };
        timer.Start();

        try { return await Benchmark.CaptureAsync("cs2", CaptureSeconds, _tweak.Name); }
        finally { timer.Stop(); }
    }

    void ShowResult()
    {
        var a = new Attribution
        {
            TweakId = _tweak.Id,
            Label = _tweak.Name,
            BaselineAvg = _baseline!.AvgFps,
            BaselineLow1 = _baseline.Low1Fps,
            TweakedAvg = _tweaked!.AvgFps,
            TweakedLow1 = _tweaked.Low1Fps,
            Frames = _baseline.Frames + _tweaked.Frames
        };
        _store.Add(a);

        BaseText.Text = $"{a.BaselineLow1:0} fps";
        TweakedText.Text = $"{a.TweakedLow1:0} fps";
        TweakedText.Foreground = a.Significant
            ? (a.DeltaLow1Pct > 0 ? B("#4FBF8B") : B("#F2726A"))
            : B("#E8EEF5");

        if (!a.Significant)
        {
            VerdictText.Text = "Aucun effet mesurable sur ta machine.";
            VerdictText.Foreground = B("#7C8FA4");
            VerdictNote.Text = $"L'écart est de {a.DeltaLow1Pct:+0.0;-0.0} %, sous le seuil de 2 % en dessous duquel "
                             + "deux parties consécutives varient déjà d'elles-mêmes. Ce n'est pas un échec de la mesure : "
                             + "c'est la réponse.";
        }
        else if (a.DeltaLow1Pct > 0)
        {
            VerdictText.Text = $"Gain réel : +{a.DeltaLow1Pct:0.0} % sur les 1% lows.";
            VerdictText.Foreground = B("#4FBF8B");
            VerdictNote.Text = $"FPS moyen : {a.BaselineAvg:0} → {a.TweakedAvg:0} ({a.DeltaAvgPct:+0.0;-0.0} %). "
                             + "Mesure conservée : la carte du réglage affichera désormais ce chiffre plutôt qu'une estimation.";
        }
        else
        {
            VerdictText.Text = $"Ce réglage te fait perdre {Math.Abs(a.DeltaLow1Pct):0.0} %.";
            VerdictText.Foreground = B("#F2726A");
            VerdictNote.Text = "Sur ta configuration, il vaut mieux ne pas l'appliquer. "
                             + "Le bouton ci-dessous le retire.";
        }

        ResultCard.Visibility = Visibility.Visible;
        StepText.Text = "Mesure terminée.";
        HintText.Text = $"{a.Frames} images analysées au total.";
        FootNote.Text = "";

        _phase = 3;
        BtnPrimary.Content = "Garder le réglage";
        BtnSecondary.Content = "Le retirer";
        BtnSecondary.Visibility = Visibility.Visible;
    }

    void Secondary_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        // après mesure, ce bouton retire le réglage ; avant, il annule tout
        if (_phase == 3 && _tweaked != null && _tweakApplied)
        {
            _journal.RevertTweak(_tweak.Id);
            Changed = true;
        }
        Close();
    }

    void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

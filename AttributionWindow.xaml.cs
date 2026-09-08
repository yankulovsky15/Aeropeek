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
            StepText.Text = "This tweak cannot be measured this way.";
            HintText.Text = "It only takes effect after Windows restarts. A comparison "
                          + "within the same session would measure nothing. Measure first, restart, "
                          + "then measure again — the Benchmark tab handles that.";
            BtnPrimary.Content = "Fermer";
            BtnSecondary.Visibility = Visibility.Collapsed;
            _phase = 3;
            return;
        }

        if (!Benchmark.IsRunning("cs2"))
        {
            StepText.Text = "CS2 isn't running.";
            HintText.Text = "Start the game, get into a match, then open this window again. "
                          + "Both runs have to happen under comparable conditions.";
            BtnPrimary.Content = "Fermer";
            BtnSecondary.Visibility = Visibility.Collapsed;
            _phase = 3;
            return;
        }

        StepText.Text = "Two 60-second runs, with the tweak applied in between.";
        HintText.Text = _tweakApplied
            ? "The tweak is currently applied: it will first be removed to establish the baseline, "
              + "then put back. Stay in game from start to finish — about two and a half minutes."
            : "Stay in game from start to finish, on the same map and under similar conditions. "
              + "Environ deux minutes trente au total.";
        FootNote.Text = "Nothing is kept if you cancel.";
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
                StepText.Text = "Temporarily removing the tweak…";
                HintText.Text = "Needed to establish the baseline.";
                _journal.RevertTweak(_tweak.Id);
                Changed = true;
                await Task.Delay(1200);
            }

            _phase = 1;
            _baseline = await Capture("Run 1 of 2 — without the tweak");

            StepText.Text = "Applying the tweak…";
            HintText.Text = "";
            _tweak.Apply(_journal);
            Changed = true;
            _tweakApplied = true;
            await Task.Delay(1500);

            _phase = 2;
            _tweaked = await Capture("Run 2 of 2 — with the tweak");

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
            StepText.Text = "The measurement failed.";
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
                ? $"Get back in the game — recording starts in {inDelay} s"
                : $"Recording — {left} s left. Don't leave the game.";
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
            VerdictText.Text = "No measurable effect on your machine.";
            VerdictText.Foreground = B("#7C8FA4");
            VerdictNote.Text = $"The difference is {a.DeltaLow1Pct:+0.0;-0.0}%, below the 2% threshold under which "
                             + "two consecutive matches already vary on their own. This is not a failed measurement: "
                             + "it is the answer.";
        }
        else if (a.DeltaLow1Pct > 0)
        {
            VerdictText.Text = $"Real gain: +{a.DeltaLow1Pct:0.0}% on the 1% lows.";
            VerdictText.Foreground = B("#4FBF8B");
            VerdictNote.Text = $"FPS moyen : {a.BaselineAvg:0} → {a.TweakedAvg:0} ({a.DeltaAvgPct:+0.0;-0.0} %). "
                             + "Measurement kept: the tweak's card will now show this figure instead of an estimate.";
        }
        else
        {
            VerdictText.Text = $"This tweak costs you {Math.Abs(a.DeltaLow1Pct):0.0}%.";
            VerdictText.Foreground = B("#F2726A");
            VerdictNote.Text = "On your configuration you are better off not applying it. "
                             + "The button below removes it.";
        }

        ResultCard.Visibility = Visibility.Visible;
        StepText.Text = "Measurement finished.";
        HintText.Text = $"{a.Frames} frames analysed in total.";
        FootNote.Text = "";

        _phase = 3;
        BtnPrimary.Content = "Keep the tweak";
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

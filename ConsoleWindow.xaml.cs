using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Aeropeek.Core;

namespace Aeropeek;

/// <summary>
/// Exécute un utilitaire système et montre sa sortie au fur et à mesure.
/// Aucune écriture n'est journalisée : ces outils sont ceux de Windows, ils
/// agissent seuls et Aeropeek ne prétend pas pouvoir les annuler.
/// </summary>
public partial class ConsoleWindow : Window
{
    readonly Utility _utility;
    readonly CancellationTokenSource _cts = new();
    readonly StringBuilder _buffer = new();
    bool _running;

    static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public ConsoleWindow(Utility utility)
    {
        InitializeComponent();
        _utility = utility;

        TitleText.Text = utility.Title;
        CmdText.Text = utility.Display;
        IconPath.Data = Geometry.Parse(utility.Icon);

        Loaded += async (_, _) => { PlayOpen(); await RunAsync(); };
        Closed += (_, _) => _cts.Cancel();
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

    void Spin(bool on)
    {
        Spinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;

        var sweep = new DoubleAnimation(-60, 180, TimeSpan.FromMilliseconds(1000))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        ((TranslateTransform)SpinnerBar.RenderTransform).BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    void Append(string line)
    {
        _buffer.AppendLine(line);
        OutputText.Text = _buffer.ToString();
        Scroller.ScrollToEnd();
    }

    async Task RunAsync()
    {
        _running = true;
        Spin(true);
        StatusText.Text = "En cours…";
        BtnClose.Content = "Interrompre";

        if (!string.IsNullOrEmpty(_utility.Caution)) Append("· " + _utility.Caution + Environment.NewLine);

        try
        {
            var progress = new Progress<string>(Append);
            int code = _utility.Kind == UtilityKind.External
                ? await External.FetchAndRunAsync(_utility, progress, _cts.Token)
                : await Utilities.RunAsync(_utility, progress, _cts.Token);

            StatusText.Text = _utility.Kind == UtilityKind.External
                ? "Tool started."
                : code == 0 ? "Finished with no error." : $"Finished with code {code}.";
            StatusText.Foreground = code == 0 ? B("#4FBF8B") : B("#E0A63C");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Interrompu.";
            StatusText.Foreground = B("#7C8FA4");
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            StatusText.Text = "Failed.";
            StatusText.Foreground = B("#F2726A");
        }
        finally
        {
            _running = false;
            Spin(false);
            BtnClose.Content = "Fermer";
        }
    }

    void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_running) { _cts.Cancel(); return; }   // premier clic : on interrompt
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_running) { Close(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}

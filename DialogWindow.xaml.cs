using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Aeropeek;

public partial class DialogWindow : Window
{
    // tracés repris du vocabulaire d'icônes de l'application
    const string IcoInfo = "M 3,12 A 9,9 0 1 0 21,12 A 9,9 0 1 0 3,12 M 12,11 V 17 M 12,7.6 H 12.01";
    const string IcoWarn = "M 12,3 L 21.5,20 H 2.5 Z M 12,9.5 V 14.5 M 12,17.4 H 12.01";
    const string IcoAsk = "M 3,12 A 9,9 0 1 0 21,12 A 9,9 0 1 0 3,12 M 9.2,9.4 A 2.8,2.8 0 1 1 12,13 V 14.6 M 12,17.6 H 12.01";
    const string IcoOk = "M 3,12 A 9,9 0 1 0 21,12 A 9,9 0 1 0 3,12 M 7.8,12.2 L 10.8,15.2 L 16.4,9";

    DialogWindow() => InitializeComponent();

    void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = BtnCancel.Visibility != Visibility.Visible; e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    internal static bool Show(string title, string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var w = new DialogWindow { TitleText = { Text = title }, MessageText = { Text = message } };

        var (path, stroke, pill) = icon switch
        {
            MessageBoxImage.Warning => (IcoWarn, "#E0A63C", "#2B2317"),
            MessageBoxImage.Error => (IcoWarn, "#F2726A", "#2C1A1A"),
            MessageBoxImage.Question => (IcoAsk, "#2DD4BF", "#0F2A29"),
            MessageBoxImage.None => (IcoOk, "#4FBF8B", "#132A22"),
            _ => (IcoInfo, "#6BA6F5", "#16223A")
        };
        w.IconPath.Data = Geometry.Parse(path);
        w.IconPath.Stroke = B(stroke);
        w.IconPill.Background = B(pill);

        if (buttons is MessageBoxButton.YesNo or MessageBoxButton.OKCancel)
        {
            w.BtnCancel.Visibility = Visibility.Visible;
            w.BtnCancel.Content = buttons == MessageBoxButton.YesNo ? "No" : "Cancel";
            w.BtnOk.Content = buttons == MessageBoxButton.YesNo ? "Oui" : "OK";
        }

        var owner = Application.Current?.MainWindow;
        if (owner != null && owner.IsLoaded && !ReferenceEquals(owner, w)) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return w.ShowDialog() == true;
    }
}

/// <summary>
/// Remplace <see cref="System.Windows.MessageBox"/> pour tout le code de
/// l'application : étant dans le même espace de noms, cette classe a la priorité
/// sur celle du framework. Les appels existants n'ont pas eu à changer, et toutes
/// les boîtes de dialogue adoptent l'apparence de l'application.
/// </summary>
internal static class MessageBox
{
    public static MessageBoxResult Show(string text) =>
        Show(text, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Information);

    public static MessageBoxResult Show(string text, string caption) =>
        Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Information);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons) =>
        Show(text, caption, buttons, MessageBoxImage.Information);

    public static MessageBoxResult Show(string text, string caption,
                                        MessageBoxButton buttons, MessageBoxImage icon)
    {
        bool ok = DialogWindow.Show(caption, text, buttons, icon);
        return buttons switch
        {
            MessageBoxButton.YesNo => ok ? MessageBoxResult.Yes : MessageBoxResult.No,
            MessageBoxButton.OKCancel => ok ? MessageBoxResult.OK : MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK
        };
    }
}

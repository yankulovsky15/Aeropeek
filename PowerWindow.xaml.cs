using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Aeropeek.Core;

namespace Aeropeek;

/// <summary>
/// Gestion des plans d'alimentation, en fenêtre flottante. Toutes les écritures
/// passent par le journal de l'application appelante : elles restent annulables
/// depuis l'onglet Journal comme le reste.
/// </summary>
public partial class PowerWindow : Window
{
    readonly Journal _journal;
    List<PowerPlan> _plans = new();

    static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    static readonly Brush AccFg = B("#2DD4BF"), MuteFg = B("#7C8FA4");

    public bool Changed { get; private set; }

    public PowerWindow(Journal journal)
    {
        InitializeComponent();
        _journal = journal;
        Loaded += async (_, _) => { PlayOpen(); await Refresh(); };
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

    void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    // ---------- lecture ----------

    /// <summary>
    /// Chaque lecture lance un processus powercfg, et il en faut sept. Sur le fil
    /// d'interface, ouvrir cette fenêtre la figeait le temps de les enchaîner.
    /// </summary>
    async Task Refresh()
    {
        PowerVeil.Visibility = Visibility.Visible;
        try
        {
            var lu = await Task.Run(() => (
                Plans: PowerOps.ListPlans(),
                Dupes: PowerOps.Duplicates().Select(g => (Nom: g.Key, Nombre: g.Count())).ToList(),
                Ultimate: PowerOps.FindUltimate(),
                Usb: PowerOps.ReadSetting(PowerOps.SubUsb, PowerOps.UsbSuspend),
                MinState: PowerOps.ReadSetting(PowerOps.SubProcessor, PowerOps.MinProcState),
                MinCores: PowerOps.ReadSetting(PowerOps.SubProcessor, PowerOps.MinCores),
                Aspm: PowerOps.ReadSetting(PowerOps.SubPci, PowerOps.PciAspm)));

            _plans = lu.Plans;
            var active = _plans.FirstOrDefault(p => p.Active);

            PlansList.ItemsSource = _plans.Select(p => new PlanVm
            {
                Model = p,
                DotFill = p.Active ? AccFg : B("#28374A"),
                NameFill = p.Active ? B("#E8EEF5") : B("#9DB0C4")
            }).ToList();

            PowerSummary.Text = active != null
                ? $"Actif : {active.Name} · {_plans.Count} plans disponibles"
                : $"{_plans.Count} plans disponibles";

            var dupes = lu.Dupes;
            int extra = dupes.Sum(g => g.Nombre - 1);
            CardDupes.Visibility = extra > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (extra > 0)
                DupesDetail.Text = $"{extra} copie(s) inutile(s) : "
                    + string.Join(", ", dupes.Select(g => $"« {g.Nom} » ×{g.Nombre}"))
                    + ". Le plan actif et un exemplaire de chaque nom sont conservés ; "
                    + "les autres sont exportés avant suppression.";

            var (ultimate, certain) = lu.Ultimate;
            BtnUltimate.Content = ultimate != null ? "Déjà présent" : "Ajouter";
            BtnUltimate.IsEnabled = ultimate == null;
            UltimateDetail.Text = ultimate == null
                ? "Plan livré avec Windows mais masqué par défaut. Aucun ralentissement des cœurs en charge légère."
                : certain
                    ? $"Déjà présent sous le nom « {ultimate.Name} »" + (ultimate.Active ? " — c'est ton plan actif." : ".")
                    : $"Un plan nommé « {ultimate.Name} » est déjà présent"
                      + (ultimate.Active ? " et c'est ton plan actif" : "")
                      + ". Son nom indique qu'il dérive du modèle Performances ultimes, sans qu'Aeropeek puisse le certifier.";

            int? usb = lu.Usb;
            TglUsb.IsChecked = usb == 0;
            TglUsb.IsEnabled = usb != null;
            UsbState.Text = usb == null ? "illisible" : (usb == 0 ? "Appliqué" : "Non appliqué");
            UsbDetail.Text = usb == 0
                ? "La suspension sélective est désactivée : tes ports restent alimentés en permanence."
                : "Windows peut mettre tes ports USB en veille. Sur une souris ou un casque sans fil, "
                  + "ça ajoute une latence au réveil. Contrepartie : quelques watts de plus au repos.";

            int? minState = lu.MinState, minCores = lu.MinCores, aspm = lu.Aspm;

            PowerFacts.Text = string.Join("\n", new[]
            {
                minState != null ? $"· État minimal du processeur : {minState} %" : "· État minimal du processeur : non lisible",
                minCores != null ? $"· Cœurs actifs minimum : {minCores} %" : "· Parking de cœurs : réglage masqué par Windows",
                aspm != null ? $"· Économie d'énergie PCI Express : {(aspm == 0 ? "désactivée" : "active")}" : "· PCI Express : non lisible",
                usb != null ? $"· Suspension sélective USB : {(usb == 0 ? "désactivée" : "active")}" : "· USB : non lisible"
            });
        }
        catch (Exception ex)
        {
            PowerSummary.Text = "Lecture impossible : " + ex.Message;
        }
        finally { PowerVeil.Visibility = Visibility.Collapsed; }
    }

    // ---------- actions ----------

    void ApplyPlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlanVm vm } || vm.Active) return;
        try
        {
            _journal.Record(PowerOps.SetActivePlan(vm.Model.Id, "Plan d'alimentation : " + vm.Name));
            Changed = true;
            _ = Refresh();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void DeletePlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlanVm vm }) return;

        var answer = MessageBox.Show(
            $"Supprimer le plan « {vm.Name} » ?\n\n" +
            "Il est exporté sur disque avant suppression : « Tout annuler » saura le réimporter.",
            "Supprimer un plan", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _journal.Record(PowerOps.DeletePlan(vm.Model.Id, vm.Name));
            Changed = true;
            _ = Refresh();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void CleanDupes_Click(object sender, RoutedEventArgs e)
    {
        var toDelete = new List<PowerPlan>();
        foreach (var g in PowerOps.Duplicates())
        {
            var keep = g.FirstOrDefault(p => p.Active) ?? g.First();
            toDelete.AddRange(g.Where(p => p.Id != keep.Id));
        }
        if (toDelete.Count == 0) return;

        var answer = MessageBox.Show(
            $"Supprimer {toDelete.Count} plan(s) en double ?\n\n" +
            "Chacun est exporté avant suppression : « Tout annuler » saura les réimporter.",
            "Nettoyer les doublons", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        int done = 0;
        foreach (var p in toDelete)
        {
            try { _journal.Record(PowerOps.DeletePlan(p.Id, p.Name)); done++; } catch { }
        }

        Changed = true;
        _ = Refresh();
        MessageBox.Show($"{done} plan(s) supprimé(s).", "Aeropeek");
    }

    void Ultimate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (_, created) = PowerOps.EnsureUltimatePlan();
            Changed = true;
            _ = Refresh();
            MessageBox.Show(created
                ? "Le plan Performances ultimes est maintenant disponible dans la liste."
                : "Ce plan existait déjà : rien n'a été créé.", "Aeropeek");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void UsbPower_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int target = TglUsb.IsChecked == true ? 0 : 1;
            _journal.Record(PowerOps.SetSetting(PowerOps.SubUsb, PowerOps.UsbSuspend, target,
                target == 0 ? "Alimentation permanente des ports USB" : "Suspension sélective USB rétablie"));
            Changed = true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Aeropeek", MessageBoxButton.OK, MessageBoxImage.Warning); }
        _ = Refresh();
    }
}

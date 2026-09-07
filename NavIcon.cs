using System.Windows;
using System.Windows.Media;

namespace Aeropeek;

/// <summary>
/// Porte le tracé de l'icône d'une entrée de navigation, pour que le gabarit
/// puisse l'afficher sans avoir à redéfinir le contenu de chaque bouton.
/// </summary>
public static class NavIcon
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.RegisterAttached("Data", typeof(Geometry), typeof(NavIcon),
            new PropertyMetadata(null));

    public static void SetData(DependencyObject o, Geometry value) => o.SetValue(DataProperty, value);
    public static Geometry GetData(DependencyObject o) => (Geometry)o.GetValue(DataProperty);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Aeropeek.Core;

/// <summary>
/// Coins arrondis de la fenêtre, demandés au gestionnaire de fenêtres.
/// <para>
/// Windows 11 arrondit les fenêtres tout seul, mais seulement celles qui ont un
/// cadre standard. Avec <c>WindowStyle="None"</c>, la nôtre est un popup sans
/// cadre : DWM la laisse carrée. Il faut donc le lui demander.
/// </para>
/// <para>
/// C'est DWM qui découpe, pas nous : l'arrondi est lissé, il emporte l'ombre
/// portée avec lui, et il disparaît de lui-même en plein écran — trois choses
/// qu'un découpage fait à la main dans l'application ne saurait pas faire.
/// </para>
/// </summary>
public static class WindowEffects
{
    // dwmapi.h — ces constantes ne sont pas exposées par .NET.
    const int UseImmersiveDarkMode = 20;
    const int WindowCornerPreference = 33;

    const int RoundCorners = 2;   // DWMWCP_ROUND

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Arrondit les coins et passe le cadre en thème sombre.
    /// <para>
    /// Sans le thème sombre, le liseré d'un pixel que DWM dessine autour des
    /// coins arrondis reste clair : il ferait un halo blanc sur une interface
    /// qui ne l'est pas.
    /// </para>
    /// Sans effet avant Windows 11 : l'appel échoue et la fenêtre reste carrée,
    /// comme aujourd'hui.
    /// </summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        Set(hwnd, UseImmersiveDarkMode, 1);
        Set(hwnd, WindowCornerPreference, RoundCorners);
    }

    static void Set(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
}

using System.IO;
using System.Text.Json;

namespace Aeropeek.Core;

/// <summary>
/// Préférences locales. Sert notamment à mémoriser ce que l'application ne peut
/// pas déterminer seule et que l'utilisateur a confirmé une fois pour toutes.
/// </summary>
public sealed class Settings
{
    /// <summary>
    /// Fréquence mémoire confirmée par l'utilisateur comme étant la bonne.
    /// Windows n'expose pas la fréquence certifiée des barrettes : sans SPD,
    /// seule une confirmation humaine peut lever le doute.
    /// </summary>
    public uint ConfirmedMemoryMhz { get; set; }

    /// <summary>
    /// L'écran d'accueil a déjà été lu. Il ne réapparaît pas de lui-même :
    /// c'est une présentation, pas un avertissement à répéter.
    /// </summary>
    public bool WelcomeSeen { get; set; }

    static string Path => System.IO.Path.Combine(Journal.Directory, "settings.json");

    static Settings? _current;
    public static Settings Current => _current ??= Load();

    static Settings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Journal.Directory);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

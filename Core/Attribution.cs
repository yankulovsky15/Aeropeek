using System.IO;
using System.Text.Json;

namespace Aeropeek.Core;

/// <summary>
/// Effet mesuré d'un réglage sur CETTE machine : une mesure sans, une mesure avec,
/// et l'écart entre les deux. C'est ce qui permet de remplacer un gain annoncé par
/// un gain constaté.
/// </summary>
public sealed class Attribution
{
    public string TweakId { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTimeOffset When { get; set; } = DateTimeOffset.Now;

    public double BaselineAvg { get; set; }
    public double BaselineLow1 { get; set; }
    public double TweakedAvg { get; set; }
    public double TweakedLow1 { get; set; }
    public int Frames { get; set; }

    public double DeltaLow1Pct =>
        BaselineLow1 > 0 ? (TweakedLow1 - BaselineLow1) / BaselineLow1 * 100.0 : 0;

    public double DeltaAvgPct =>
        BaselineAvg > 0 ? (TweakedAvg - BaselineAvg) / BaselineAvg * 100.0 : 0;

    /// <summary>
    /// Sous 2 %, l'écart ne se distingue pas de la variation naturelle entre deux
    /// parties. On refuse alors de conclure, dans un sens comme dans l'autre.
    /// </summary>
    public bool Significant => Math.Abs(DeltaLow1Pct) >= 2.0;

    public string Verdict => !Significant
        ? "sans effet mesurable ici"
        : DeltaLow1Pct > 0
            ? $"mesuré ici : +{DeltaLow1Pct:0.0} % sur les 1% lows"
            : $"mesuré ici : {DeltaLow1Pct:0.0} % sur les 1% lows";
}

public sealed class AttributionStore
{
    public int Version { get; set; } = 1;
    public List<Attribution> Items { get; set; } = new();

    static string Path => System.IO.Path.Combine(Journal.Directory, "attributions.json");

    public static AttributionStore Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AttributionStore>(File.ReadAllText(Path)) ?? new AttributionStore();
        }
        catch { }
        return new AttributionStore();
    }

    /// <summary>Dernière mesure connue pour un réglage donné.</summary>
    public Attribution? For(string tweakId) =>
        Items.Where(a => a.TweakId == tweakId).OrderByDescending(a => a.When).FirstOrDefault();

    public void Add(Attribution a)
    {
        Items.Add(a);
        try
        {
            Directory.CreateDirectory(Journal.Directory);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

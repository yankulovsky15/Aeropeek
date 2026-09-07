namespace Aeropeek.Core;

public enum CompareVerdict
{
    /// <summary>Pas de mesure précédente.</summary>
    None,
    /// <summary>L'application ignore ce qui s'est passé entre les deux.</summary>
    Unknown,
    /// <summary>L'écart n'est pas un effet : variation naturelle.</summary>
    Noise,
    /// <summary>Quelque chose a changé, mais les deux mesures ne sont pas comparables.</summary>
    NotComparable,
    Gain,
    Loss
}

public sealed record Comparison(CompareVerdict Verdict, string Text);

/// <summary>
/// Comparaison de deux captures.
/// <para>
/// Le piège que cette classe existe pour éviter : afficher un écart entre deux
/// mesures et laisser croire qu'un réglage en est la cause. Deux captures faites
/// à des moments différents diffèrent de toute façon — carte, serveur, adversaires,
/// état de la machine. Un écart n'est attribuable que si trois conditions tiennent :
/// on sait ce qui a changé, quelque chose a effectivement changé, et les deux
/// mesures se suivent d'assez près.
/// </para>
/// <para>
/// Le seuil de significativité n'est pas choisi : il est MESURÉ, à partir des
/// paires de captures entre lesquelles l'utilisateur n'a rien touché. Ces
/// paires-là ne montrent que du bruit, par construction.
/// </para>
/// </summary>
public static class BenchCompare
{
    /// <summary>Au-delà, deux captures ne relèvent plus de la même session de jeu.</summary>
    public static readonly TimeSpan SameSession = TimeSpan.FromHours(2);

    /// <summary>On ne prétend jamais mesurer plus fin, même si l'historique est calme.</summary>
    public const double FloorPct = 2.0;

    public static Comparison Compare(BenchRun? prev, BenchRun cur,
                                     IReadOnlyList<ChangeMark>? changes, double noisePct)
    {
        if (prev == null || prev.Low1Fps <= 0) return new(CompareVerdict.None, "");

        double pct = (cur.Low1Fps - prev.Low1Fps) / prev.Low1Fps * 100.0;
        string amount = $"{(pct >= 0 ? "+" : "−")}{Math.Abs(pct):0.0} %";
        string figures = $"({prev.Low1Fps:0} → {cur.Low1Fps:0} fps)";
        var gap = cur.When - prev.When;

        // La chronologie ne couvre pas cette période : on ne peut rien conclure.
        if (changes == null)
            return new(CompareVerdict.Unknown,
                $"Écart avec « {prev.Label} » : {amount} sur les 1% lows {figures}. "
                + "Aeropeek ne sait pas ce qui a changé entre ces deux mesures — elles sont "
                + "antérieures à la tenue de sa chronologie. L'écart n'est attribuable à rien.");

        if (changes.Count == 0)
            return new(CompareVerdict.Noise,
                $"Rien n'a été modifié entre « {prev.Label} » et cette mesure. L'écart de "
                + $"{amount} {figures} est la variation naturelle de ta machine, pas un effet.");

        string what = changes.Count == 1 ? $"« {changes[0].Label} »" : $"{changes.Count} réglages";
        string verb = changes.Count == 1 ? "a changé" : "ont changé";

        if (gap > SameSession)
            return new(CompareVerdict.NotComparable,
                $"{what} {verb} entre les deux mesures, mais {Gap(gap)} les séparent : l'écart "
                + $"de {amount} {figures} n'est pas attribuable. La carte, le serveur et l'état "
                + "de la machine ont changé aussi. Refais les deux captures à la suite pour trancher.");

        if (Math.Abs(pct) < noisePct)
            return new(CompareVerdict.Noise,
                $"{what} {verb} : {amount} {figures}, sous le bruit mesuré sur ta machine "
                + $"(±{noisePct:0.0} %). Rien de significatif.");

        return new(pct > 0 ? CompareVerdict.Gain : CompareVerdict.Loss,
            $"{what} {verb} : {amount} sur les 1% lows {figures}.");
    }

    /// <summary>
    /// Bruit de mesure observé sur cette machine : le plus grand écart entre deux
    /// captures rapprochées que rien ne sépare. Faute d'une telle paire, on garde
    /// le plancher — mieux vaut un seuil arbitraire assumé qu'un seuil inventé à
    /// partir de données qui ne disent rien.
    /// </summary>
    public static double NoiseFloor(IReadOnlyList<BenchRun> runs,
                                    Func<BenchRun, BenchRun, IReadOnlyList<ChangeMark>?> between)
    {
        double worst = 0;

        for (int i = 1; i < runs.Count; i++)
        {
            var (a, b) = (runs[i - 1], runs[i]);
            if (a.Low1Fps <= 0) continue;
            if (b.When - a.When > SameSession) continue;

            var changes = between(a, b);
            if (changes is not { Count: 0 }) continue;   // inconnu ou modifié : n'apprend rien

            worst = Math.Max(worst, Math.Abs((b.Low1Fps - a.Low1Fps) / a.Low1Fps * 100.0));
        }

        return Math.Max(worst, FloorPct);
    }

    static string Gap(TimeSpan g) =>
        g.TotalDays >= 2 ? $"{g.TotalDays:0} jours"
        : g.TotalHours >= 24 ? "plus d'une journée"
        : g.TotalHours >= 2 ? $"{g.TotalHours:0} heures"
        : $"{g.TotalMinutes:0} minutes";
}

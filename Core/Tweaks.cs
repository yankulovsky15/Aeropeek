using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Aeropeek.Core;

public enum Verdict { Applicable, DejaApplique, NonPertinent, Bloque }

public sealed record Applicability(Verdict Verdict, string Reason = "")
{
    public bool CanApply => Verdict is Verdict.Applicable;
}

public sealed class TweakOperation
{
    [JsonPropertyName("hive")] public string Hive { get; set; } = "HKCU";
    [JsonPropertyName("key")] public string SubKey { get; set; } = "";
    [JsonPropertyName("valueName")] public string ValueName { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "dword";
    [JsonPropertyName("to")] public JsonElement Target { get; set; }

    public RegistryValueKind Kind => Type.Equals("string", StringComparison.OrdinalIgnoreCase)
        ? RegistryValueKind.String
        : RegistryValueKind.DWord;

    public object TargetValue => Kind == RegistryValueKind.String
        ? (Target.ValueKind == JsonValueKind.String ? Target.GetString() ?? "" : Target.ToString())
        : (Target.ValueKind == JsonValueKind.Number ? Target.GetInt32() : 0);

    public bool Matches(object? current)
    {
        if (current == null) return false;
        if (Kind == RegistryValueKind.String)
            return string.Equals(current.ToString(), TargetValue.ToString(), StringComparison.OrdinalIgnoreCase);
        return Convert.ToInt64(current) == Convert.ToInt64(TargetValue);
    }
}

public sealed class TweakCondition
{
    [JsonPropertyName("buildMin")] public int BuildMin { get; set; }
    [JsonPropertyName("buildMax")] public int BuildMax { get; set; }
    [JsonPropertyName("laptop")] public string? Laptop { get; set; }     // "no" = masqué sur portable
    [JsonPropertyName("gpu")] public string? Gpu { get; set; }           // "nvidia" | "amd" | "intel"
}

public sealed class Tweak
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
    [JsonPropertyName("consequence")] public string Consequence { get; set; } = "";
    [JsonPropertyName("gain")] public string Gain { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "performance";
    [JsonPropertyName("needsRestart")] public bool NeedsRestart { get; set; }
    [JsonPropertyName("appliesWhen")] public TweakCondition? Condition { get; set; }
    [JsonPropertyName("operations")] public List<TweakOperation> Operations { get; set; } = new();

    /// <summary>Vrai si toutes les opérations sont déjà à la valeur voulue.</summary>
    public bool IsApplied()
    {
        if (Operations.Count == 0) return false;
        foreach (var op in Operations)
            if (!op.Matches(RegistryOps.ReadValue(op.Hive, op.SubKey, op.ValueName))) return false;
        return true;
    }

    public Applicability Check(SystemProfile sys)
    {
        foreach (var op in Operations)
            if (!RegistryOps.IsAllowed(op.Hive, op.SubKey, out var why))
                return new Applicability(Verdict.Bloque, why);

        var c = Condition;
        if (c != null)
        {
            if (c.BuildMin > 0 && sys.OsBuild < c.BuildMin)
                return new Applicability(Verdict.NonPertinent, $"Requires Windows build {c.BuildMin} or newer");
            if (c.BuildMax > 0 && sys.OsBuild > c.BuildMax)
                return new Applicability(Verdict.NonPertinent, $"Not applicable from build {c.BuildMax} onwards");
            if (string.Equals(c.Laptop, "no", StringComparison.OrdinalIgnoreCase) && sys.IsLaptop)
                return new Applicability(Verdict.NonPertinent, "Not advised on a laptop");
            if (!string.IsNullOrEmpty(c.Gpu)
                && !sys.GpuNames.Any(g => g.Contains(c.Gpu, StringComparison.OrdinalIgnoreCase)))
                return new Applicability(Verdict.NonPertinent, $"No {c.Gpu} graphics card detected");
        }

        return IsApplied()
            ? new Applicability(Verdict.DejaApplique, "Already applied")
            : new Applicability(Verdict.Applicable);
    }

    /// <summary>Applique le réglage et journalise chaque écriture avec son état précédent.</summary>
    public int Apply(Journal journal)
    {
        int count = 0;
        foreach (var op in Operations)
        {
            // Déjà à la bonne valeur : ne rien écrire, et surtout ne rien journaliser.
            // Une ligne de journal doit correspondre à une modification réelle.
            if (op.Matches(RegistryOps.ReadValue(op.Hive, op.SubKey, op.ValueName))) continue;

            var rec = RegistryOps.Apply(op.Hive, op.SubKey, op.ValueName, op.TargetValue, op.Kind, Id, Name);
            journal.Record(rec);
            count++;
        }
        return count;
    }
}

public sealed class Catalogue
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("tweaks")] public List<Tweak> Tweaks { get; set; } = new();

    public static Catalogue Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "catalogue.json");
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Catalogue>(File.ReadAllText(path)) ?? new Catalogue();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Catalogue illisible : " + ex.Message);
        }
        return new Catalogue();
    }
}

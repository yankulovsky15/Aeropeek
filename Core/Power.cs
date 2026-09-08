using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Aeropeek.Core;

public sealed record PowerPlan(Guid Id, string Name, bool Active);

/// <summary>
/// Réglages d'alimentation via powercfg. Même discipline que le registre :
/// liste blanche stricte, état d'origine capturé avant écriture, annulation exacte.
/// </summary>
public static class PowerOps
{
    // Sous-groupes et paramètres — identifiants stables de Windows.
    public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    public const string MinProcState = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    public const string MinCores     = "0cc5b647-c1df-4637-891a-dec35c318583";
    public const string SubUsb       = "2a737441-1930-4402-8d77-b2bebba308a3";
    public const string UsbSuspend   = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    public const string SubPci       = "501a4d13-42af-4429-9fd1-a8218c268e20";
    public const string PciAspm      = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    /// <summary>Plan « Performances ultimes », masqué par défaut dans Windows.</summary>
    public static readonly Guid UltimateTemplate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>
    /// Seuls ces paramètres peuvent être écrits. Un catalogue, même signé, ne peut
    /// pas élargir ce périmètre.
    /// </summary>
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        SubProcessor + "|" + MinProcState,
        SubProcessor + "|" + MinCores,
        SubUsb + "|" + UsbSuspend,
        SubPci + "|" + PciAspm
    };

    public static bool IsAllowed(string subgroup, string setting) =>
        Allowed.Contains(subgroup + "|" + setting);

    static string BackupDir => Path.Combine(Journal.Directory, "plans");

    // ---------- exécution ----------

    /// <summary>
    /// Les utilitaires console de Windows écrivent dans la page de codes OEM.
    /// Sans ça, « Économie d'énergie » revient en « conomie d',nergie ».
    /// </summary>
    static readonly System.Text.Encoding OemEncoding = GetOem();

    static System.Text.Encoding GetOem()
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { return System.Text.Encoding.UTF8; }
    }

    static (int Code, string Output) Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p == null) return (-1, "");
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, o);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    // ---------- lecture ----------

    public static List<PowerPlan> ListPlans()
    {
        var list = new List<PowerPlan>();
        var (_, output) = Run("/list");

        foreach (Match m in Regex.Matches(output,
            @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(([^)]*)\)(\s*\*)?"))
        {
            if (Guid.TryParse(m.Groups[1].Value, out var g))
                list.Add(new PowerPlan(g, m.Groups[2].Value.Trim(), m.Groups[3].Success));
        }
        return list;
    }

    public static PowerPlan? ActivePlan() => ListPlans().FirstOrDefault(p => p.Active);

    /// <summary>Groupes de plans partageant exactement le même nom.</summary>
    public static List<IGrouping<string, PowerPlan>> Duplicates() =>
        ListPlans().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                   .Where(g => g.Count() > 1)
                   .ToList();

    /// <summary>Valeur du paramètre sur secteur pour le plan actif, ou null.</summary>
    public static int? ReadSetting(string subgroup, string setting)
    {
        var (_, output) = Run("/query", "SCHEME_CURRENT", subgroup, setting);
        var hex = Regex.Matches(output, @"0x([0-9a-fA-F]{8})");
        if (hex.Count < 2) return null;
        return Convert.ToInt32(hex[^2].Groups[1].Value, 16);   // avant-dernière = secteur
    }

    // ---------- écriture ----------

    public static OpRecord SetActivePlan(Guid target, string description)
    {
        var previous = ActivePlan();
        var (code, output) = Run("/setactive", target.ToString());
        if (code != 0) throw new InvalidOperationException("Plan switch refused. " + Shorten(output));

        return new OpRecord
        {
            Kind = "power-plan",
            TweakId = "alimentation",
            Description = description,
            PreviousValue = previous?.Id.ToString(),
            NewValue = target.ToString()
        };
    }

    public static OpRecord SetSetting(string subgroup, string setting, int value, string description)
    {
        if (!IsAllowed(subgroup, setting))
            throw new InvalidOperationException("Power setting not allowed.");

        int? previous = ReadSetting(subgroup, setting);

        var (code, output) = Run("/setacvalueindex", "SCHEME_CURRENT", subgroup, setting, value.ToString());
        if (code != 0) throw new InvalidOperationException("Write refused. " + Shorten(output));
        Run("/setactive", "SCHEME_CURRENT");   // sans ça le changement n'est pas appliqué

        return new OpRecord
        {
            Kind = "power-setting",
            TweakId = "alimentation",
            Description = description,
            SubKey = subgroup,
            ValueName = setting,
            Existed = previous != null,
            PreviousValue = previous?.ToString(),
            NewValue = value.ToString()
        };
    }

    /// <summary>
    /// Rend disponible le plan « Performances ultimes ». Idempotent : s'il existe
    /// déjà, on le renvoie au lieu d'en fabriquer une copie de plus.
    /// </summary>
    /// <summary>
    /// Cherche un plan Performances ultimes déjà présent. « Certain » quand
    /// l'identifiant est celui du modèle Windows, « probable » quand seule la
    /// dénomination correspond — un autre outil peut avoir nommé son plan ainsi.
    /// </summary>
    public static (PowerPlan? Plan, bool Certain) FindUltimate()
    {
        var plans = ListPlans();

        var exact = plans.FirstOrDefault(p => p.Id == UltimateTemplate);
        if (exact != null) return (exact, true);

        var byName = plans.FirstOrDefault(p =>
            p.Name.Contains("ultime", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("ultimate", StringComparison.OrdinalIgnoreCase));
        return (byName, false);
    }

    public static (Guid Id, bool Created) EnsureUltimatePlan()
    {
        var (existing, _) = FindUltimate();
        if (existing != null) return (existing.Id, false);

        var (code, output) = Run("/duplicatescheme", UltimateTemplate.ToString());
        if (code != 0) throw new InvalidOperationException("Plan creation refused. " + Shorten(output));

        var m = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        if (!m.Success || !Guid.TryParse(m.Groups[1].Value, out var g))
            throw new InvalidOperationException("Plan created but its identifier is unreadable.");
        return (g, true);
    }

    /// <summary>
    /// Supprime un plan après l'avoir exporté. La sauvegarde permet de le réimporter :
    /// on ne supprime jamais sans filet.
    /// </summary>
    public static OpRecord DeletePlan(Guid id, string name)
    {
        if (ActivePlan()?.Id == id)
            throw new InvalidOperationException("The active plan cannot be deleted.");

        Directory.CreateDirectory(BackupDir);
        var backup = Path.Combine(BackupDir, id.ToString("N") + ".pow");
        Run("/export", backup, id.ToString());

        var (code, output) = Run("/delete", id.ToString());
        if (code != 0) throw new InvalidOperationException("Deletion refused. " + Shorten(output));

        return new OpRecord
        {
            Kind = "power-plan-deleted",
            TweakId = "alimentation",
            Description = "Power plan deleted: " + name,
            PreviousValue = id.ToString(),
            BackupPath = File.Exists(backup) ? backup : ""
        };
    }

    // ---------- annulation ----------

    public static void Revert(OpRecord rec)
    {
        switch (rec.Kind)
        {
            case "power-plan":
                if (Guid.TryParse(rec.PreviousValue, out var prev)) Run("/setactive", prev.ToString());
                break;

            case "power-setting":
                if (!IsAllowed(rec.SubKey, rec.ValueName)) return;
                if (!rec.Existed || !int.TryParse(rec.PreviousValue, out var val)) return;
                Run("/setacvalueindex", "SCHEME_CURRENT", rec.SubKey, rec.ValueName, val.ToString());
                Run("/setactive", "SCHEME_CURRENT");
                break;

            case "power-plan-deleted":
                if (string.IsNullOrEmpty(rec.BackupPath) || !File.Exists(rec.BackupPath)) return;
                if (Guid.TryParse(rec.PreviousValue, out var id))
                    Run("/import", rec.BackupPath, id.ToString());
                break;
        }
    }

    static string Shorten(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" : s.Trim().Split('\n')[0].Trim();
}

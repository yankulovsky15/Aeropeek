using Microsoft.Win32;

namespace Aeropeek.Core;

/// <summary>
/// Une opération déjà effectuée, avec l'état EXACT qui précédait — y compris
/// l'absence de la valeur. C'est ce qui permet une annulation fidèle plutôt
/// qu'un retour à une valeur « par défaut » supposée.
/// </summary>
public sealed record OpRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset When { get; init; } = DateTimeOffset.Now;
    public string Kind { get; init; } = "registry";
    public string TweakId { get; init; } = "";
    public string Description { get; init; } = "";

    // registre
    public string Hive { get; init; } = "";
    public string SubKey { get; init; } = "";
    public string ValueName { get; init; } = "";
    public bool Existed { get; init; }
    public bool SubKeyExisted { get; init; }
    public string? PreviousValue { get; init; }
    public string? PreviousKind { get; init; }
    public string? NewValue { get; init; }

    // service
    public string ServiceName { get; init; } = "";
    public bool WasRunning { get; init; }

    /// <summary>Sauvegarde sur disque permettant de restaurer un objet supprimé.</summary>
    public string BackupPath { get; init; } = "";
}

/// <summary>Point d'entrée unique pour annuler une opération, quel que soit son type.</summary>
public static class Ops
{
    public static void Revert(OpRecord rec)
    {
        switch (rec.Kind)
        {
            case "registry": RegistryOps.Revert(rec); break;
            case "service": ServiceOps.Revert(rec); break;
            case "power-plan":
            case "power-setting":
            case "power-plan-deleted": PowerOps.Revert(rec); break;
            case "dns": DnsOps.Revert(rec); break;
            case "app-closed": AppOps.Revert(rec); break;
            case "priority": AppOps.RevertPriority(rec); break;
            case "cs2-launch": Cs2.RevertLaunch(rec); break;
            case "cs2-file": Cs2.RevertFile(rec); break;
            case "nvidia-setting": Nvidia.Revert(rec); break;
        }
    }

    /// <summary>
    /// Vrai si ce type d'opération sait revenir en arrière. La liste doit suivre
    /// exactement celle de <see cref="Revert"/> : une opération absente du
    /// dispatcher serait annoncée réversible et ne ferait rien.
    /// </summary>
    public static bool CanRevert(string kind) => kind switch
    {
        "registry" or "service" or "power-plan" or "power-setting" or "power-plan-deleted"
            or "dns" or "app-closed" or "priority" or "cs2-launch" or "cs2-file"
            or "nvidia-setting" => true,
        _ => false
    };
}

public static class RegistryOps
{
    /// <summary>Ruches autorisées. Un catalogue distant ne peut pas élargir ce périmètre.</summary>
    static readonly Dictionary<string, RegistryKey> Hives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCU"] = Registry.CurrentUser,
        ["HKLM"] = Registry.LocalMachine
    };

    /// <summary>Chemins interdits, quelle que soit la provenance du catalogue.</summary>
    static readonly string[] Forbidden =
    {
        @"SYSTEM\CurrentControlSet\Services\WinDefend",
        @"SYSTEM\CurrentControlSet\Services\SecurityHealthService",
        @"SYSTEM\CurrentControlSet\Services\wscsvc",
        @"SOFTWARE\Microsoft\Windows Defender",
        @"SYSTEM\CurrentControlSet\Control\Lsa",
        @"SAM",
        @"SECURITY"
    };

    public static bool IsAllowed(string hive, string subKey, out string reason)
    {
        reason = "";
        if (!Hives.ContainsKey(hive)) { reason = $"Hive not allowed: {hive}"; return false; }
        foreach (var f in Forbidden)
            if (subKey.StartsWith(f, StringComparison.OrdinalIgnoreCase))
            { reason = $"Protected path: {subKey}"; return false; }
        return true;
    }

    static RegistryKey Root(string hive) =>
        Hives.TryGetValue(hive, out var k) ? k : throw new InvalidOperationException($"Ruche inconnue : {hive}");

    public static object? ReadValue(string hive, string subKey, string valueName)
    {
        try
        {
            using var k = Root(hive).OpenSubKey(subKey);
            return k?.GetValue(valueName);
        }
        catch { return null; }
    }

    /// <summary>Écrit une valeur et renvoie l'enregistrement permettant de revenir en arrière.</summary>
    public static OpRecord Apply(string hive, string subKey, string valueName,
                                 object newValue, RegistryValueKind kind,
                                 string tweakId, string description)
    {
        if (!IsAllowed(hive, subKey, out var reason))
            throw new InvalidOperationException(reason);

        using var root = Root(hive);
        bool subKeyExisted;
        object? previous;
        RegistryValueKind? previousKind = null;

        using (var probe = root.OpenSubKey(subKey))
        {
            subKeyExisted = probe != null;
            previous = probe?.GetValue(valueName);
            if (previous != null)
            {
                try { previousKind = probe!.GetValueKind(valueName); } catch { }
            }
        }

        using (var write = root.CreateSubKey(subKey, writable: true))
        {
            write.SetValue(valueName, newValue, kind);
        }

        return new OpRecord
        {
            Kind = "registry",
            TweakId = tweakId,
            Description = description,
            Hive = hive,
            SubKey = subKey,
            ValueName = valueName,
            Existed = previous != null,
            SubKeyExisted = subKeyExisted,
            PreviousValue = previous?.ToString(),
            PreviousKind = previousKind?.ToString(),
            NewValue = newValue.ToString()
        };
    }

    /// <summary>
    /// Annule une opération. Si la valeur n'existait pas avant, elle est supprimée —
    /// on ne réécrit jamais une valeur « par défaut » inventée.
    /// </summary>
    public static void Revert(OpRecord rec)
    {
        if (rec.Kind != "registry") return;
        if (!IsAllowed(rec.Hive, rec.SubKey, out _)) return;

        using var root = Root(rec.Hive);

        if (!rec.Existed)
        {
            using var k = root.OpenSubKey(rec.SubKey, writable: true);
            if (k == null) return;
            try { k.DeleteValue(rec.ValueName, throwOnMissingValue: false); } catch { }

            // La sous-clé n'existait pas non plus et elle est restée vide : on la retire.
            if (!rec.SubKeyExisted && k.ValueCount == 0 && k.SubKeyCount == 0)
            {
                k.Dispose();
                try { root.DeleteSubKey(rec.SubKey, throwOnMissingSubKey: false); } catch { }
            }
            return;
        }

        var kind = Enum.TryParse<RegistryValueKind>(rec.PreviousKind, out var pk) ? pk : RegistryValueKind.String;
        object value = kind switch
        {
            RegistryValueKind.DWord => int.TryParse(rec.PreviousValue, out var i) ? i : 0,
            RegistryValueKind.QWord => long.TryParse(rec.PreviousValue, out var l) ? l : 0L,
            _ => rec.PreviousValue ?? ""
        };

        using var w = root.CreateSubKey(rec.SubKey, writable: true);
        w.SetValue(rec.ValueName, value, kind);
    }
}

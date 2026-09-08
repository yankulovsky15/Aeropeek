using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Aeropeek.Core;

public sealed record RestorePoint(uint Sequence, string Description, DateTime Created, uint Type)
{
    /// <summary>Constantes de SRSetRestorePoint. Un type inconnu est affiché tel quel.</summary>
    public string TypeLabel => Type switch
    {
        0 => "Installation d'application",
        1 => "Application uninstall",
        7 => "Checkpoint",
        10 => "Installation de pilote",
        12 => "Settings change",
        13 => "Operation undone",
        _ => $"Type {Type}"
    };
}

/// <summary>
/// Points de restauration système.
/// <para>
/// Le seul module d'Aeropeek qui ne repose pas sur son propre journal. Un point
/// de restauration couvre TOUT le système — pilotes, logiciels, registre — là où
/// le journal ne rend que ce que l'application a elle-même écrit. Les deux se
/// complètent : le journal est précis, le point de restauration est large.
/// </para>
/// <para>
/// Restaurer est la seule action de l'application qui touche la machine entière
/// et exige un redémarrage. Elle est donc confirmée à part, et jamais déclenchée
/// par un enchaînement automatique.
/// </para>
/// </summary>
public static class Restore
{
    const string Scope = @"\\.\root\default";
    const string SrKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    // SRSetRestorePoint
    const uint BeginSystemChange = 100;
    const uint ModifySettings = 12;

    public sealed record State(
        bool Available,      // la classe WMI répond
        bool Enabled,        // la protection n'est pas coupée
        string? Problem,     // ce qui empêche, en clair
        int DiskPercent,     // part du disque réservée
        int FrequencyMin);   // délai minimal entre deux points, 0 = aucun

    // ---------- état ----------

    public static State Read()
    {
        int freq = 1440, percent = 0;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(SrKey);
            if (k?.GetValue("SystemRestorePointCreationFrequency") is int f) freq = f;
            if (k?.GetValue("DisableSR") is int d && d == 1)
                return new State(true, false, "System protection is disabled.", 0, freq);
        }
        catch { }

        try
        {
            using var cfg = new ManagementObjectSearcher(Scope, "SELECT * FROM SystemRestoreConfig");
            foreach (ManagementObject mo in cfg.Get())
            {
                if (mo["DiskPercent"] is uint p) percent = (int)p;
                break;
            }
        }
        catch { }

        try
        {
            using var probe = new ManagementObjectSearcher(Scope, "SELECT SequenceNumber FROM SystemRestore");
            _ = probe.Get().Count;      // lève si l'accès est refusé ou le service coupé
            return new State(true, true, null, percent, freq);
        }
        catch (ManagementException me) when (me.ErrorCode == ManagementStatus.AccessDenied)
        {
            // WMI ne lève pas UnauthorizedAccessException : il encode le refus
            // dans son propre code d'erreur.
            return new State(true, false,
                "Access denied. Aeropeek must run as administrator to read restore points.",
                percent, freq);
        }
        catch (Exception ex)
        {
            return new State(false, false,
                "System Restore is not responding on this machine: " + ex.Message, percent, freq);
        }
    }

    // ---------- lecture ----------

    public static List<RestorePoint> List()
    {
        var list = new List<RestorePoint>();
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM SystemRestore");
            foreach (ManagementObject mo in searcher.Get())
            {
                uint seq = Convert.ToUInt32(mo["SequenceNumber"]);
                uint type = mo["RestorePointType"] is null ? 99u : Convert.ToUInt32(mo["RestorePointType"]);
                string desc = mo["Description"]?.ToString() ?? "";
                list.Add(new RestorePoint(seq, desc, ParseWmiDate(mo["CreationTime"]?.ToString()), type));
            }
        }
        catch { }

        return list.OrderByDescending(p => p.Created).ThenByDescending(p => p.Sequence).ToList();
    }

    /// <summary>
    /// WMI date au format yyyyMMddHHmmss.ffffff±UUU. On ne garde que la partie
    /// fixe : le décalage horaire est parfois rempli de zéros, ce qui fait
    /// échouer les analyseurs stricts.
    /// </summary>
    static DateTime ParseWmiDate(string? raw)
    {
        if (raw is { Length: >= 14 }
            && DateTime.TryParseExact(raw[..14], "yyyyMMddHHmmss",
                                      CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        return DateTime.MinValue;
    }

    // ---------- création ----------

    public static void Create(string description)
    {
        using var cls = new ManagementClass(new ManagementScope(Scope), new ManagementPath("SystemRestore"), null);
        var args = cls.GetMethodParameters("CreateRestorePoint");
        args["Description"] = description;
        args["EventType"] = BeginSystemChange;
        args["RestorePointType"] = ModifySettings;

        var result = cls.InvokeMethod("CreateRestorePoint", args, null);
        uint code = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
        if (code != 0) throw new InvalidOperationException(Explain(code));
    }

    static string Explain(uint code) => code switch
    {
        1058 => "The System Restore service is disabled on this machine.",
        1359 => "Windows refused the creation. A point may already have been created very recently.",
        _ => $"Windows refused to create the restore point (code {code})."
    };

    // ---------- activation de la protection ----------

    /// <summary>
    /// Active la protection sur le volume système.
    /// <para>
    /// Le lecteur n'est pas codé en dur : Windows n'est pas toujours sur C:.
    /// Activer la protection sur le mauvais volume échouerait, ou pire,
    /// protégerait un disque de données pendant que le système reste à nu.
    /// </para>
    /// </summary>
    public static void Enable(string? drive = null)
    {
        drive ??= (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + @"\";

        using var cls = new ManagementClass(new ManagementScope(Scope), new ManagementPath("SystemRestore"), null);
        var args = cls.GetMethodParameters("Enable");
        args["Drive"] = drive;
        args["WaitTillEnabled"] = true;

        var result = cls.InvokeMethod("Enable", args, null);
        uint code = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
        if (code != 0)
            throw new InvalidOperationException($"Windows refused to enable protection (code {code}).");
    }

    /// <summary>
    /// Par défaut Windows refuse un second point dans les 24 heures. Le réglage
    /// passe par le journal comme n'importe quel autre : il est annulable.
    /// </summary>
    public static OpRecord AllowFrequentPoints() =>
        RegistryOps.Apply("HKLM", SrKey, "SystemRestorePointCreationFrequency", 0,
                          RegistryValueKind.DWord, "restauration-frequence",
                          "Allow several restore points per day");

    // ---------- suppression ----------

    [DllImport("srclient.dll", SetLastError = true)]
    static extern int SRRemoveRestorePoint(int index);

    /// <summary>Supprime un point. Renvoie faux si Windows a refusé.</summary>
    public static bool Delete(uint sequence)
    {
        try { return SRRemoveRestorePoint((int)sequence) == 0; }
        catch { return false; }
    }

    public static (int Deleted, int Failed) DeleteAll(IEnumerable<RestorePoint> points)
    {
        int ok = 0, ko = 0;
        foreach (var p in points)
            if (Delete(p.Sequence)) ok++; else ko++;
        return (ok, ko);
    }

    // ---------- restauration ----------

    /// <summary>
    /// Demande la restauration. Windows ne l'exécute qu'au redémarrage suivant :
    /// tant que la machine n'a pas redémarré, rien n'a changé.
    /// </summary>
    public static void Apply(uint sequence)
    {
        using var cls = new ManagementClass(new ManagementScope(Scope), new ManagementPath("SystemRestore"), null);
        var args = cls.GetMethodParameters("Restore");
        args["SequenceNumber"] = sequence;

        var result = cls.InvokeMethod("Restore", args, null);
        uint code = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
        if (code != 0)
            throw new InvalidOperationException($"Windows refused the restore (code {code}).");
    }

    public static void Reboot()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe")
        {
            Arguments = "/r /t 5 /c \"Restauration système demandée par Aeropeek\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}

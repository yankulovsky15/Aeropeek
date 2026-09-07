using System.IO;
using System.Management;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Aeropeek.Core;

public sealed record DiskInfo(int Index, string Name, string MediaType, string BusType, bool IsNvme, bool IsSsd)
{
    public string Label => IsNvme ? "SSD NVMe" : IsSsd ? "SSD SATA" : MediaType;

    /// <summary>Ordre de rapidité : NVMe, puis SSD, puis le reste.</summary>
    public int Rank => IsNvme ? 3 : IsSsd ? 2 : 1;
}

public static class Storage
{
    /// <summary>Emplacement d'installation de CS2, trouvé via les bibliothèques Steam.</summary>
    public static string? GamePath(string appId = "730", string folderName = "Counter-Strike Global Offensive")
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steam = k?.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steam)) return null;
            steam = steam.Replace('/', '\\');

            var libraries = new List<string> { steam };

            // libraryfolders.vdf liste les autres disques utilisés par Steam
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), @"""path""\s*""([^""]+)"""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }

            foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(manifest)) continue;

                var install = Path.Combine(lib, "steamapps", "common", folderName);
                return Directory.Exists(install) ? install : lib;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Tous les disques physiques, avec leur nature réelle.</summary>
    public static List<DiskInfo> Disks()
    {
        var list = new List<DiskInfo>();
        try
        {
            using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk");
            foreach (ManagementObject mo in s.Get())
            {
                int index = int.TryParse(mo["DeviceId"] as string, out var i) ? i : -1;
                int media = ToInt(mo["MediaType"]);
                int bus = ToInt(mo["BusType"]);

                // MediaType : 3 = disque dur, 4 = SSD.  BusType : 17 = NVMe
                bool nvme = bus == 17;
                bool ssd = media == 4 || nvme;
                string mediaText = media switch { 3 => "disque dur", 4 => "SSD", _ => "type inconnu" };

                list.Add(new DiskInfo(index, (mo["FriendlyName"] as string ?? "").Trim(),
                    mediaText, bus == 17 ? "NVMe" : bus == 11 ? "SATA" : "", nvme, ssd));
            }
        }
        catch { }
        return list;
    }

    static int ToInt(object? o) { try { return o == null ? 0 : Convert.ToInt32(o); } catch { return 0; } }

    /// <summary>Disque physique portant une lettre de lecteur donnée.</summary>
    public static DiskInfo? DiskFor(char driveLetter)
    {
        try
        {
            // lettre → partition → disque physique
            using var parts = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (ManagementObject part in parts.Get())
            {
                using var drives = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject drive in drives.Get())
                {
                    int index = ToInt(drive["Index"]);
                    return Disks().FirstOrDefault(d => d.Index == index);
                }
            }
        }
        catch { }
        return null;
    }
}

public sealed record DriverInfo(string Name, string Version, DateTime? Date)
{
    public int? AgeMonths => Date == null ? null
        : (int)((DateTime.Now - Date.Value).TotalDays / 30.44);
}

public static class Drivers
{
    /// <summary>Version et date du pilote de la carte graphique dédiée.</summary>
    public static DriverInfo? Display()
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver " +
                "WHERE DeviceClass = 'DISPLAY'");

            DriverInfo? best = null;
            foreach (ManagementObject mo in s.Get())
            {
                string name = (mo["DeviceName"] as string ?? "").Trim();
                string version = (mo["DriverVersion"] as string ?? "").Trim();
                DateTime? date = null;
                try
                {
                    if (mo["DriverDate"] is string raw && raw.Length >= 8)
                        date = ManagementDateTimeConverter.ToDateTime(raw);
                }
                catch { }

                var info = new DriverInfo(name, version, date);

                // on privilégie la carte dédiée sur le circuit intégré
                bool dedicated = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                              || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                              || name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
                if (best == null || dedicated) best = info;
                if (dedicated) break;
            }
            return best;
        }
        catch { return null; }
    }

    /// <summary>
    /// Les pilotes NVIDIA portent une version Windows longue dont les cinq derniers
    /// chiffres forment le numéro commercial : 32.0.15.7602 → 576.02.
    /// </summary>
    public static string Pretty(string windowsVersion)
    {
        var digits = windowsVersion.Replace(".", "");
        if (digits.Length < 5) return windowsVersion;
        var five = digits[^5..];
        return $"{five[..3]}.{five[3..]}";
    }

    // ---------- version publiée par NVIDIA ----------

    public sealed record LatestDriver(string Version, string Date, string Url);

    /// <summary>
    /// Dernière version connue, renseignée uniquement après une recherche
    /// explicite. Aucun appel réseau n'a lieu sans que l'utilisateur le demande.
    /// </summary>
    public static LatestDriver? Latest { get; private set; }
    public static string? LastLookupError { get; private set; }

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Aeropeek)");
        return c;
    }

    /// <summary>
    /// Interroge NVIDIA pour la carte détectée. Deux étapes : résoudre la série
    /// puis le modèle exact dans les tables publiques, avant de demander le pilote.
    /// Ainsi rien n'est codé en dur et la liste reste juste quand de nouvelles
    /// cartes sortent.
    /// </summary>
    public static async Task<LatestDriver?> CheckLatestAsync(string gpuName, CancellationToken ct = default)
    {
        Latest = null;
        LastLookupError = null;

        try
        {
            if (!gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                && !gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            {
                LastLookupError = "La recherche automatique n'est disponible que pour les cartes NVIDIA.";
                return null;
            }

            // « NVIDIA GeForce RTX 4070 » → série « RTX 40 », modèle « RTX 4070 »
            var model = System.Text.RegularExpressions.Regex.Match(gpuName, @"(RTX|GTX)\s*(\d{3,4})\s*(Ti\s*SUPER|SUPER|Ti)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!model.Success) { LastLookupError = "Modèle de carte non reconnu."; return null; }

            string family = model.Groups[1].Value.ToUpperInvariant();
            string number = model.Groups[2].Value;
            string suffix = model.Groups[3].Value.Trim();
            // NVIDIA nomme ses séries « RTX 40 Series » pour les modèles à quatre
            // chiffres, et « 900 Series » pour les anciens à trois.
            string seriesKey = number.Length >= 4
                ? $"{family} {number[..2]}"      // 4070 → « RTX 40 »
                : $"{number[..1]}00";            // 970  → « 900 »

            int? psid = await LookupIdAsync($"https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2",
                n => n.Contains(seriesKey, StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Notebook", StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Laptop", StringComparison.OrdinalIgnoreCase), ct);
            if (psid == null) { LastLookupError = "Série de carte introuvable chez NVIDIA."; return null; }

            string wanted = $"{family} {number}" + (suffix.Length > 0 ? " " + suffix : "");
            int? pfid = await LookupIdAsync(
                $"https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3&ParentID={psid}",
                n => Normalise(n).EndsWith(Normalise(wanted), StringComparison.OrdinalIgnoreCase), ct);
            if (pfid == null) { LastLookupError = "Modèle de carte introuvable chez NVIDIA."; return null; }

            int lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? 1036 : 1033;
            var url = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php"
                    + $"?func=DriverManualLookup&psid={psid}&pfid={pfid}&osID=135&languageCode={lang}"
                    + "&isWHQL=0&dch=1&sort1=0&numberOfResults=1";

            using var doc = System.Text.Json.JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            if (!doc.RootElement.TryGetProperty("IDS", out var ids) || ids.GetArrayLength() == 0)
            { LastLookupError = "NVIDIA n'a renvoyé aucun pilote pour cette carte."; return null; }

            var info = ids[0].GetProperty("downloadInfo");
            Latest = new LatestDriver(
                info.GetProperty("Version").GetString() ?? "",
                info.TryGetProperty("ReleaseDateTime", out var d) ? d.GetString() ?? "" : "",
                info.TryGetProperty("DownloadURL", out var u) ? u.GetString() ?? "" : "");
            return Latest;
        }
        catch (Exception ex)
        {
            LastLookupError = "La recherche a échoué : " + ex.Message;
            return null;
        }
    }

    static string Normalise(string s) =>
        s.Replace("NVIDIA", "", StringComparison.OrdinalIgnoreCase)
         .Replace("GeForce", "", StringComparison.OrdinalIgnoreCase)
         .Replace(" ", "").Trim();

    /// <summary>Les tables d'identifiants de NVIDIA sont renvoyées en XML.</summary>
    static async Task<int?> LookupIdAsync(string url, Func<string, bool> match, CancellationToken ct)
    {
        var xml = System.Xml.Linq.XDocument.Parse(await Http.GetStringAsync(url, ct));
        foreach (var e in xml.Descendants("LookupValue"))
        {
            string name = e.Element("Name")?.Value ?? "";
            if (!match(name)) continue;
            if (int.TryParse(e.Element("Value")?.Value, out var v)) return v;
        }
        return null;
    }

    /// <summary>Compare deux numéros commerciaux NVIDIA du type « 616.56 ».</summary>
    public static int Compare(string a, string b)
    {
        static double Parse(string s) =>
            double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1;
        return Parse(a).CompareTo(Parse(b));
    }
}

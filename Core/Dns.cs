using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace Aeropeek.Core;

public sealed record NetAdapter(string Name, string Description, string Guid, string[] Dns,
                               bool FromDhcp, bool Wireless)
{
    public string DnsText => Dns.Length == 0 ? "none" : string.Join(" / ", Dns);
}

public sealed record DnsProvider(string Name, string Primary, string Secondary, string Note, string[] Tags,
                                string Accent, string Icon)
{
    public bool IsDhcp => Primary.Length == 0;
}

/// <summary>
/// Serveurs DNS. Le trafic d'une partie ne passe pas par le DNS : celui-ci ne sert
/// qu'à traduire les noms en adresses, une fois au lancement. Changer de DNS accélère
/// l'ouverture des pages et des lanceurs, pas le ping en jeu.
/// </summary>
public static class DnsOps
{
    public static readonly DnsProvider[] Providers =
    {
        new("Cloudflare", "1.1.1.1", "1.0.0.1",
            "Fast, with no query logging.",
            new[] { "Fast", "Privacy" }, "#F6821F", "M 7,18 H 17.3 A 3.6,3.6 0 0 0 17.3,10.8 A 5.6,5.6 0 0 0 6.8,9.9 A 4.1,4.1 0 0 0 7,18 Z"),
        new("Google", "8.8.8.8", "8.8.4.4",
            "Very widespread and reliable, but Google keeps query data.",
            new[] { "Reliable", "Widespread" }, "#5B9BF8", "M 3,12 A 9,9 0 1 0 21,12 A 9,9 0 1 0 3,12 M 3,12 H 21 M 12,3 A 14,14 0 0 1 12,21 A 14,14 0 0 1 12,3"),
        new("Quad9", "9.9.9.9", "149.112.112.112",
            "Blocks known malicious domains.",
            new[] { "Security", "Filtering" }, "#B98CF7", "M 12,3 L 20,6 V 12 C 20,17 16.6,20.2 12,21 C 7.4,20.2 4,17 4,12 V 6 Z"),
        new("AdGuard", "94.140.14.14", "94.140.15.15",
            "Blocks ads and trackers at the domain-name level.",
            new[] { "Ad-free", "Filtering" }, "#6BC97F", "M 7,18 H 17.3 A 3.6,3.6 0 0 0 17.3,10.8 A 5.6,5.6 0 0 0 6.8,9.9 A 4.1,4.1 0 0 0 7,18 Z"),
        new("OpenDNS", "208.67.222.222", "208.67.220.220",
            "Configurable content filtering, owned by Cisco.",
            new[] { "Filtering", "Security" }, "#3BB6E8", "M 12,3 L 20,6 V 12 C 20,17 16.6,20.2 12,21 C 7.4,20.2 4,17 4,12 V 6 Z"),
        new("Automatic", "", "",
            "The ones your router or provider hands out. Often the closest.",
            new[] { "Default", "DHCP" }, "#8497AC", "M 9,12 A 3,3 0 1 0 15,12 A 3,3 0 1 0 9,12 M 12,2 V 5 M 12,19 V 22 M 4.2,6.6 L 6.3,8.7 M 17.7,15.3 L 19.8,17.4 M 2,12 H 5 M 19,12 H 22 M 4.2,17.4 L 6.3,15.3 M 17.7,8.7 L 19.8,6.6")
    };

    // ---------- lecture ----------

    const System.Net.Sockets.AddressFamily V4 = System.Net.Sockets.AddressFamily.InterNetwork;

    /// <summary>
    /// Cartes réseau réellement utilisables. Windows expose une pseudo-carte par
    /// filtre installé — plateforme de filtrage, antivirus, ordonnanceur de paquets,
    /// Wi-Fi Direct — qui remontent toutes comme « actives » sans porter de
    /// configuration. Seules celles qui ont une adresse IPv4 routable comptent.
    /// </summary>
    public static List<NetAdapter> Adapters()
    {
        var list = new List<NetAdapter>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet
                                             or NetworkInterfaceType.GigabitEthernet
                                             or NetworkInterfaceType.Wireless80211)) continue;

            var props = ni.GetIPProperties();

            // une adresse IPv4, et pas une auto-attribuée faute de DHCP
            var v4 = props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == V4)
                .Select(a => a.Address.ToString())
                .Where(a => !a.StartsWith("169.254."))
                .ToArray();
            if (v4.Length == 0) continue;

            // une passerelle, sinon la carte ne mène nulle part
            bool routable = props.GatewayAddresses
                .Any(g => g.Address.AddressFamily == V4 && !g.Address.ToString().StartsWith("0."));
            if (!routable) continue;

            var dns = props.DnsAddresses
                .Where(a => a.AddressFamily == V4)
                .Select(a => a.ToString())
                .ToArray();

            list.Add(new NetAdapter(ni.Name, ni.Description, ni.Id, dns,
                StaticServers(ni.Id).Length == 0,
                ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
        }

        return list;
    }

    /// <summary>
    /// Serveurs DNS écrits en dur pour cette interface. Vide signifie « fournis par
    /// DHCP » : c'est cette distinction qui permet une annulation exacte.
    /// </summary>
    static string[] StaticServers(string guid)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}");
            var v = k?.GetValue("NameServer") as string ?? "";
            return v.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch { return Array.Empty<string>(); }
    }

    public static async Task<long> PingAsync(string address, int attempts = 3)
    {
        if (string.IsNullOrEmpty(address)) return -1;
        long best = -1;
        using var ping = new Ping();
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(address, 1200);
                if (r.Status == IPStatus.Success && (best < 0 || r.RoundtripTime < best))
                    best = r.RoundtripTime;
            }
            catch { }
        }
        return best;
    }

    // ---------- écriture ----------

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

    static (int Code, string Output) Netsh(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = OemEncoding, StandardErrorEncoding = OemEncoding
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return (-1, "");
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return (p.ExitCode, o);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    static void Write(string adapter, string[] servers)
    {
        if (servers.Length == 0)
        {
            var (c, o) = Netsh("interface", "ipv4", "set", "dnsservers", "name=" + adapter, "source=dhcp");
            if (c != 0) throw new InvalidOperationException("netsh refused. " + Shorten(o));
            return;
        }

        var (code, output) = Netsh("interface", "ipv4", "set", "dnsservers",
            "name=" + adapter, "source=static", "address=" + servers[0], "register=primary", "validate=no");
        if (code != 0) throw new InvalidOperationException("netsh refused. " + Shorten(output));

        for (int i = 1; i < servers.Length; i++)
            Netsh("interface", "ipv4", "add", "dnsservers",
                "name=" + adapter, "address=" + servers[i], "index=" + (i + 1), "validate=no");
    }

    public static OpRecord Apply(NetAdapter adapter, DnsProvider provider)
    {
        var previous = StaticServers(adapter.Guid);      // vide = DHCP
        var target = provider.IsDhcp
            ? Array.Empty<string>()
            : new[] { provider.Primary, provider.Secondary }.Where(s => s.Length > 0).ToArray();

        Write(adapter.Name, target);

        return new OpRecord
        {
            Kind = "dns",
            TweakId = "dns",
            Description = $"DNS for “{adapter.Name}”: {provider.Name}",
            SubKey = adapter.Name,
            ValueName = adapter.Guid,
            Existed = previous.Length > 0,
            PreviousValue = string.Join(",", previous),
            NewValue = target.Length == 0 ? "dhcp" : string.Join(",", target)
        };
    }

    public static void Revert(OpRecord rec)
    {
        if (rec.Kind != "dns") return;
        var previous = (rec.PreviousValue ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        // Aucun serveur statique avant : on rend la main au DHCP.
        Write(rec.SubKey, previous);
    }

    static string Shorten(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" : s.Trim().Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
}

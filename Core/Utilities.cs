using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aeropeek.Core;

public enum UtilityKind
{
    /// <summary>Ouvre un outil de Windows et rend la main.</summary>
    Launch,
    /// <summary>Exécute une commande et affiche sa sortie au fur et à mesure.</summary>
    Run,
    /// <summary>Redémarre le pilote graphique par le raccourci système.</summary>
    DriverRestart,
    /// <summary>Télécharge un outil tiers, vérifie son origine, puis le lance.</summary>
    External
}

public sealed record Utility(
    string Id,
    string Title,
    string Description,
    string Display,          // ce qui est montré à l'utilisateur
    string Exe,
    string[] Args,
    UtilityKind Kind,
    string ActionLabel,
    string Icon,
    string? Caution = null,
    string? Url = null,       // outil tiers : adresse de téléchargement
    string? Signer = null);   // signataire attendu ; null quand l'éditeur ne signe pas

public static class Utilities
{
    const string IcoDisk = "M 3,7 A 2,2 0 0 1 5,5 H 19 A 2,2 0 0 1 21,7 V 17 A 2,2 0 0 1 19,19 H 5 A 2,2 0 0 1 3,17 Z M 7,15 H 7.01 M 11,15 H 17";
    const string IcoScreen = "M 3,4 H 21 V 16 H 3 Z M 8,20 H 16 M 12,16 V 20";
    const string IcoGpu = "M 2,7 H 22 V 17 H 2 Z M 7,12 H 10 M 14,12 H 17";
    const string IcoGlobe = "M 3,12 A 9,9 0 1 0 21,12 A 9,9 0 1 0 3,12 M 3,12 H 21 M 12,3 A 14,14 0 0 1 12,21 A 14,14 0 0 1 12,3";
    const string IcoWrench = "M 14.5,4.5 A 5,5 0 0 0 19.5,12.5 L 12.5,19.5 A 2.5,2.5 0 0 1 9,16 L 16,9 A 5,5 0 0 0 14.5,4.5 Z";
    const string IcoShield = "M 12,3 L 20,6 V 12 C 20,17 16.6,20.2 12,21 C 7.4,20.2 4,17 4,12 V 6 Z M 8.5,12.2 L 11,14.7 L 15.5,10";
    const string IcoSearch = "M 4,11 A 7,7 0 1 0 18,11 A 7,7 0 1 0 4,11 M 16,16 L 21,21";
    const string IcoEye = "M 2,12 C 5,7 8.5,5 12,5 C 15.5,5 19,7 22,12 C 19,17 15.5,19 12,19 "
                        + "C 8.5,19 5,17 2,12 Z M 9.4,12 A 2.6,2.6 0 1 0 14.6,12 A 2.6,2.6 0 1 0 9.4,12 "
                        + "M 4,20 L 20,4";
    const string IcoBox = "M 12,2.5 L 20.5,7 V 17 L 12,21.5 L 3.5,17 V 7 Z "
                        + "M 3.5,7 L 12,11.5 L 20.5,7 M 12,11.5 V 21.5";

    public static readonly Utility[] All =
    {
        new("cleanmgr", "Windows Disk Cleanup",
            "Opens Microsoft's own tool. It offers locations Aeropeek does not touch, such as restore points.",
            "cleanmgr", "cleanmgr.exe", Array.Empty<string>(),
            UtilityKind.Launch, "Open", IcoDisk),

        new("msinfo32", "System information",
            "Windows' detailed report: hardware, drivers, components, software environment.",
            "msinfo32", "msinfo32.exe", Array.Empty<string>(),
            UtilityKind.Launch, "Open", IcoScreen),

        new("gpu-restart", "Restart the graphics driver",
            "Resets the driver without rebooting the PC. Useful when the display freezes or flickers.",
            "Win + Ctrl + Shift + B", "", Array.Empty<string>(),
            UtilityKind.DriverRestart, "Restart", IcoGpu,
            "The screen goes black for a second. Close your games first: some do not survive the reset."),

        new("flushdns", "Flush the DNS cache",
            "Clears the remembered name-to-address mappings. Worth doing when a site stays unreachable while it works elsewhere.",
            "ipconfig /flushdns", "ipconfig.exe", new[] { "/flushdns" },
            UtilityKind.Run, "Flush", IcoGlobe),

        new("sfc", "System File Checker",
            "Checks the integrity of Windows files and repairs damaged ones.",
            "sfc /scannow", "sfc.exe", new[] { "/scannow" },
            UtilityKind.Run, "Check", IcoWrench,
            "Allow five to fifteen minutes. Leave the window open to the end."),

        new("dism", "Windows image repair",
            "Repairs the component store the file checker depends on. Run it when that one fails.",
            "DISM /Online /Cleanup-Image /RestoreHealth", "dism.exe",
            new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" },
            UtilityKind.Run, "Repair", IcoShield,
            "Needs an internet connection and can take more than fifteen minutes."),

        new("chkdsk", "Disk check",
            "Scans the file system for errors. Read-only: nothing is changed.",
            "chkdsk C:", "chkdsk.exe", new[] { @"C:" },
            UtilityKind.Run, "Scan", IcoSearch,
            "Read-only. A real repair would require a restart, which Aeropeek does not trigger."),

        // --- outils tiers ---
        // Ils font ce qu'Aeropeek ne fait pas, et le font hors de son journal.
        // La carte le dit, et le dialogue de confirmation le redit.

        new("oosu10", "O&O ShutUp10++",
            "The reference privacy tool for Windows: hundreds of telemetry settings, "
            + "far beyond the four Aeropeek handles. Portable, free, no installation.",
            "oo-software.com", "", Array.Empty<string>(),
            UtilityKind.External, "Run", IcoEye,
            "About 76 MB on first run. Its changes will not appear in Aeropeek's journal "
            + "and cannot be undone from here: use its own restore function.",
            "https://dl5.oo-software.com/files/ooshutup10/OOSU10.exe",
            "O&O Software"),

        new("winutil", "Chris Titus Windows Utility",
            "The Windows swiss army knife: bulk software installation, system repairs, "
            + "update policy, and a long list of adjustments Aeropeek does not cover.",
            "christitus.com", "", Array.Empty<string>(),
            UtilityKind.External, "Run", IcoBox,
            "Unsigned PowerShell script: Aeropeek verifies the download address, not the author. "
            + "Its changes escape the journal. Create a restore point first — it offers to.",
            "https://github.com/ChrisTitusTech/winutil/releases/latest/download/winutil.ps1")
    };

    // ---------- exécution ----------

    public static void Launch(Utility u)
    {
        Process.Start(new ProcessStartInfo(u.Exe) { UseShellExecute = true });
    }

    /// <summary>
    /// Exécute la commande et transmet sa sortie ligne par ligne. Les outils console
    /// écrivent leur progression avec des retours chariot : on découpe donc sur \r
    /// autant que sur \n, sinon rien ne s'affiche avant la fin.
    /// </summary>
    public static async Task<int> RunAsync(Utility u, IProgress<string> output, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(u.Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Oem,
            StandardErrorEncoding = Oem
        };
        foreach (var a in u.Args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("The command could not be started.");

        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });

        var pump = Task.Run(async () =>
        {
            var buf = new System.Text.StringBuilder();
            var reader = proc.StandardOutput;
            char[] chunk = new char[256];
            int n;
            while ((n = await reader.ReadAsync(chunk, 0, chunk.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    char c = chunk[i];
                    if (c is '\r' or '\n')
                    {
                        var line = buf.ToString().Trim();
                        buf.Clear();
                        if (line.Length > 0) output.Report(line);
                    }
                    else buf.Append(c);
                }
            }
            var last = buf.ToString().Trim();
            if (last.Length > 0) output.Report(last);
        }, ct);

        string err = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        await pump;

        foreach (var line in err.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.Trim().Length > 0) output.Report(line.Trim());

        return proc.ExitCode;
    }

    static readonly System.Text.Encoding Oem = GetOem();

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

    // ---------- redémarrage du pilote graphique ----------

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    const byte VkWin = 0x5B, VkCtrl = 0x11, VkShift = 0x10, VkB = 0x42;
    const uint KeyUp = 0x0002;

    /// <summary>
    /// Envoie Win + Ctrl + Maj + B, le raccourci de Windows qui réinitialise le
    /// pilote graphique. Aucune écriture, rien à annuler.
    /// </summary>
    public static void RestartGraphicsDriver()
    {
        keybd_event(VkWin, 0, 0, UIntPtr.Zero);
        keybd_event(VkCtrl, 0, 0, UIntPtr.Zero);
        keybd_event(VkShift, 0, 0, UIntPtr.Zero);
        keybd_event(VkB, 0, 0, UIntPtr.Zero);

        keybd_event(VkB, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkShift, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkCtrl, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkWin, 0, KeyUp, UIntPtr.Zero);
    }
}

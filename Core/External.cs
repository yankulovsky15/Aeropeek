using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Aeropeek.Core;

/// <summary>
/// Téléchargement et lancement d'un outil tiers.
/// <para>
/// Ces outils ne sont pas d'Aeropeek : ils écrivent où ils veulent, et le
/// journal n'en saura rien. C'est la seule partie de l'application dont les
/// effets ne sont ni mesurés ni annulables — l'interface doit le dire, et le
/// dialogue de confirmation le redit avant chaque lancement.
/// </para>
/// <para>
/// La seule garantie apportée ici est l'origine du fichier : quand l'éditeur
/// signe son binaire, la signature est vérifiée avant exécution et un fichier
/// qui échoue n'est jamais lancé. Quand il ne signe pas, l'application le dit
/// au lieu de laisser croire à un contrôle qui n'a pas eu lieu.
/// </para>
/// </summary>
public static class External
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>Au-delà, on retélécharge : ces outils évoluent vite.</summary>
    static readonly TimeSpan Fresh = TimeSpan.FromDays(7);

    public static string ToolsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aeropeek", "outils");

    public static async Task<int> FetchAndRunAsync(Utility u, IProgress<string> log, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(u.Url))
            throw new InvalidOperationException("Aucune source n'est définie pour cet outil.");

        Directory.CreateDirectory(ToolsDir);
        string name = Path.GetFileName(new Uri(u.Url).LocalPath);
        string path = Path.Combine(ToolsDir, name);

        log.Report($"Source   {u.Url}");
        log.Report($"Dossier  {ToolsDir}");
        log.Report("");

        var existing = new FileInfo(path);
        bool reuse = existing.Exists && DateTime.UtcNow - existing.LastWriteTimeUtc < Fresh;

        if (reuse)
        {
            log.Report($"Copie locale du {existing.LastWriteTime:dd/MM/yyyy à HH\\hmm} réutilisée "
                     + $"({Size(existing.Length)}).");
        }
        else
        {
            log.Report(existing.Exists ? "Copie locale trop ancienne, téléchargement…" : "Téléchargement…");
            await DownloadAsync(u.Url, path, log, ct);
        }

        log.Report("");

        // --- origine du fichier ---
        var (trusted, subject) = Authenticode.Check(path);

        if (u.Signer != null)
        {
            if (!trusted)
                throw new InvalidOperationException(
                    "La signature du fichier téléchargé n'est pas valide. Rien n'a été exécuté.");

            if (subject == null || subject.IndexOf(u.Signer, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(
                    $"Le fichier est signé par « {subject ?? "signataire inconnu"} », "
                    + $"et non par « {u.Signer} » comme attendu. Rien n'a été exécuté.");

            log.Report($"Signature vérifiée : {subject}");
        }
        else if (trusted && subject != null)
        {
            log.Report($"Fichier signé par : {subject}");
        }
        else
        {
            log.Report("Ce fichier n'est pas signé : Aeropeek ne peut pas vérifier qu'il vient bien");
            log.Report("de son auteur. Tu l'exécutes sur la foi de son adresse de téléchargement.");
        }

        log.Report("");
        log.Report("Lancement de l'outil…");

        Start(path);

        log.Report("");
        log.Report("L'outil s'exécute maintenant dans sa propre fenêtre.");
        log.Report("Ce qu'il modifiera n'apparaîtra pas dans le journal d'Aeropeek");
        log.Report("et ne pourra pas être annulé depuis cette application.");
        return 0;
    }

    static async Task DownloadAsync(string url, string path, IProgress<string> log, CancellationToken ct)
    {
        // Écriture sous un nom temporaire : une coupure de réseau ne doit pas
        // laisser un fichier tronqué que le prochain lancement croirait complet.
        string tmp = path + ".part";

        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            if (total is > 0) log.Report($"Taille annoncée : {Size(total.Value)}");

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);

            var buffer = new byte[81920];
            long done = 0;
            int lastPct = -1, n;

            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;

                if (total is > 0)
                {
                    int pct = (int)(done * 100 / total.Value);
                    if (pct >= lastPct + 10) { lastPct = pct; log.Report($"  {pct} %  ({Size(done)})"); }
                }
            }
        }

        File.Move(tmp, path, overwrite: true);
        log.Report($"Reçu : {Size(new FileInfo(path).Length)}");
    }

    /// <summary>
    /// Lance l'outil. Un script PowerShell passe par l'interpréteur, un
    /// exécutable démarre directement. Dans les deux cas le processus hérite
    /// des privilèges d'Aeropeek, qui tourne en administrateur.
    /// </summary>
    static void Start(string path)
    {
        ProcessStartInfo psi;

        if (Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = true };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(path);
        }
        else
        {
            psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path)!
            };
        }

        Process.Start(psi);
    }

    static string Size(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.#} Mo"
        : $"{bytes / 1024d:0} Ko";
}

/// <summary>
/// Vérification Authenticode par WinVerifyTrust — le même contrôle que celui
/// que Windows applique lui-même. Lire le certificat ne suffirait pas : un
/// fichier modifié après signature porte toujours son certificat.
/// </summary>
static class Authenticode
{
    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    const uint UiNone = 2, RevokeNone = 0, ChoiceFile = 1, ActionVerify = 1, ActionClose = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct FileInfoNative
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

    public static (bool Trusted, string? Subject) Check(string path)
    {
        IntPtr pFile = IntPtr.Zero, pData = IntPtr.Zero;
        try
        {
            var file = new FileInfoNative
            {
                cbStruct = (uint)Marshal.SizeOf<FileInfoNative>(),
                pcwszFilePath = path
            };
            pFile = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
            Marshal.StructureToPtr(file, pFile, false);

            var data = new TrustData
            {
                cbStruct = (uint)Marshal.SizeOf<TrustData>(),
                dwUIChoice = UiNone,
                fdwRevocationChecks = RevokeNone,
                dwUnionChoice = ChoiceFile,
                pFile = pFile,
                dwStateAction = ActionVerify
            };
            pData = Marshal.AllocHGlobal(Marshal.SizeOf<TrustData>());
            Marshal.StructureToPtr(data, pData, false);

            uint result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);

            // L'API exige qu'on referme l'état qu'elle vient d'ouvrir.
            var close = Marshal.PtrToStructure<TrustData>(pData);
            close.dwStateAction = ActionClose;
            Marshal.StructureToPtr(close, pData, false);
            WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);

            return (result == 0, Subject(path));
        }
        catch { return (false, null); }
        finally
        {
            if (pData != IntPtr.Zero) { Marshal.DestroyStructure<TrustData>(pData); Marshal.FreeHGlobal(pData); }
            if (pFile != IntPtr.Zero) { Marshal.DestroyStructure<FileInfoNative>(pFile); Marshal.FreeHGlobal(pFile); }
        }
    }

    static string? Subject(string path)
    {
        try
        {
            // CreateFromSignedFile est marquée obsolète au profit de
            // X509CertificateLoader, qui charge des certificats mais ne sait pas
            // lire la signature d'un exécutable : il n'existe pas de remplaçant
            // pour cet usage. La confiance vient de WinVerifyTrust ci-dessus ;
            // on ne lit ici que le nom du signataire, pour l'afficher et le
            // comparer à celui attendu.
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch { return null; }
    }
}

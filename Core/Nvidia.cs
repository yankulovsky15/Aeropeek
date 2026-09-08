using System.Runtime.InteropServices;

namespace Aeropeek.Core;

public sealed record NvSetting(
    uint Id,
    string Name,
    uint Value,
    string Reading,
    bool Optimal,
    string Note = "",
    bool Writable = false);

/// <summary>
/// Lecture des réglages du pilote NVIDIA par NvAPI, et écriture du seul réglage
/// dont la table de valeurs est certaine.
/// <para>
/// Le pilote fournit le nom de chaque réglage — « Power management mode » — mais
/// pas la signification de ses valeurs : rien dans l'interface ne dit que « 1 »
/// veut dire « performances maximales ». Les interprétations ci-dessous viennent
/// de la documentation NVIDIA. Pour la plupart des réglages, cela suffit à
/// afficher, pas à écrire : se tromper de constante donnerait un pilote au
/// comportement inexplicable, sans que l'utilisateur sache d'où cela vient.
/// </para>
/// <para>
/// Un seul réglage échappe à cette règle — la gestion de l'alimentation. Sa table
/// de valeurs est documentée, son effet est mesurable, et il se remet en place
/// d'un clic. Le reste renvoie au panneau NVIDIA.
/// </para>
/// </summary>
public static class Nvidia
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr QueryInterface(uint id);

    static T? Fn<T>(uint id) where T : Delegate
    {
        try
        {
            var p = QueryInterface(id);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
        }
        catch { return null; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_Unload();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DriverVer(out uint ver, [Out] byte[] branch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DrsCreate(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DrsLoad(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DrsBase(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DrsDestroy(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DrsGetSetting(IntPtr session, IntPtr profile, uint id, ref NVDRS_SETTING setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DrsSetSetting(IntPtr session, IntPtr profile, ref NVDRS_SETTING setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D_DrsSaveSettings(IntPtr session);

    /// <summary>
    /// Disposition validée par le pilote lui-même : il refuse la structure si le
    /// champ de version — qui encode sa taille — ne correspond pas exactement.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct NVDRS_SETTING
    {
        public uint version;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2048)] public ushort[] settingName;
        public uint settingId;
        public uint settingType;
        public uint settingLocation;
        public uint isCurrentPredefined;
        public uint isPredefinedValid;
        public uint u32PredefinedValue;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)] public byte[] predefinedRest;
        public uint u32CurrentValue;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)] public byte[] currentRest;
    }

    public static bool Available => Fn<D_Init>(0x0150E828) != null;

    // ---------- ouverture du panneau NVIDIA ----------

    static readonly string[] ClassicPaths =
    {
        @"%ProgramFiles%\NVIDIA Corporation\Control Panel Client\nvcplui.exe",
        @"%ProgramFiles%\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA app.exe",
        @"%ProgramFiles%\NVIDIA Corporation\NVIDIA App\NVIDIA app.exe",
        @"%ProgramFiles(x86)%\NVIDIA Corporation\Control Panel Client\nvcplui.exe"
    };

    /// <summary>
    /// Ouvre le panneau de configuration NVIDIA. Il existe en trois formes selon
    /// les versions de pilote — application classique, NVIDIA App, ou paquet du
    /// Microsoft Store — et seule la dernière est présente sur les installations
    /// récentes. On les essaie dans l'ordre, sans rien coder en dur.
    /// </summary>
    public static bool OpenControlPanel()
    {
        foreach (var raw in ClassicPaths)
        {
            var path = Environment.ExpandEnvironmentVariables(raw);
            if (!System.IO.File.Exists(path)) continue;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        // Version du Store : on la lance par son identifiant d'application,
        // trouvé dans le registre plutôt que supposé.
        var family = StorePackageFamily();
        if (family != null)
        {
            try
            {
                string aumid = $"{family}!NVIDIACorp.NVIDIAControlPanel";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"shell:appsFolder\\{aumid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch { }
        }

        return false;
    }

    static string? StorePackageFamily()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData");
            return k?.GetSubKeyNames()
                    .FirstOrDefault(n => n.StartsWith("NVIDIACorp.NVIDIAControlPanel", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    public static string? DriverVersion()
    {
        var init = Fn<D_Init>(0x0150E828);
        if (init == null || init() != 0) return null;
        try
        {
            var f = Fn<D_DriverVer>(0x2926AAAD);
            if (f == null) return null;
            var branch = new byte[64];
            return f(out uint v, branch) == 0 ? $"{v / 100}.{v % 100:00}" : null;
        }
        finally { Fn<D_Unload>(0xD22BDD7E)?.Invoke(); }
    }

    /// <summary>Gestion de l'alimentation. Le seul réglage qu'Aeropeek sait écrire.</summary>
    public const uint PowerModeId = 0x1057EB71;

    /// <summary>« Privilégier les performances maximales ».</summary>
    public const uint PowerModeMax = 1;

    // Interprétations issues de la documentation NVIDIA, pas du pilote. Les noms
    // affichés, eux, viennent du pilote : c'est lui qui les fournit.
    static readonly (uint Id, Func<uint, (string Reading, bool Optimal, string Note)> Read)[] Known =
    {
        (PowerModeId, v => (v switch
        {
            0 => "Adaptatif",
            1 => "Prefer maximum performance",
            2 => "Driver controlled",
            3 => "Performances constantes",
            _ => $"valeur {v}"
        }, v == PowerModeMax,
           "In adaptive mode the card drops its clocks between frames and takes a moment to climb back. "
           + "It is the only setting on this list whose effect shows up on the 1% lows.")),

        (0x007BA09E, v => (v == 0 ? "Left to the game" : $"{v} image(s)", true,
            "A pre-Reflex setting. When the game manages its own queue — which CS2 does with Reflex — "
            + "forcing it here achieves nothing.")),

        (0x00CE2691, v => (v switch
        {
            0x14 => "High quality",
            0x10 => "Quality",
            0x0A => "Performance",
            0x00 => "Hautes performances",
            _ => $"valeur {v}"
        }, true,
           "The difference is invisible in game, and free on a recent card.")),

        // Nom donné par le pilote : « Threaded optimization ». Les constantes
        // 1 et 2 sont celles de la documentation ; 0 signifie « laissé au pilote ».
        (0x20C1221E, v => (v switch
        {
            0 => "Automatique",
            1 => "On",
            2 => "Off",
            _ => $"valeur {v}"
        }, true,
           "Applies to OpenGL rendering. CS2 uses Direct3D: this setting does not affect it.")),

        // Nom donné par le pilote : « Vertical Sync ». Ces constantes sont des
        // valeurs magiques : on n'interprète que celles dont on est sûr, et on
        // affiche la valeur brute pour les autres plutôt que de deviner.
        (0x00A879CF, v => (v switch
        {
            0x08416747 => "Forced off",
            0x60925591 => "Left to the game",
            _ => $"valeur 0x{v:X8}"
        }, true,
           "The game already decides in its own options. The driver only serves to override it."))
    };

    /// <summary>
    /// Écrit la gestion de l'alimentation dans le profil global du pilote, et
    /// renvoie de quoi revenir exactement à la valeur précédente.
    /// <para>
    /// Aucun autre réglage n'est acceptés ici : la garantie « Aeropeek n'écrit que
    /// ce dont il connaît la table de valeurs » doit tenir dans le code, pas
    /// seulement dans la documentation.
    /// </para>
    /// </summary>
    public static OpRecord SetPowerMode(uint value)
    {
        if (value > 3)
            throw new InvalidOperationException($"Value outside the power-management table: {value}");

        var init = Fn<D_Init>(0x0150E828);
        if (init == null || init() != 0)
            throw new InvalidOperationException("The NVIDIA driver did not respond.");

        try
        {
            var create = Fn<D_DrsCreate>(0x0694D52E);
            var load = Fn<D_DrsLoad>(0x375DBD6B);
            var basep = Fn<D_DrsBase>(0xDA8466A0);
            var get = Fn<D_DrsGetSetting>(0x73BF8338);
            var set = Fn<D_DrsSetSetting>(0x577DD202);
            var save = Fn<D_DrsSaveSettings>(0xFCBC7E14);
            var destroy = Fn<D_DrsDestroy>(0xDAD9CFF8);

            if (create == null || load == null || basep == null || get == null || set == null || save == null)
                throw new InvalidOperationException("This NVIDIA driver version does not expose profile writing.");

            if (create(out IntPtr session) != 0)
                throw new InvalidOperationException("Could not open an NvAPI session.");

            try
            {
                if (load(session) != 0 || basep(session, out IntPtr profile) != 0 || profile == IntPtr.Zero)
                    throw new InvalidOperationException("Profil global du pilote illisible.");

                uint version = (uint)(Marshal.SizeOf<NVDRS_SETTING>() | (1 << 16));

                // On relit l'état exact avant d'écrire : c'est lui qui sera restauré,
                // et non une valeur « par défaut » supposée.
                var before = new NVDRS_SETTING { version = version };
                if (get(session, profile, PowerModeId, ref before) != 0)
                    throw new InvalidOperationException("Power setting unreadable.");

                uint previous = before.u32CurrentValue;
                if (previous == value)
                    throw new InvalidOperationException("This setting is already at that value.");

                var write = new NVDRS_SETTING
                {
                    version = version,
                    settingId = PowerModeId,
                    settingType = 0,        // NVDRS_DWORD_TYPE
                    settingLocation = 0,    // NVDRS_CURRENT_PROFILE_LOCATION
                    settingName = new ushort[2048],
                    predefinedRest = new byte[4096],
                    currentRest = new byte[4096],
                    u32CurrentValue = value
                };

                int rc = set(session, profile, ref write);
                if (rc != 0) throw new InvalidOperationException($"The driver refused the write (code {rc}).");

                rc = save(session);
                if (rc != 0) throw new InvalidOperationException($"The driver refused to save (code {rc}).");

                return new OpRecord
                {
                    Kind = "nvidia-setting",
                    TweakId = "nvidia-alimentation",
                    Description = "Gestion de l'alimentation NVIDIA",
                    ValueName = PowerModeId.ToString(),
                    PreviousValue = previous.ToString(),
                    NewValue = value.ToString(),
                    Existed = true
                };
            }
            finally { destroy?.Invoke(session); }
        }
        finally { Fn<D_Unload>(0xD22BDD7E)?.Invoke(); }
    }

    /// <summary>Remet la gestion de l'alimentation telle qu'elle était.</summary>
    public static void Revert(OpRecord rec)
    {
        if (rec.Kind != "nvidia-setting") return;
        if (!uint.TryParse(rec.PreviousValue, out var previous)) return;

        try { SetPowerMode(previous); }
        catch (InvalidOperationException) { /* déjà à cette valeur : rien à faire */ }
    }

    /// <summary>Réglages du profil global du pilote.</summary>
    public static List<NvSetting> ReadSettings()
    {
        var list = new List<NvSetting>();

        var init = Fn<D_Init>(0x0150E828);
        if (init == null || init() != 0) return list;

        try
        {
            var create = Fn<D_DrsCreate>(0x0694D52E);
            var load = Fn<D_DrsLoad>(0x375DBD6B);
            var basep = Fn<D_DrsBase>(0xDA8466A0);
            var get = Fn<D_DrsGetSetting>(0x73BF8338);
            var destroy = Fn<D_DrsDestroy>(0xDAD9CFF8);
            if (create == null || load == null || basep == null || get == null) return list;

            if (create(out IntPtr session) != 0) return list;
            try
            {
                if (load(session) != 0 || basep(session, out IntPtr profile) != 0 || profile == IntPtr.Zero)
                    return list;

                uint version = (uint)(Marshal.SizeOf<NVDRS_SETTING>() | (1 << 16));
                foreach (var (id, read) in Known)
                {
                    var s = new NVDRS_SETTING { version = version };
                    if (get(session, profile, id, ref s) != 0) continue;

                    string name = s.settingName == null ? $"0x{id:X8}"
                        : new string(s.settingName.TakeWhile(c => c != 0).Select(c => (char)c).ToArray());

                    var (reading, optimal, note) = read(s.u32CurrentValue);
                    list.Add(new NvSetting(id, name, s.u32CurrentValue, reading, optimal, note,
                        Writable: id == PowerModeId));
                }
            }
            finally { destroy?.Invoke(session); }
        }
        catch { }
        finally { Fn<D_Unload>(0xD22BDD7E)?.Invoke(); }

        return list;
    }
}

using System.Runtime.InteropServices;

namespace Aeropeek.Core;

/// <summary>
/// Appels Win32. Tout est en lecture seule sauf indication contraire.
/// </summary>
internal static class Native
{
    // ---------- Affichage ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    internal const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    internal const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    internal const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    // ---------- Fenêtre au premier plan ----------

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Nom du processus dont la fenêtre est au premier plan, sans extension.</summary>
    internal static string ForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return "";
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    // ---------- Topologie processeur ----------

    internal enum LOGICAL_PROCESSOR_RELATIONSHIP : uint
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationAll = 0xffff
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP relationship,
        IntPtr buffer,
        ref uint returnedLength);

    /// <summary>
    /// Renvoie, pour chaque cœur physique, sa classe d'efficacité et son masque d'affinité.
    /// EfficiencyClass 0 = cœur le moins performant (E-core chez Intel hybride).
    /// Sur un processeur non hybride, toutes les classes valent 0.
    /// </summary>
    internal static List<(byte EfficiencyClass, ulong AffinityMask, bool Smt)> EnumProcessorCores()
    {
        var result = new List<(byte, ulong, bool)>();
        uint len = 0;
        GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref len);
        if (len == 0) return result;

        IntPtr buffer = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, buffer, ref len))
                return result;

            IntPtr p = buffer;
            IntPtr end = buffer + (int)len;
            while (p.ToInt64() < end.ToInt64())
            {
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX :
                //   0  DWORD Relationship
                //   4  DWORD Size
                //   8  PROCESSOR_RELATIONSHIP { BYTE Flags; BYTE EfficiencyClass; BYTE[20] Reserved;
                //                               WORD GroupCount; GROUP_AFFINITY[] GroupMask }
                //      GROUP_AFFINITY { KAFFINITY Mask (8 o. en x64); WORD Group; WORD[3] Reserved }
                uint relationship = (uint)Marshal.ReadInt32(p, 0);
                int size = Marshal.ReadInt32(p, 4);
                if (size <= 0) break;

                if (relationship == (uint)LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                {
                    byte flags = Marshal.ReadByte(p, 8);
                    byte efficiencyClass = Marshal.ReadByte(p, 9);
                    ushort groupCount = (ushort)Marshal.ReadInt16(p, 30);
                    ulong mask = 0;
                    if (groupCount > 0)
                        mask = (ulong)Marshal.ReadInt64(p, 32); // premier groupe uniquement
                    result.Add((efficiencyClass, mask, flags == 1));
                }

                p += size;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }
}

using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace Aeropeek.Core;

public sealed record DisplayInfo(
    string DeviceName,
    string MonitorName,
    string AdapterName,
    int Width,
    int Height,
    int RefreshHz,
    bool IsPrimary);

public sealed record MemoryModule(string Manufacturer, string PartNumber, ulong CapacityBytes,
                                  uint ConfiguredMhz, uint RatedMhz, string Kind);

public sealed record CpuTopology(int PhysicalCores, int LogicalCores, int PerformanceCores, int EfficiencyCores, ulong PerformanceMask)
{
    public bool IsHybrid => EfficiencyCores > 0 && PerformanceCores > 0;
}

/// <summary>
/// Instantané de la machine. Construit une fois au démarrage, consulté par les
/// diagnostics et par le contrôle d'applicabilité de chaque réglage.
/// </summary>
public sealed class SystemProfile
{
    public string OsName { get; private init; } = "Windows";
    public string OsEdition { get; private init; } = "";
    public string OsDisplayVersion { get; private init; } = "";
    public int OsBuild { get; private init; }
    public bool IsLaptop { get; private init; }

    public string CpuName { get; private init; } = "";
    public string CpuVendor { get; private init; } = "";
    public CpuTopology Cpu { get; private init; } = new(0, 0, 0, 0, 0);

    public IReadOnlyList<string> GpuNames { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<DisplayInfo> Displays { get; private init; } = Array.Empty<DisplayInfo>();
    public IReadOnlyList<MemoryModule> Memory { get; private init; } = Array.Empty<MemoryModule>();
    public ulong TotalMemoryBytes { get; private init; }

    public IReadOnlyList<string> AntiCheats { get; private init; } = Array.Empty<string>();

    public static SystemProfile Capture()
    {
        // ProductName annonce encore « Windows 10 » sous Windows 11 : on corrige par la build.
        int build = ParseInt(ReadString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber"));
        string product = ReadString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows";
        if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");

        return new SystemProfile
        {
            OsName = product,
            OsEdition = ReadString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID") ?? "",
            OsDisplayVersion = ReadString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") ?? "",
            OsBuild = build,
            IsLaptop = DetectLaptop(),
            CpuName = (ReadString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "").Trim(),
            CpuVendor = ReadString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier") ?? "",
            Cpu = ReadTopology(),
            GpuNames = ReadGpus(),
            Displays = ReadDisplays(),
            Memory = ReadMemory(),
            TotalMemoryBytes = ReadTotalMemory(),
            AntiCheats = DetectAntiCheats()
        };
    }

    // ---------- lecture ----------

    static string? ReadString(string subKey, string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(subKey);
            return k?.GetValue(name) as string;
        }
        catch { return null; }
    }

    static int ParseInt(string? s) => int.TryParse(s, out var v) ? v : 0;

    static bool DetectLaptop()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject mo in s.Get())
            {
                if (mo["ChassisTypes"] is ushort[] types)
                    foreach (var t in types)
                        // 8..14 : portable, laptop, notebook, sub-notebook, docking station
                        // 30..32 : tablette, convertible, détachable
                        if ((t >= 8 && t <= 14) || (t >= 30 && t <= 32)) return true;
            }
        }
        catch { /* pas de WMI : on suppose un poste fixe */ }
        return false;
    }

    static CpuTopology ReadTopology()
    {
        var cores = Native.EnumProcessorCores();
        if (cores.Count == 0)
            return new CpuTopology(Environment.ProcessorCount, Environment.ProcessorCount, 0, 0, 0);

        byte maxClass = cores.Max(c => c.EfficiencyClass);
        int perf = 0, eff = 0, logical = 0;
        ulong perfMask = 0;

        foreach (var (cls, mask, _) in cores)
        {
            logical += System.Numerics.BitOperations.PopCount(mask);
            if (maxClass > 0 && cls == maxClass) { perf++; perfMask |= mask; }
            else if (maxClass > 0) eff++;
        }

        if (maxClass == 0) { perf = cores.Count; eff = 0; perfMask = 0; }
        return new CpuTopology(cores.Count, logical, perf, eff, perfMask);
    }

    static List<string> ReadGpus()
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dev = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
        for (uint i = 0; Native.EnumDisplayDevices(null, i, ref dev, 0); i++)
        {
            if (!string.IsNullOrWhiteSpace(dev.DeviceString) && seen.Add(dev.DeviceString))
                list.Add(dev.DeviceString);
            dev = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
        }
        return list;
    }

    static List<DisplayInfo> ReadDisplays()
    {
        var list = new List<DisplayInfo>();
        int structSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>();

        for (uint i = 0; ; i++)
        {
            var adapter = new Native.DISPLAY_DEVICE { cb = structSize };
            if (!Native.EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var mode = new Native.DEVMODE { dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
            if (!Native.EnumDisplaySettings(adapter.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref mode)) continue;

            string monitorName = adapter.DeviceString;
            var monitor = new Native.DISPLAY_DEVICE { cb = structSize };
            if (Native.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0)
                && !string.IsNullOrWhiteSpace(monitor.DeviceString))
                monitorName = monitor.DeviceString;

            list.Add(new DisplayInfo(
                adapter.DeviceName,
                monitorName,
                adapter.DeviceString,
                (int)mode.dmPelsWidth,
                (int)mode.dmPelsHeight,
                (int)mode.dmDisplayFrequency,
                (adapter.StateFlags & Native.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0));
        }
        return list;
    }

    static List<MemoryModule> ReadMemory()
    {
        var list = new List<MemoryModule>();
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            foreach (ManagementObject mo in s.Get())
            {
                ulong cap = mo["Capacity"] is ulong c ? c : 0;
                uint rated = mo["Speed"] is uint r ? r : 0;
                uint conf = mo["ConfiguredClockSpeed"] is uint cc ? cc : 0;
                int smbios = 0;
                try { if (mo["SMBIOSMemoryType"] is { } raw) smbios = Convert.ToInt32(raw); } catch { }
                string kind = smbios switch { 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => "" };
                list.Add(new MemoryModule((mo["Manufacturer"] as string ?? "").Trim(),
                                          (mo["PartNumber"] as string ?? "").Trim(),
                                          cap, conf, rated, kind));
            }
        }
        catch { /* WMI indisponible */ }
        return list;
    }

    static ulong ReadTotalMemory()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in s.Get())
                if (mo["TotalPhysicalMemory"] is ulong t) return t;
        }
        catch { }
        return 0;
    }

    static List<string> DetectAntiCheats()
    {
        var found = new List<string>();
        var known = new (string Service, string Label)[]
        {
            ("FACEIT",       "FACEIT Anti-Cheat"),
            ("vgk",          "Riot Vanguard"),
            ("EasyAntiCheat","Easy Anti-Cheat"),
            ("BEDaisy",      "BattlEye"),
            ("ESEADriver2",  "ESEA")
        };
        foreach (var (svc, label) in known)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svc}");
                if (k != null) found.Add(label);
            }
            catch { }
        }
        return found;
    }

    public enum ModuleAccess
    {
        /// <summary>Le processus n'est pas lancé.</summary>
        NotRunning,
        /// <summary>Le processus tourne mais refuse la lecture de ses modules.</summary>
        Denied,
        /// <summary>Liste obtenue.</summary>
        Ok
    }

    /// <summary>
    /// Modules chargés dans un processus donné. Lecture seule, aucune injection.
    /// Un anticheat noyau (FACEIT, Vanguard, EAC) retire les droits de lecture sur
    /// le processus protégé : il faut distinguer ce refus d'un jeu non lancé.
    /// </summary>
    public static (ModuleAccess Access, List<string> Modules) LoadedModules(string processName)
    {
        var modules = new List<string>();
        var procs = Process.GetProcessesByName(processName);
        if (procs.Length == 0) return (ModuleAccess.NotRunning, modules);

        bool anyRead = false;
        foreach (var p in procs)
        {
            try
            {
                foreach (ProcessModule m in p.Modules)
                    if (m.FileName is { } f) { modules.Add(f); anyRead = true; }
            }
            catch { /* accès refusé sur ce processus */ }
            finally { p.Dispose(); }
        }

        return (anyRead ? ModuleAccess.Ok : ModuleAccess.Denied, modules);
    }
}

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Nexo.Core
{
    public sealed class GpuInfo
    {
        public string Name { get; }
        public long VramBytes { get; }
        public string Tier { get; }
        public string Vendor { get; }
        public bool IsIntegrated { get; }
        public int MonitorWidth { get; }
        public int MonitorHeight { get; }
        public int MonitorRefreshRate { get; }
        public string MonitorDeviceName { get; }
        /// <summary>Vulkan 1.3 or newer reported by this GPU's installed driver; not a per-emulator feature/benchmark guarantee.</summary>
        public bool SupportsVulkan => VulkanApiVersion >= ((1u << 22) | (3u << 12));
        internal uint VulkanApiVersion { get; }
        internal GpuInfo(string name, long bytes, string vendor, bool integrated, int width, int height,
            uint vulkanApiVersion = 0, int refreshRate = 0, string monitorDeviceName = null)
        {
            Name = name; VramBytes = Math.Max(0, bytes); Vendor = vendor; IsIntegrated = integrated;
            // Decimal GB thresholds avoid mistaking a 6 GB card with reserved driver memory for 4 GB.
            Tier = integrated || vendor == "Desconocido" || bytes < 3000000000L ? "Bajo" : bytes < 6000000000L ? "Medio" : "Alto";
            if (Tier == "Alto" && bytes >= 10000000000L && UltraClass(name)) Tier = "Ultra";
            MonitorWidth = width; MonitorHeight = height;
            VulkanApiVersion = vulkanApiVersion; MonitorRefreshRate = refreshRate; MonitorDeviceName = monitorDeviceName;
        }

        private static bool UltraClass(string name)
        {
            // VRAM capacity is not GPU throughput: a GTX 1080 Ti or RTX 3060 12 GB
            // does not qualify solely by memory. Unknown/future families stay Alto.
            var nvidia = Regex.Match(name ?? "", @"\bRTX\s*(\d{4})\b", RegexOptions.IgnoreCase);
            if (nvidia.Success)
            {
                int model = int.Parse(nvidia.Groups[1].Value);
                return model >= 3070 && model <= 3099 || model >= 4070 && model <= 4099 || model >= 5070 && model <= 5099;
            }
            var amd = Regex.Match(name ?? "", @"\bRX\s*(\d{4})\b", RegexOptions.IgnoreCase);
            if (amd.Success)
            {
                int model = int.Parse(amd.Groups[1].Value);
                return model >= 6800 && model <= 6999 || model >= 7700 && model <= 7999 || model >= 9070 && model <= 9099;
            }
            return false;
        }
    }

    internal sealed class GpuAdapter
    {
        internal string Name, HardwareId;
        internal object Memory;
        internal GpuAdapter(string name, object memory, string hardwareId = "") { Name = name; Memory = memory; HardwareId = hardwareId; }
    }

    internal static class GpuDetector
    {
        private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        internal static GpuInfo Detect()
        {
            var adapters = new List<GpuAdapter>();
            try
            {
                using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var display = machine.OpenSubKey(DisplayClass, false))
                {
                    if (display != null)
                        foreach (string name in display.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")).Take(100))
                        {
                            // One protected or broken driver entry must not hide all the other GPUs.
                            try
                            {
                                using (var key = display.OpenSubKey(name, false))
                                    if (key != null && key.GetValue("DriverDesc") is string description)
                                        adapters.Add(new GpuAdapter(description, key.GetValue("HardwareInformation.qwMemorySize"), key.GetValue("MatchingDeviceId") as string ?? ""));
                            }
                            catch (Exception) { }
                        }
                }
            }
            catch (Exception) { }
            var selected = Select(adapters);
            return RefreshDisplay(new GpuInfo(selected.Name, selected.VramBytes, selected.Vendor, selected.IsIntegrated,
                0, 0, VulkanCapabilities.Detect(selected.Name)));
        }

        internal static GpuInfo RefreshDisplay(GpuInfo gpu)
        {
            try
            {
                // Explicit primary desktop device: no WPF/DPI dimensions, virtual desktop,
                // preferred EDID mode or resolution of an arbitrary secondary display.
                for (uint i = 0; i < 32; i++)
                {
                    var device = new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
                    if (!EnumDisplayDevices(null, i, ref device, 0)) break;
                    if ((device.Flags & 5) != 5) continue; // attached + primary
                    var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf(typeof(DeviceMode)) };
                    if (EnumDisplaySettings(device.Name, -1, ref mode))
                        return WithDisplay(gpu, device.Name, (int)mode.Width, (int)mode.Height, (int)mode.Frequency);
                }
                var fallback = new DeviceMode { Size = (ushort)Marshal.SizeOf(typeof(DeviceMode)) };
                if (EnumDisplaySettings(null, -1, ref fallback))
                    return WithDisplay(gpu, null, (int)fallback.Width, (int)fallback.Height, (int)fallback.Frequency);
            }
            catch (Exception) { }
            return gpu;
        }

        internal static GpuInfo WithDisplay(GpuInfo gpu, string device, int width, int height, int frequency)
            => width > 0 && height > 0 && width <= 32768 && height <= 32768
                ? new GpuInfo(gpu.Name, gpu.VramBytes, gpu.Vendor, gpu.IsIntegrated, width, height,
                    gpu.VulkanApiVersion, frequency > 1 && frequency <= 2000 ? frequency : 0, device)
                : gpu;

        internal static long ReadMemory(object raw)
        {
            if (raw is long signed) return Math.Max(0, signed);
            if (raw is ulong unsigned) return unsigned <= long.MaxValue ? (long)unsigned : 0;
            if (raw is byte[] data && data.Length == 8)
            {
                ulong value = 0; for (int i = 0; i < 8; i++) value |= (ulong)data[i] << (8 * i);
                return value <= long.MaxValue ? (long)value : 0;
            }
            // A DWORD AdapterRAM/MemorySize is not a reliable fallback for dedicated VRAM.
            return 0;
        }

        internal static GpuInfo Select(IEnumerable<GpuAdapter> adapters, int width = 0, int height = 0)
        {
            return adapters.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name))
                .Select(a => Describe(a, width, height))
                .OrderByDescending(a => a.VramBytes).ThenBy(a => a.IsIntegrated)
                .FirstOrDefault() ?? new GpuInfo("GPU no identificada", 0, "Desconocido", true, width, height);
        }

        private static GpuInfo Describe(GpuAdapter adapter, int width, int height)
        {
            string name = adapter.Name, id = adapter.HardwareId ?? "";
            string text = (name + " " + id).ToUpperInvariant();
            string vendor = text.Contains("NVIDIA") || text.Contains("VEN_10DE") ? "NVIDIA" :
                text.Contains("AMD") || text.Contains("RADEON") || text.Contains("VEN_1002") ? "AMD" :
                text.Contains("INTEL") || text.Contains("VEN_8086") ? "Intel" : "Desconocido";
            // Registry VRAM alone cannot distinguish UMA reservations from a dedicated GPU.
            // Use conservative model families; Intel Arc A/B discrete cards are not Intel iGPUs.
            bool discreteIntel = Regex.IsMatch(name, @"\bArc\b.*\b(?:[AB]\d{3}|Pro\s+[AB]\d{2,3})\b", RegexOptions.IgnoreCase);
            bool discreteAmd = Regex.IsMatch(name, @"\b(?:RX\s*\d|R[579]\s+(?:M\d|\d)|HD\s*\d|FirePro|Radeon\s+Pro\s+[VW]\d)", RegexOptions.IgnoreCase);
            bool integrated = vendor == "Intel" ? !discreteIntel : vendor == "AMD" ? !discreteAmd : vendor != "NVIDIA";
            return new GpuInfo(name, ReadMemory(adapter.Memory), vendor, integrated, width, height);
        }

        // EnumDisplaySettings returns physical pixels independently of WPF's DPI scaling.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DeviceMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public ushort SpecVersion, DriverVersion, Size, DriverExtra;
            public uint Fields;
            public int PositionX, PositionY;
            public uint Orientation, FixedOutput;
            public short Color, Duplex, YResolution, TTOption, Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
            public ushort LogPixels;
            public uint BitsPerPel, Width, Height, DisplayFlags, Frequency, ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(string deviceName, int mode, ref DeviceMode settings);
    }
}

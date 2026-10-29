using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Nexo.Core
{
    // Query the installed driver, not a VRAM/model guess. No window, surface or GPU device
    // is created. A missing loader, unmatched adapter or error leaves backend choice to
    // the emulator. 1.3 is our common conservative floor, not a complete feature test.
    internal static class VulkanCapabilities
    {
        internal static uint Match(string selected, IEnumerable<Tuple<string, uint>> devices)
            => devices.Where(d => string.Equals(d.Item1.Trim(), selected?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Item2).DefaultIfEmpty(0u).Max();

        internal static uint Detect(string selected)
        {
            IntPtr library = IntPtr.Zero, instance = IntPtr.Zero, buffer = IntPtr.Zero;
            DestroyInstance destroy = null;
            try
            {
                library = LoadLibraryEx(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll"), IntPtr.Zero, 0x800);
                if (library == IntPtr.Zero) return 0;
                T Function<T>(string name) where T : class
                {
                    var address = GetProcAddress(library, name);
                    if (address == IntPtr.Zero) throw new EntryPointNotFoundException(name);
                    return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
                }
                var create = Function<CreateInstance>("vkCreateInstance");
                destroy = Function<DestroyInstance>("vkDestroyInstance");
                var enumerate = Function<EnumerateDevices>("vkEnumeratePhysicalDevices");
                var properties = Function<GetProperties>("vkGetPhysicalDeviceProperties");
                var info = new InstanceInfo { Type = 1 }; // VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO
                if (create(ref info, IntPtr.Zero, out instance) != 0 || instance == IntPtr.Zero) return 0;
                uint count = 0;
                if (enumerate(instance, ref count, null) != 0 || count == 0 || count > 32) return 0;
                var devices = new IntPtr[count];
                if (enumerate(instance, ref count, devices) != 0 || count > devices.Length) return 0;
                // VkPhysicalDeviceProperties has fixed size < 1 KiB; its initial header
                // is five uint32 fields followed by deviceName[256] on both x86 and x64.
                buffer = Marshal.AllocHGlobal(4096);
                var found = new List<Tuple<string, uint>>();
                for (int i = 0; i < count; i++)
                {
                    properties(devices[i], buffer);
                    uint type = unchecked((uint)Marshal.ReadInt32(buffer, 16));
                    if (type != 1 && type != 2) continue; // integrated/discrete, never software
                    string name = Marshal.PtrToStringAnsi(IntPtr.Add(buffer, 20), 256).Split('\0')[0];
                    found.Add(Tuple.Create(name, unchecked((uint)Marshal.ReadInt32(buffer))));
                }
                return Match(selected, found);
            }
            catch (Exception) { return 0; }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (instance != IntPtr.Zero) destroy?.Invoke(instance, IntPtr.Zero);
                if (library != IntPtr.Zero) FreeLibrary(library);
            }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceInfo
        {
            internal uint Type; internal IntPtr Next; internal uint Flags; internal IntPtr Application;
            internal uint LayerCount; internal IntPtr Layers; internal uint ExtensionCount; internal IntPtr Extensions;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateInstance(ref InstanceInfo info, IntPtr allocator, out IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DestroyInstance(IntPtr instance, IntPtr allocator);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumerateDevices(IntPtr instance, ref uint count, [Out] IntPtr[] devices);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetProperties(IntPtr device, IntPtr properties);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")] private static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr library, string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr library);
    }
}

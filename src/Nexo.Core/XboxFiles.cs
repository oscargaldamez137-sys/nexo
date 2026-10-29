using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexo.Core
{
    internal static class XboxFiles
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
            ["bootrom_path"] = "mcpx_1.0.bin", ["flashrom_path"] = "flash.bin", ["hdd_path"] = "xbox_hdd.qcow2" };
        internal static bool Configure(string folder)
        {
            var file = new ControllerConfig(Path.Combine(folder, "xemu.toml"), toml: true);
            var fields = file.ReadSection("sys.files");
            var patch = new Dictionary<string, string>();
            foreach (var pair in Names)
                if ((fields == null || !fields.TryGetValue(pair.Key, out var current) || GraphicsSetup.Equivalent(current, "")) &&
                    SafePaths.IsRegularFile(Path.Combine(folder, "system", pair.Value)))
                    patch[pair.Key] = "\"system/" + pair.Value + "\"";
            if (patch.Count == 0) return false;
            file.SetSection("sys.files", patch);
            return file.Save();
        }
        internal static IReadOnlyList<string> Missing(string folder)
        {
            var fields = new ControllerConfig(Path.Combine(folder, "xemu.toml"), toml: true).ReadSection("sys.files");
            var missing = new List<string>();
            foreach (var pair in Names)
            {
                string path = Path.Combine(folder, "system", pair.Value);
                if (fields != null && fields.TryGetValue(pair.Key, out var value) && !GraphicsSetup.Equivalent(value, ""))
                {
                    value = value.Trim().Trim('"', '\'').Replace("\\\\", "\\");
                    path = Path.IsPathRooted(value) ? value : Path.Combine(folder, value);
                }
                bool valid = SafePaths.IsRegularFile(path) && (pair.Key == "hdd_path" ? IsQcow(path) :
                    pair.Key == "bootrom_path" ? new FileInfo(path).Length == 512 : IsFlash(path));
                if (!valid) missing.Add(pair.Key == "bootrom_path" ? "Falta la MCPX boot ROM de Xbox (512 bytes)." :
                    pair.Key == "flashrom_path" ? "Falta la BIOS flash de Xbox compatible con xemu (256 KiB, 512 KiB o 1 MiB)." : "Falta un disco duro Xbox .qcow2 válido.");
            }
            return missing.AsReadOnly();
        }
        internal static string ImportName(string file)
        {
            string ext = Path.GetExtension(file);
            if (ext.Equals(".qcow2", StringComparison.OrdinalIgnoreCase) && IsQcow(file)) return "xbox_hdd.qcow2";
            if (ext.Equals(".bin", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(file).Length == 512) return "mcpx_1.0.bin";
                if (IsFlash(file)) return "flash.bin";
            }
            throw new NexoException("Selecciona MCPX .bin de 512 bytes, BIOS .bin de 256/512 KiB o 1 MiB, o disco .qcow2 autónomo. El tamaño no garantiza compatibilidad con xemu.");
        }
        private static bool IsFlash(string file) => new[] { 262144L, 524288L, 1048576L }.Contains(new FileInfo(file).Length);
        internal static bool IsQcow(string file)
        {
            if (!SafePaths.IsRegularFile(file)) return false;
            // xemu keeps the disk open while running: read the header without demanding exclusivity.
            using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var h = new byte[104];
                if (f.Length < h.Length || f.Read(h, 0, h.Length) != h.Length || h[0] != 0x51 || h[1] != 0x46 || h[2] != 0x49 || h[3] != 0xfb) return false;
                uint version = XboxMetadata.Be32(h, 4), cluster = XboxMetadata.Be32(h, 20);
                // Reject external backing files and encrypted images.
                return (version == 2 || version == 3) && h.Skip(8).Take(12).All(b => b == 0) &&
                    cluster >= 9 && cluster <= 21 && h.Skip(24).Take(8).Any(b => b != 0) && XboxMetadata.Be32(h, 32) == 0 &&
                    (version != 3 || (XboxMetadata.Be32(h, 76) & 4) == 0);
            }
        }
    }
}

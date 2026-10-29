using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexo.Core
{
    // Only retire the exact two-setting stub when BOTH values have matching receipts.
    // A full legacy configuration, comments, unknown keys or edited values are user data.
    internal sealed class RpcGraphicsMigration
    {
        private readonly string oldPath, receiptPath;
        private byte[] oldBytes, receiptBytes;
        internal string ConfigPath { get; }
        internal RpcGraphicsMigration(string folder)
        {
            oldPath = Path.Combine(folder, "config.yml");
            receiptPath = oldPath + ".nexo-graphics.json";
            string modern = Path.Combine(folder, "config", "config.yml");
            bool stub = IsOwnedStub();
            ConfigPath = File.Exists(modern) || !File.Exists(oldPath) || stub ? modern : oldPath;
        }
        private bool IsOwnedStub()
        {
            try
            {
                if (!SafePaths.IsRegularFile(oldPath) || !SafePaths.IsRegularFile(receiptPath) ||
                    new FileInfo(oldPath).Length > 4096 || new FileInfo(receiptPath).Length > 65536) return false;
                byte[] config = File.ReadAllBytes(oldPath), receipt = File.ReadAllBytes(receiptPath);
                string text = new UTF8Encoding(false, true).GetString(config).TrimStart('\uFEFF').Replace("\r\n", "\n");
                var lines = text.Split('\n').Where(l => l.Length != 0).ToArray();
                if (lines.Length != 3 || lines[0] != "Video:" ||
                    !lines.Skip(1).All(l => Regex.IsMatch(l, @"^  (Resolution Scale|Anisotropic Filter Override): [0-9]+$"))) return false;
                var values = new ControllerConfig(oldPath).ReadYamlScalars("Video");
                var history = new GraphicsHistory(oldPath, true);
                if (history.Count != 2 || values == null || values.Count != 2 ||
                    !values.All(p => history.Owns("Video", p.Key, p.Value))) return false;
                // Readers above must have observed the same snapshot.
                if (!File.ReadAllBytes(oldPath).SequenceEqual(config) || !File.ReadAllBytes(receiptPath).SequenceEqual(receipt)) return false;
                oldBytes = config; receiptBytes = receipt;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
            { return false; }
        }
        internal bool Retire()
        {
            if (oldBytes == null || !File.Exists(ConfigPath) || ConfigPath == oldPath) return false;
            if (!SafePaths.IsRegularFile(oldPath) || !SafePaths.IsRegularFile(receiptPath) ||
                !File.ReadAllBytes(oldPath).SequenceEqual(oldBytes) || !File.ReadAllBytes(receiptPath).SequenceEqual(receiptBytes))
                throw new IOException("El archivo antiguo de RPCS3 cambió; se conserva.");
            File.Delete(oldPath);
            File.Delete(receiptPath);
            return true;
        }
    }
}

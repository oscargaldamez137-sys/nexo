using System;
using System.IO;

namespace Nexo.Core
{
    // Header-only, bounded I/O: library scans must not decompress multi-megapixel images.
    internal static class ArtworkHeader
    {
        internal static bool IsHd(string path)
        {
            try
            {
                if (!SafePaths.IsRegularFile(path)) return false;
                using (var stream = File.OpenRead(path))
                    return stream.Length <= 32 * 1024 * 1024 && IsHd(stream);
            }
            catch (Exception ex) when (ArtworkMetadata.OptionalFailure(ex)) { return false; }
        }
        internal static bool IsHd(byte[] bytes)
        { if (bytes == null) return false; using (var stream = new MemoryStream(bytes, false)) return IsHd(stream); }
        private static bool IsHd(Stream stream) => Dimensions(stream, out int w, out int h) && w >= 1280 && h >= 720;
        internal static bool Dimensions(Stream stream, out int width, out int height)
        {
            width = height = 0;
            if (!stream.CanSeek || stream.Length < 24) return false;
            stream.Position = 0;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                byte[] head = reader.ReadBytes(24);
                if (head.Length != 24) return false;
                if (head[0] == 137 && head[1] == 80 && head[2] == 78 && head[3] == 71 && head[4] == 13 && head[5] == 10 && head[6] == 26 && head[7] == 10)
                {
                    if (stream.Length < 33 || Big(head, 8) != 13 || head[12] != 73 || head[13] != 72 || head[14] != 68 || head[15] != 82) return false;
                    width = Big(head, 16); height = Big(head, 20); return Sane(width, height);
                }
                if (head[0] != 255 || head[1] != 216) return false;
                stream.Position = 2;
                // Skip APP/EXIF segments by seeking; bounded marker count and header offset.
                for (int budget = 2048; budget > 0 && stream.Position < Math.Min(stream.Length - 1, 1024 * 1024); budget--)
                {
                    if (reader.ReadByte() != 255) return false;
                    int marker;
                    do { if (--budget <= 0 || stream.Position >= stream.Length) return false; marker = reader.ReadByte(); } while (marker == 255);
                    if (marker == 218 || marker == 217 || marker == 0) return false;
                    if (marker == 1 || marker >= 208 && marker <= 215) continue;
                    if (stream.Position + 2 > stream.Length) return false;
                    int length = reader.ReadByte() * 256 + reader.ReadByte();
                    if (length < 2 || stream.Position + length - 2 > stream.Length) return false;
                    if (marker == 192 || marker == 193 || marker == 194)
                    {
                        if (length < 8) return false;
                        reader.ReadByte(); height = reader.ReadByte() * 256 + reader.ReadByte(); width = reader.ReadByte() * 256 + reader.ReadByte();
                        return Sane(width, height);
                    }
                    stream.Position += length - 2;
                }
            }
            return false;
        }
        private static bool Sane(int w, int h) => w > 0 && h > 0 && w <= 8192 && h <= 8192 && (long)w * h <= 32 * 1024 * 1024;
        private static int Big(byte[] b, int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
    }
}

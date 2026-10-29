using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Nexo.Core
{
    internal static class SafePaths
    {
        internal static string Full(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0 || path.StartsWith(@"\\", StringComparison.Ordinal)
                || path.StartsWith("//", StringComparison.Ordinal) || path.Length < 3 || !char.IsLetter(path[0])
                || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
                throw new NexoException("Elige una ruta absoluta en una unidad local.");
            try
            {
                var full = Path.GetFullPath(path);
                return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { throw new NexoException("La ruta no es válida.", ex); }
        }

        internal static bool Within(string path, string root) =>
            string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        internal static bool HasReparsePoint(string path)
        {
            try
            {
                for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                    if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { return true; }
        }

        internal static bool IsRegularFile(string path) => File.Exists(path) && !HasReparsePoint(path);

        internal static string Executable(string path)
        {
            var full = Full(path);
            if (!string.Equals(Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase) || !IsRegularFile(full))
                throw new NexoException("Selecciona un archivo .exe existente; no se admiten enlaces ni junctions.");
            return full;
        }
    }

    internal static class FileTree
    {
        internal static IEnumerable<string> Files(string root, int maxDepth, int maxEntries, TimeSpan budget, CancellationToken ct)
        {
            var watch = Stopwatch.StartNew();
            var pending = new Queue<Tuple<string, int>>();
            if (!Directory.Exists(root) || SafePaths.HasReparsePoint(root)) yield break;
            pending.Enqueue(Tuple.Create(root, 0));
            int count = 0;
            while (pending.Count != 0)
            {
                ct.ThrowIfCancellationRequested();
                if (count >= maxEntries || watch.Elapsed >= budget) yield break;
                var folder = pending.Dequeue();
                IEnumerator<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(folder.Item1).GetEnumerator(); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }
                using (entries)
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (count >= maxEntries || watch.Elapsed >= budget) yield break;
                        string entry;
                        try { if (!entries.MoveNext()) break; entry = entries.Current; }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { break; }
                        count++;
                        FileAttributes attributes;
                        try { attributes = File.GetAttributes(entry); }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            // Extraction staging is never part of the public library or executable search.
                            if (Path.GetFileName(entry).StartsWith(".nexo-extract-", StringComparison.OrdinalIgnoreCase)) continue;
                            if (Path.GetFileName(entry).Equals("covers", StringComparison.OrdinalIgnoreCase) ||
                                Path.GetFileName(entry).Equals(".nexo-art", StringComparison.OrdinalIgnoreCase)) continue;
                            if (folder.Item2 < maxDepth) pending.Enqueue(Tuple.Create(entry, folder.Item2 + 1));
                        }
                        else yield return entry;
                    }
                }
            }
        }
    }
}

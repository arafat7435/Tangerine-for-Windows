using System.IO.Compression;
using System.Formats.Tar;
using SharpCompress.Archives;
namespace Tangerine;
public sealed partial class Engine
{
    static List<string> ArchiveConvert(string input, string target, string format, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetDirectoryName(target)!, "unpacked"); ExtractArchive(input, temp, ct); ct.ThrowIfCancellationRequested();
        if (format == "zip")
        {
            using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
            foreach (var dir in Directory.EnumerateDirectories(temp, "*", SearchOption.AllDirectories)) zip.CreateEntry(Path.GetRelativePath(temp, dir).Replace('\\', '/') + "/");
            foreach (var path in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories)) { ct.ThrowIfCancellationRequested(); using var source = File.OpenRead(path); using var destination = zip.CreateEntry(Path.GetRelativePath(temp, path).Replace('\\', '/'), CompressionLevel.Optimal).Open(); CopyArchiveData(source, destination, ct); }
        }
        else if (format == "tar") TarFile.CreateFromDirectory(temp, target, false);
        else { using var file = File.Create(target); using var gzip = new GZipStream(file, CompressionLevel.Optimal); TarFile.CreateFromDirectory(temp, gzip, false); }
        ct.ThrowIfCancellationRequested(); return new() { target };
    }
    public static string SafeArchivePath(string root, string entry)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(prefix, entry.Replace('/', Path.DirectorySeparatorChar)));
        if ((!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !full.Equals(prefix.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) || entry.Contains(':')) throw new InvalidDataException("The archive contains an unsafe path."); return full;
    }
    static long CopyArchiveData(Stream source, Stream destination, CancellationToken ct, long limit = 20L * 1024 * 1024 * 1024)
    {
        var buffer = new byte[65536]; long total = 0; int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) { ct.ThrowIfCancellationRequested(); total += read; if (total > limit) throw new InvalidDataException("The archive exceeds the 20 GB extraction limit."); destination.Write(buffer, 0, read); }
        return total;
    }
    static bool IsTarHeader(Stream stream)
    {
        var block = new byte[512]; int read = stream.ReadAtLeast(block, 512, false); stream.Position = 0; if (read < 512) return false;
        if (block.All(x => x == 0)) return true;
        var value = Encoding.ASCII.GetString(block, 148, 8).Trim('\0', ' '); try { var checksum = Convert.ToInt64(value, 8); long actual = 0; for (int i = 0; i < 512; i++) actual += i >= 148 && i < 156 ? 32 : block[i]; return checksum == actual; } catch (FormatException) { return false; }
    }
    public static void ExtractArchive(string input, string root, CancellationToken ct)
    {
        Directory.CreateDirectory(root); var ext = Catalog.Extension(input); long total = 0;
        const long maxBytes = 20L * 1024 * 1024 * 1024; const int maxEntries = 100000; int count = 0;
        void Count() { if (++count > maxEntries) throw new InvalidDataException("The archive exceeds 100,000 entries."); ct.ThrowIfCancellationRequested(); }
        void ExtractTar(Stream stream) { using var tar = new TarReader(stream, true); TarEntry? entry; while ((entry = tar.GetNextEntry()) != null) { Count(); var dest = SafeArchivePath(root, entry.Name); if (entry.EntryType == TarEntryType.Directory) Directory.CreateDirectory(dest); else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile) { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); using var output = new FileStream(dest, FileMode.CreateNew); if (entry.DataStream != null) total += CopyArchiveData(entry.DataStream, output, ct, maxBytes - total); } else if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink) throw new InvalidDataException("Archive links are not extracted."); } }
        if (ext == "zip")
        {
            using var zip = ZipFile.OpenRead(input); foreach (var entry in zip.Entries) { Count(); var dest = SafeArchivePath(root, entry.FullName); if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(dest); else { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); using var source = entry.Open(); using var output = new FileStream(dest, FileMode.CreateNew); total += CopyArchiveData(source, output, ct, maxBytes - total); } }
        }
        else if (ext == "tar")
        {
            using var file = File.OpenRead(input); ExtractTar(file);
        }
        else if (ext == "gz")
        {
            var temporary = Path.Combine(root, ".gzip-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var file = File.OpenRead(input)) using (var gzip = new GZipStream(file, CompressionMode.Decompress)) using (var destination = new FileStream(temporary, FileMode.CreateNew)) CopyArchiveData(gzip, destination, ct, maxBytes);
                bool tar; using (var unpacked = File.OpenRead(temporary)) { tar = IsTarHeader(unpacked); if (tar) ExtractTar(unpacked); }
                if (!tar) { var name = Path.GetFileNameWithoutExtension(input); if (string.IsNullOrWhiteSpace(name)) name = "Extracted file"; File.Move(temporary, SafeArchivePath(root, name), false); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        else if (ext == "rar")
        {
            using var archive = ArchiveFactory.Open(input);
            foreach (var entry in archive.Entries) { Count(); if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new InvalidDataException("Archive links are not extracted."); var dest = SafeArchivePath(root, entry.Key ?? throw new InvalidDataException("Archive entry has no name.")); if (entry.IsDirectory) Directory.CreateDirectory(dest); else { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); using var source = entry.OpenEntryStream(); using var output = new FileStream(dest, FileMode.CreateNew); total += CopyArchiveData(source, output, ct, maxBytes - total); } }
        }
        else throw new NotSupportedException("This archive format is not supported.");
    }
}

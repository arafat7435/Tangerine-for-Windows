using System.Formats.Tar;
using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Tangerine;
public sealed partial class FileActionAudit
{
    async Task Files()
    {
        var pdf = Path.Combine(input, "pages.pdf"); using (var doc = new PdfDocument()) { doc.Info.Title = "Private title"; doc.Info.Author = "Private author"; for (int i = 0; i < 3; i++) { var page = doc.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(200 + i * 10); page.Height = PdfSharp.Drawing.XUnit.FromPoint(300); } doc.Save(pdf); }
        Track(pdf);
        foreach (var id in new[] { "compress", "metadata", "organize", "split" }) await Test("PDF tool " + id, async () =>
        {
            var result = await Tool(pdf, id, new() { Pages = "3,1,3", Rotation = 90 });
            if (id == "split") { var files = Directory.GetFiles(result, "*.pdf"); Check(files.Length == 3, "Split dropped repeated pages"); foreach (var file in files) { using var part = PdfReader.Open(file, PdfDocumentOpenMode.Import); Check(part.PageCount == 1, "Split includes extra pages"); } return; }
            using var doc = PdfReader.Open(result, PdfDocumentOpenMode.Import); Check(doc.PageCount == 3, "Page count changed");
            if (id == "metadata") Check(doc.Info.Title != "Private title" && doc.Info.Author != "Private author", "Private PDF metadata remains");
            if (id == "organize") Check(doc.Pages[0].Width.Point == 220 && doc.Pages[1].Width.Point == 200 && doc.Pages[2].Width.Point == 220 && doc.Pages.Cast<PdfPage>().All(p => p.Rotate == 90), "Reorder, duplicates or rotation failed");
        });
        await Test("PDF merge preserves source order", async () => { using var doc = PdfReader.Open((await Tool(new[] { pdf, pdf }, "merge"))[0], PdfDocumentOpenMode.Import); Check(doc.PageCount == 6 && doc.Pages[3].Width.Point == 200, "Merge lost page order"); });
        foreach (var pages in new[] { "0", "4", "1,", "-1", "1-4", "abc" }) await Test("Reject invalid PDF page selection " + pages, () => Reject(async () => { await Tool(pdf, "organize", new() { Pages = pages }); }));
        await Test("Descending PDF range and negative rotation", async () => { using var doc = PdfReader.Open(await Tool(pdf, "organize", new() { Pages = "3-1", Rotation = -90 }), PdfDocumentOpenMode.Import); Check(doc.Pages[0].Width.Point == 220 && doc.Pages[2].Width.Point == 200 && doc.Pages[0].Rotate == 270, "Descending range or rotation incorrect"); });
        var payload = Path.Combine(input, "archive-payload"); Directory.CreateDirectory(Path.Combine(payload, "nested")); Directory.CreateDirectory(Path.Combine(payload, "empty")); File.WriteAllText(Path.Combine(payload, "nested", "hello.txt"), "archive fixture");
        foreach (var ext in new[] { "zip", "tar", "tar.gz", "tgz", "gz" }) await Test("Archive extract " + ext, async () =>
        {
            var file = Path.Combine(input, "archive." + ext);
            if (ext == "zip") ZipFile.CreateFromDirectory(payload, file);
            else if (ext == "tar") TarFile.CreateFromDirectory(payload, file, false);
            else { using var output = File.Create(file); using var gzip = new GZipStream(output, CompressionLevel.Optimal); if (ext == "gz") gzip.Write(Encoding.UTF8.GetBytes("single gzip file")); else TarFile.CreateFromDirectory(payload, gzip, false); }
            Track(file); var result = await Tool(file, "extract");
            if (ext == "gz") Check(File.ReadAllText(Path.Combine(result, "archive")) == "single gzip file", "Plain gzip content changed");
            else Check(File.ReadAllText(Path.Combine(result, "nested", "hello.txt")) == "archive fixture" && Directory.Exists(Path.Combine(result, "empty")), "Archive lost content or empty directory");
        });
        var rar = Environment.GetEnvironmentVariable("TANGERINE_RAR_FIXTURE"); if (!string.IsNullOrEmpty(rar)) await Test("RAR extraction", async () => { var file = Path.Combine(input, "fixture.rar"); File.Copy(rar, file); Track(file); var result = await Tool(file, "extract"); Check(Directory.GetFiles(result, "*", SearchOption.AllDirectories).Length > 0, "RAR empty"); });
        foreach (var entry in new[] { "../escape.txt", "nested/../../escape.txt", "C:/escape.txt", "file.txt:stream" }) await Test("Archive rejects unsafe entry " + entry, async () =>
        {
            var file = Path.Combine(input, Guid.NewGuid() + ".zip"); using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("unsafe fixture"); }
            Track(file); var before = Directory.GetFileSystemEntries(output).Length; await Reject(async () => { await Tool(file, "extract"); }); Check(Directory.GetFileSystemEntries(output).Length == before, "Partial extraction leaked");
        });
        await Test("TAR symbolic links are rejected", async () => { var file = Path.Combine(input, "links.tar"); using (var stream = File.Create(file)) using (var tar = new TarWriter(stream)) { tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" }); } Track(file); await Reject(async () => { await Tool(file, "extract"); }); });
        var move = Path.Combine(directory, "move-tests"); Directory.CreateDirectory(move);
        string Fixture(string path) { var file = Path.Combine(move, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "move fixture"); return file; }
        var destination = Path.Combine(move, "destination"); Directory.CreateDirectory(destination);
        string OtherVolumeDirectory() { var root = Environment.GetEnvironmentVariable("TANGERINE_CROSS_VOLUME_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"); Check(!string.Equals(Path.GetPathRoot(Path.GetFullPath(root)), Path.GetPathRoot(move), StringComparison.OrdinalIgnoreCase), "Set TANGERINE_CROSS_VOLUME_ROOT to a writable folder on another drive."); var path = Path.Combine(root, "Tangerine-move-audit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        await Test("Folder with trailing slash moves inside chosen destination", () => { var child = Fixture("trailing/nested/child.txt"); var folder = Path.Combine(move, "trailing"); var moved = FileActions.MoveFiles(new[] { folder + Path.DirectorySeparatorChar }, destination); Check(moved.Single() == Path.Combine(destination, "trailing") && File.Exists(Path.Combine(destination, "trailing", "nested", "child.txt")), "Folder moved outside requested destination"); return Task.CompletedTask; });
        await Test("Move into current folder is a no-op", () => { var file = Fixture("same-parent.txt"); Check(FileActions.MoveFiles(new[] { file }, move).Single() == file && File.Exists(file), "Same-folder move renamed source"); return Task.CompletedTask; });
        await Test("Move rollback restores earlier files when later file is locked", async () => { var first = Fixture("rollback/first.txt"); var locked = Fixture("rollback/locked.txt"); using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None); await Reject(() => { FileActions.MoveFiles(new[] { first, locked }, destination); return Task.CompletedTask; }); Check(File.Exists(first) && !File.Exists(Path.Combine(destination, "first.txt")), "Rollback failed"); });
        await Test("Move treats duplicate slash variants as one folder", () => { var child = Fixture("duplicates/child.txt"); var folder = Path.GetDirectoryName(child)!; var result = FileActions.MoveFiles(new[] { folder, folder + Path.DirectorySeparatorChar }, destination); Check(result.Count == 1 && File.Exists(Path.Combine(result[0], "child.txt")), "Duplicate source was moved twice"); return Task.CompletedTask; });
        await Test("Cross-volume locked folder rolls back children and prior batch entries", async () =>
        {
            var first = Fixture("cross-batch-first.txt"); var child = Fixture("cross-locked/a-first.txt"); var locked = Fixture("cross-locked/z-locked.txt"); var folder = Path.GetDirectoryName(child)!; var other = OtherVolumeDirectory();
            try
            {
                Check(Path.GetPathRoot(other) != Path.GetPathRoot(folder), "Cross-volume test requires another volume");
                using (var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) { await Reject(() => { FileActions.MoveFiles(new[] { first, folder }, other); return Task.CompletedTask; }); Check(File.Exists(child) && File.Exists(first), "Partial move left source files at the destination"); }
                Check(File.ReadAllText(child) == "move fixture" && File.ReadAllText(locked) == "move fixture" && !Directory.EnumerateFileSystemEntries(other).Any(), "Cross-volume rollback lost content or left partial output");
            }
            finally { if (!Directory.EnumerateFileSystemEntries(other).Any()) Directory.Delete(other, false); }
        });
        await Test("Cross-volume folder move retains complete tree", () => { var child = Fixture("cross-volume/nested/leaf.txt"); var folder = Path.Combine(move, "cross-volume"); var other = OtherVolumeDirectory(); try { var result = FileActions.MoveFiles(new[] { folder }, other)[0]; Check(File.ReadAllText(Path.Combine(result, "nested", "leaf.txt")) == "move fixture" && !Directory.Exists(folder), "Cross-volume move lost files"); FileActions.MoveFiles(new[] { result }, move); } finally { Directory.Delete(other, false); } return Task.CompletedTask; });
    }
}

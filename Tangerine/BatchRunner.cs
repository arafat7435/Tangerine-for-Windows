using ImageMagick;
namespace Tangerine;

public sealed class BatchEntry
{
    public string Input { get; }
    public OperationOptions? CustomOptions { get; set; }
    public Job Job { get; }
    public string State { get; internal set; } = "Ready";
    public string Error { get; internal set; } = "";
    public List<string> Outputs { get; internal set; } = new();
    public BatchEntry(string input, WheelItem item) { Input = input; Job = new() { Name = Path.GetFileName(input), Operation = item.Description }; }
}

public sealed class BatchPlan
{
    public List<BatchEntry> Entries { get; } = new();
    public WheelItem Item { get; }
    public WheelMode Mode { get; }
    public OperationOptions? CommonOptions { get; private set; }
    public string? ReferenceFile { get; private set; }
    public bool ScaleSelection { get; set; } = true;
    (int Width, int Height) referenceSize;
    public bool Spatial => Mode == WheelMode.Tools && Item.Id is "crop" or "redact" or "annotate";
    public bool NeedsSettings => Mode == WheelMode.Tools && OptionsWindow.Needed(Item.Id) && Item.Id is not ("compress" or "metadata");
    public bool CanRun => Entries.Count > 0 && (!NeedsSettings || CommonOptions != null || Entries.All(e => e.CustomOptions != null));
    public BatchPlan(string[] inputs, WheelItem item, WheelMode mode)
    {
        if (mode == WheelMode.Tools && Catalog.IsGroup(item.Id)) throw new ArgumentException("Combined outputs use the group editor.");
        Item = item; Mode = mode; foreach (var path in Catalog.Selection(inputs)) Entries.Add(new(path, item));
    }
    public async Task SetCommon(string reference, OperationOptions options, Engine engine, CancellationToken token)
    {
        var size = Spatial ? await Dimensions(reference, engine, token) : (0, 0);
        CommonOptions = options.Copy(); ReferenceFile = reference; referenceSize = size;
        // A new common edit intentionally replaces all previous custom edits.
        foreach (var entry in Entries) entry.CustomOptions = null;
    }
    public async Task<OperationOptions> OptionsFor(BatchEntry entry, Engine engine, int quality, CancellationToken token)
    {
        if (entry.CustomOptions != null) return entry.CustomOptions.Copy();
        var options = CommonOptions?.Copy() ?? new OperationOptions { Quality = quality };
        if (CommonOptions != null && Spatial && ScaleSelection)
        {
            var size = await Dimensions(entry.Input, engine, token);
            if (referenceSize.Width <= 0 || referenceSize.Height <= 0) throw new InvalidDataException("Cannot determine the reference frame size.");
            double sx = (double)size.Width / referenceSize.Width, sy = (double)size.Height / referenceSize.Height;
            options.X = Math.Clamp((int)Math.Round(options.X * sx), 0, size.Width - 1); options.Y = Math.Clamp((int)Math.Round(options.Y * sy), 0, size.Height - 1);
            options.Width = Math.Clamp((int)Math.Round(options.Width * sx), 1, size.Width - options.X); options.Height = Math.Clamp((int)Math.Round(options.Height * sy), 1, size.Height - options.Y);
            if (Item.Id == "annotate") options.FontSize = Math.Clamp((int)Math.Round(options.FontSize * Math.Min(sx, sy)), 1, 1000);
        }
        return options;
    }
    static async Task<(int Width, int Height)> Dimensions(string path, Engine engine, CancellationToken token)
    {
        if (Catalog.Kind(path) == FileKind.Video) { var b = await engine.MediaBounds(path, token); if (b.Width <= 0 || b.Height <= 0) throw new InvalidDataException("Cannot read the video dimensions."); return (b.Width, b.Height); }
        return await Task.Run(() => { token.ThrowIfCancellationRequested(); using var image = new MagickImage(path); image.AutoOrient(); return ((int)image.Width, (int)image.Height); }, token);
    }
}

public static class BatchRunner
{
    // Each source is its own staged transaction. A bad file does not discard completed work.
    public static async Task Run(BatchPlan plan, Engine engine, int quality, CancellationToken token, Action<BatchEntry>? changed = null)
    {
        if (!plan.CanRun) throw new InvalidOperationException("Set options for all files, or edit each file before starting.");
        foreach (var entry in plan.Entries)
        {
            if (token.IsCancellationRequested) { entry.State = entry.Job.Status = "Cancelled"; changed?.Invoke(entry); continue; }
            entry.State = entry.Job.Status = "Processing"; entry.Error = ""; changed?.Invoke(entry);
            try
            {
                var options = await plan.OptionsFor(entry, engine, quality, token);
                entry.Outputs = await engine.Run(new[] { entry.Input }, plan.Item, plan.Mode, options, token);
                entry.Job.Output = entry.Outputs.FirstOrDefault(); entry.State = "Done"; entry.Job.Status = entry.Outputs.Count == 1 ? "Done" : $"Done · {entry.Outputs.Count} outputs";
            }
            catch (OperationCanceledException) { entry.State = entry.Job.Status = "Cancelled"; }
            catch (Exception ex) { entry.State = "Failed"; entry.Error = ex.Message; entry.Job.Status = "Failed · " + ex.Message; }
            changed?.Invoke(entry);
        }
    }
}

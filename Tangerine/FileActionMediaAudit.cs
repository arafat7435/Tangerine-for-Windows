using ImageMagick;
namespace Tangerine;
public sealed partial class FileActionAudit
{
    Task<string> Ffmpeg(params string[] args) => Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-n" }.Concat(args), CancellationToken.None);
    async Task<JsonDocument> Probe(string file) => JsonDocument.Parse(await Engine.Process(engine.FindTool("ffprobe")!, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", file }, CancellationToken.None));
    static double Duration(JsonDocument doc) => double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
    static JsonElement[] Streams(JsonDocument doc, string type) => doc.RootElement.GetProperty("streams").EnumerateArray().Where(s => s.GetProperty("codec_type").GetString() == type).ToArray();
    async Task<float[]> Samples(string file)
    {
        var raw = Path.Combine(input, Guid.NewGuid() + ".f32"); await Ffmpeg("-i", file, "-vn", "-ac", "1", "-ar", "8000", "-f", "f32le", raw); var bytes = File.ReadAllBytes(raw); File.Delete(raw); var samples = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length); return samples;
    }
    static double Rms(float[] s, int start, int length) => Math.Sqrt(s.Skip(start).Take(length).Select(x => (double)x * x).Average());
    static double Power(float[] samples, int start, int count, double frequency) { double real = 0, imaginary = 0; for (int i = 0; i < count; i++) { double angle = 2 * Math.PI * frequency * i / 8000; real += samples[start + i] * Math.Cos(angle); imaginary += samples[start + i] * Math.Sin(angle); } return real * real + imaginary * imaginary; }
    async Task Media()
    {
        foreach (var ext in new[] { "wav", "mp3", "m4a", "flac", "ogg", "opus", "aiff", "wma" })
        {
            var file = Path.Combine(input, "tone." + ext);
            await Test("Audio fixture " + ext, async () => { await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-af", "volume=0.1", "-metadata", "title=private fixture", file); Track(file); });
            foreach (var id in Catalog.Tools(file).Select(x => x.Id)) await Test($"Audio {ext}: {id}", async () =>
            {
                var result = await Tool(file, id, new() { Start = .5, Duration = .6, Channels = 2, Quality = 40 }); using var probe = await Probe(result);
                Check(Streams(probe, "audio").Length > 0 && Duration(probe) > .4, "No playable audio");
                if (id == "trim") Check(Math.Abs(Duration(probe) - .6) < .18, "Trim duration incorrect");
                else Check(Math.Abs(Duration(probe) - 2) < .3, "Action changed duration");
                if (id == "channels") Check(Streams(probe, "audio")[0].GetProperty("channels").GetInt32() == 2, "Wrong channel count");
                if (id == "visualize") Check(Streams(probe, "video").Length > 0, "No waveform video");
                if (id == "metadata") Check(!probe.RootElement.ToString().Contains("private fixture"), "Private metadata retained");
            });
        }
        var audio = Path.Combine(input, "tone.wav");
        foreach (var (ext, vcodec, acodec) in new[] { ("mp4", "libx264", "aac"), ("mov", "libx264", "aac"), ("mkv", "libx264", "aac"), ("webm", "libvpx-vp9", "libopus"), ("avi", "mpeg4", "libmp3lame"), ("wmv", "wmv2", "wmav2") })
        {
            var file = Path.Combine(input, "clip." + ext);
            await Test("Video fixture " + ext, async () => { await Ffmpeg("-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=2", "-i", audio, "-c:v", vcodec, "-pix_fmt", "yuv420p", "-c:a", acodec, "-metadata", "title=private fixture", "-shortest", file); Track(file); });
            foreach (var id in Catalog.Tools(file).Select(x => x.Id)) await Test($"Video {ext}: {id}", async () =>
            {
                var result = await Tool(file, id, new() { X = 4, Y = 6, Width = 100, Height = 80, Start = .4, Duration = .75, Speed = 2, Quality = 40 });
                if (id == "snapshot") { using var image = new MagickImage(result); Check(image.Width == 160 && image.Height == 120, "Snapshot dimensions incorrect"); return; }
                if (id == "split")
                {
                    var files = Directory.GetFiles(result, "*.mp4"); Check(files.Length == 3, "Wrong clip count"); double total = 0; foreach (var part in files) { using var p = await Probe(part); Check(Streams(p, "video").Length > 0 && Duration(p) > 0, "Empty clip"); total += Duration(p); }
                    Check(Math.Abs(total - 2) < .5, "Split lost content"); return;
                }
                using var probe = await Probe(result); var stream = Streams(probe, "video").Single(); Check(Duration(probe) > 0, "Empty video");
                Check(stream.GetProperty("width").GetInt32() == (id == "crop" ? 100 : 160) && stream.GetProperty("height").GetInt32() == (id == "crop" ? 80 : 120), "Wrong video dimensions");
                if (id == "mute") Check(Streams(probe, "audio").Length == 0, "Muted audio remains");
                if (id == "metadata") Check(!probe.RootElement.ToString().Contains("private fixture"), "Metadata remains");
                if (id == "trim") Check(Math.Abs(Duration(probe) - .75) < .25, "Trim duration incorrect");
                if (id == "speed") Check(Math.Abs(Duration(probe) - 1) < .3, "Speed duration incorrect");
                if (id == "redact")
                {
                    var frame = Path.Combine(input, Guid.NewGuid() + ".png"); await Ffmpeg("-i", result, "-frames:v", "1", frame); using var image = new MagickImage(frame); using var pixels = image.GetPixels(); var c = pixels.GetPixel(40, 40).ToColor()!; Check(c.R < 15 && c.G < 15 && c.B < 15, "Video redaction is not black"); File.Delete(frame);
                }
            });
        }
        var video = Path.Combine(input, "clip.mp4");
        foreach (var ext in new[] { "ogg", "opus", "avi", "wmv" }) await Test("Compression quality changes encoded size for " + ext, async () =>
        {
            var source = Path.Combine(input, (ext is "ogg" or "opus" ? "tone." : "clip.") + ext); var low = await Tool(source, "compress", new() { Quality = 10 }); var high = await Tool(source, "compress", new() { Quality = 90 }); Check(new FileInfo(low).Length < new FileInfo(high).Length, "Compression quality setting has no effect");
        });
        await Test("GIF metadata retains animation frames", async () => { var gif = Path.Combine(input, "animation.gif"); await Ffmpeg("-f", "lavfi", "-i", "testsrc=size=80x60:rate=5:duration=1", gif); Track(gif); using var source = new MagickImageCollection(gif); using var result = new MagickImageCollection(await Tool(gif, "metadata")); Check(source.Count == result.Count && result.Count > 1, "Animation frames dropped"); });
        foreach (var ext in new[] { "mp4", "mov", "mkv", "webm" }) await Test("Odd crop dimensions are exact in " + ext, async () => { using var p = await Probe(await Tool(Path.Combine(input, "clip." + ext), "crop", new() { X = 3, Y = 3, Width = 99, Height = 77 })); var s = Streams(p, "video")[0]; Check(s.GetProperty("width").GetInt32() == 99 && s.GetProperty("height").GetInt32() == 77, "Crop silently rounded dimensions"); });
        foreach (var id in new[] { "trim", "snapshot", "bleep" }) await Test("Reject time beyond EOF for " + id, () => Reject(async () => { await Tool(id == "bleep" ? audio : video, id, new() { Start = 99, Duration = .5 }); }));
        foreach (var id in new[] { "crop", "redact" }) await Test("Reject outside video " + id, () => Reject(async () => { await Tool(video, id, new() { X = 150, Y = 0, Width = 40, Height = 20 }); }));
        await Test("Normalize increases quiet audio amplitude", async () => { var before = await Samples(audio); var after = await Samples(await Tool(audio, "normalize")); Check(Rms(after, 1000, 8000) > Rms(before, 1000, 8000) * 3, "Normalization had no effect"); });
        await Test("Bleep replaces requested interval and preserves other audio", async () => { var before = await Samples(audio); var after = await Samples(await Tool(audio, "bleep", new() { Start = .5, Duration = .5 })); Check(Power(after, 4500, 2000, 1000) > Power(after, 4500, 2000, 440) * 20, "No bleep replacement"); Check(Math.Abs(Rms(after, 9500, 2500) - Rms(before, 9500, 2500)) < .005, "Audio outside interval changed"); });
        foreach (var speed in new[] { .25, .5, 4.0, 8.0 }) await Test("Video speed " + speed, async () => { using var p = await Probe(await Tool(video, "speed", new() { Speed = speed })); Check(Math.Abs(Duration(p) - 2 / speed) < .35, "Speed produced wrong duration"); });
        var silent = Path.Combine(input, "silent.mp4"); await Ffmpeg("-f", "lavfi", "-i", "testsrc2=size=120x160:rate=25:duration=2", "-c:v", "libx264", silent); Track(silent);
        await Test("Silent video speed works without audio stream", async () => { using var p = await Probe(await Tool(silent, "speed")); Check(Math.Abs(Duration(p) - 1) < .25 && Streams(p, "audio").Length == 0, "Silent speed failed"); });
        await Test("Join mixed audio/silent and landscape/portrait clips", async () => { using var p = await Probe((await Tool(new[] { silent, video }, "join"))[0]); Check(Math.Abs(Duration(p) - 4) < .4 && Streams(p, "video").Length == 1 && Streams(p, "audio").Length == 1, "Join lost video or audio"); });
        await Test("Rotated phone video crop uses displayed bounds", async () =>
        {
            var rotated = Path.Combine(input, "rotated.mp4"); await Ffmpeg("-display_rotation", "90", "-i", video, "-c", "copy", rotated); Track(rotated);
            using var p = await Probe(await Tool(rotated, "crop", new() { X = 20, Y = 130, Width = 80, Height = 30 })); var s = Streams(p, "video")[0]; Check(s.GetProperty("width").GetInt32() == 80 && s.GetProperty("height").GetInt32() == 30, "Portrait crop failed");
        });
        await Test("Running FFmpeg cancellation terminates promptly", async () => { using var ct = new CancellationTokenSource(); var running = Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-re", "-f", "lavfi", "-i", "sine=frequency=440", "-f", "null", "-" }, ct.Token); await Task.Delay(200); ct.Cancel(); try { await running.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { } });
    }
}

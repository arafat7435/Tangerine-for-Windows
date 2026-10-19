namespace Tangerine;
public sealed partial class Engine
{
    List<string> MediaArgs(string input) => new() { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-i", input };
    static void Codecs(List<string> args, string format, int quality, int width = 0, int height = 0)
    {
        switch (format)
        {
            case "mp4": case "mov": case "mkv": args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", N(35 - quality * .2), "-pix_fmt", width % 2 != 0 || height % 2 != 0 ? "yuv444p" : "yuv420p", "-c:a", "aac", "-b:a", "192k" }); break;
            case "webm": args.AddRange(new[] { "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", N(50 - quality * .25), "-pix_fmt", "yuv420p", "-c:a", "libopus" }); break;
            case "avi": args.AddRange(new[] { "-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame" }); break;
            case "wmv": args.AddRange(new[] { "-c:v", "wmv2", "-c:a", "wmav2" }); break;
            case "mp3": args.AddRange(new[] { "-vn", "-c:a", "libmp3lame", "-b:a", $"{Math.Clamp(quality * 3, 64, 320)}k" }); break;
            case "m4a": args.AddRange(new[] { "-vn", "-c:a", "aac", "-b:a", $"{Math.Clamp(quality * 3, 64, 320)}k" }); break;
            case "wav": args.AddRange(new[] { "-vn", "-c:a", "pcm_s16le" }); break;
            case "flac": args.AddRange(new[] { "-vn", "-c:a", "flac" }); break;
            case "ogg": args.AddRange(new[] { "-vn", "-c:a", "libvorbis", "-q:a", "5" }); break;
            case "opus": args.AddRange(new[] { "-vn", "-c:a", "libopus" }); break;
            case "aiff": case "aif": args.AddRange(new[] { "-vn", "-c:a", "pcm_s16be" }); break;
            case "wma": args.AddRange(new[] { "-vn", "-c:a", "wmav2" }); break;
        }
    }
    async Task MediaConvert(string input, string target, string format, OperationOptions o, CancellationToken ct)
    {
        var source = await InspectMedia(input, ct);
        if ((Catalog.Kind(input) == FileKind.Audio || format == "mp3") && !source.Audio) throw new InvalidDataException("This file has no audio stream to convert.");
        if (Catalog.Kind(input) == FileKind.Video && format != "mp3" && !source.Video) throw new InvalidDataException("This file has no video stream to convert.");
        var a = MediaArgs(input);
        if (format == "gif") a.AddRange(new[] { "-filter_complex", "fps=15,scale=640:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-an" });
        else Codecs(a, format, o.Quality);
        if (format is "mp4" or "mov" or "mkv" or "webm" or "avi" or "wmv") a.AddRange(new[] { "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2" });
        a.Add(target); await Process(Ffmpeg, a, ct); await ValidateMedia(target, ct);
    }
    async Task<List<string>> MediaTool(string input, string target, string id, OperationOptions o, CancellationToken ct)
    {
        var a = MediaArgs(input); var video = Catalog.Kind(input) == FileKind.Video; var format = Catalog.Extension(input); bool recode = true;
        var source = await InspectMedia(input, ct);
        if (id is "trim" or "snapshot" or "bleep" && source.Duration > 0 && o.Start >= source.Duration) throw new ArgumentException($"The start time is past the end of this {N(source.Duration)}-second file.");
        if (id is "crop" or "redact")
        {
            if (!source.Video || o.X + (long)o.Width > source.Width || o.Y + (long)o.Height > source.Height) throw new ArgumentException($"Choose a rectangle inside the {source.Width} × {source.Height} video.");
            if (id == "crop" && format is "avi" or "wmv" && (o.Width % 2 != 0 || o.Height % 2 != 0)) throw new ArgumentException("This format requires an even crop width and height.");
        }
        switch (id)
        {
            case "compress": if (!video && format is "wav" or "aiff") { format = "flac"; target = Path.ChangeExtension(target, format); } break;
            case "metadata": a.AddRange(new[] { "-map", "0", "-map_metadata", "-1", "-map_metadata:s", "-1", "-map_chapters", "-1", "-c", "copy" }); recode = false; break;
            case "mute": a.AddRange(new[] { "-an", "-c:v", "copy" }); recode = false; break;
            case "normalize": a.AddRange(new[] { "-af", $"{LoudnessFilter(o)},aresample={source.SampleRate},asetnsamples=n=4096:p=0" }); break;
            case "trim": a.AddRange(new[] { "-ss", N(o.Start), "-t", N(o.Duration) }); break;
            case "crop": a.AddRange(new[] { "-vf", $"crop={o.Width}:{o.Height}:{o.X}:{o.Y}:exact=1" }); break;
            case "redact":
                // Blur before cropping so even a one-pixel selection has neighbouring pixels to sample.
                if (o.BlurRedaction) a.AddRange(new[] { "-filter_complex", $"[0:v]format=yuv444p,split[base][part];[part]boxblur=luma_radius='min({Math.Max(1, Math.Min(20, Math.Min(o.Width, o.Height) / 4))},min(w,h)/2)':luma_power=3,crop={o.Width}:{o.Height}:{o.X}:{o.Y}:exact=1[blur];[base][blur]overlay={o.X}:{o.Y}:format=yuv444[v]", "-map", "[v]", "-map", "0:a?" });
                else a.AddRange(new[] { "-vf", $"drawbox=x={o.X}:y={o.Y}:w={o.Width}:h={o.Height}:color={o.RedactionColor.Replace("#", "0x")}:t=fill" }); break;
            case "speed":
                a.AddRange(new[] { "-vf", $"setpts=PTS/{N(o.Speed)}" });
                if (await HasAudio(input, ct)) { var filters = new List<string>(); double rate = o.Speed; while (rate > 2) { filters.Add("atempo=2"); rate /= 2; } while (rate < .5) { filters.Add("atempo=0.5"); rate /= .5; } filters.Add("atempo=" + N(rate)); a.AddRange(new[] { "-af", string.Join(",", filters) }); }
                break;
            case "channels": a.AddRange(new[] { "-af", ChannelFilter(o), "-ac", o.Channels.ToString() }); break;
            case "snapshot":
                if (o.SnapshotTimes.Count > 0)
                {
                    var frameFolder = Path.Combine(Path.GetDirectoryName(target)!, Catalog.Stem(input) + " Frames"); Directory.CreateDirectory(frameFolder);
                    foreach (var time in o.SnapshotTimes.Distinct().Order())
                    {
                        if (time >= source.Duration) throw new ArgumentException("Choose a frame before the end of the video.");
                        var frame = Unique(Path.Combine(frameFolder, "Frame " + time.ToString("00000.000", CultureInfo.InvariantCulture) + ".png"));
                        await SaveSnapshot(input, frame, time, ct);
                    }
                    return new() { frameFolder };
                }
                target = Path.ChangeExtension(target, "png"); await SaveSnapshot(input, target, o.Start, ct); return new() { target };
            case "split":
                var folder = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(input) + " Clips"); Directory.CreateDirectory(folder);
                Codecs(a, "mp4", o.Quality, source.Width, source.Height);
                if (o.SplitPoints.Count > 0) { var points = o.SplitPoints.Distinct().Order().ToArray(); if (points.Any(p => p <= 0 || p >= source.Duration)) throw new ArgumentException("Split points must be inside the video."); var times = string.Join(",", points.Select(N)); a.AddRange(new[] { "-force_key_frames", times, "-f", "segment", "-segment_times", times }); }
                else a.AddRange(new[] { "-force_key_frames", $"expr:gte(t,n_forced*{N(o.Duration)})", "-f", "segment", "-segment_time", N(o.Duration) });
                a.AddRange(new[] { "-reset_timestamps", "1", Path.Combine(folder, "Clip %03d.mp4") }); await Process(Ffmpeg, a, ct); return new() { folder };
            case "visualize":
                target = Path.ChangeExtension(target, "mp4"); a.AddRange(new[] { "-filter_complex", "[0:a]showwaves=s=1280x720:mode=cline:colors=0xff5900:rate=30,format=yuv420p[v]", "-map", "[v]", "-map", "0:a", "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac", target }); await Process(Ffmpeg, a, ct); return new() { target };
            case "bleep":
                a.AddRange(new[] { "-filter_complex", BleepFilter(o), "-map", "[out]" }); break;
            default: throw new NotSupportedException(id);
        }
        if (recode) Codecs(a, format, o.Quality, id == "crop" ? o.Width : source.Width, id == "crop" ? o.Height : source.Height);
        if (id == "compress")
        {
            // These encoders otherwise use fixed quality/bitrate defaults.
            if (format == "ogg") a.AddRange(new[] { "-q:a", N(-1 + o.Quality * .11) });
            else if (format == "opus") a.AddRange(new[] { "-b:a", $"{24 + o.Quality * 2}k" });
            else if (format is "avi" or "wmv") a.AddRange(new[] { "-q:v", N(31 - o.Quality * .29) });
        }
        a.Add(target); await Process(Ffmpeg, a, ct); await ValidateMedia(target, ct); return new() { target };
    }
    async Task SaveSnapshot(string input, string target, double time, CancellationToken ct)
    {
        var args = MediaArgs(input); args.AddRange(new[] { "-ss", N(time), "-frames:v", "1", target }); await Process(Ffmpeg, args, ct);
        // FFmpeg can exit successfully without a frame when the selected time falls after the last video frame.
        if (!File.Exists(target) || new FileInfo(target).Length == 0) throw new InvalidDataException("There is no video frame at this time. Choose an earlier frame.");
    }
    internal static string LoudnessFilter(OperationOptions o) => $"loudnorm=I={N(o.TargetLoudness)}:TP={N(o.TruePeak)}:LRA={N(o.LoudnessRange)}";
    internal static string ChannelFilter(OperationOptions o) => $"aformat=channel_layouts=stereo,pan=stereo|c0={N(o.LeftVolume / 100)}*c0|c1={N(o.RightVolume / 100)}*c1" + (o.Channels == 1 ? ",pan=mono|c0=0.5*c0+0.5*c1" : "");
    internal static string BleepFilter(OperationOptions o)
    {
        var ranges = (o.BleepRanges.Count == 0 ? new[] { new TimeRange(o.Start, o.Start + o.Duration) } : o.BleepRanges.ToArray()).OrderBy(r => r.Start).ToList();
        var merged = new List<TimeRange>(); foreach (var r in ranges) { if (merged.Count > 0 && r.Start <= merged[^1].End) merged[^1] = new(merged[^1].Start, Math.Max(merged[^1].End, r.End)); else merged.Add(r); }
        var filters = new List<string> { $"[0:a]volume=enable='{string.Join("+", merged.Select(r => $"between(t,{N(r.Start)},{N(r.End)})"))}':volume=0[clean]" };
        for (int i = 0; i < merged.Count; i++) { var r = merged[i]; filters.Add($"sine=frequency=1000:sample_rate=48000:duration={N(r.End - r.Start)},adelay={N(r.Start * 1000)}:all=1,volume=0.35[tone{i}]"); }
        filters.Add("[clean]" + string.Concat(Enumerable.Range(0, merged.Count).Select(i => $"[tone{i}]")) + $"amix=inputs={merged.Count + 1}:duration=first:normalize=0[out]"); return string.Join(";", filters);
    }
    record MediaDetails(bool Video, bool Audio, int Width, int Height, double Duration, int SampleRate);
    internal async Task<(int Width, int Height, double Duration)> MediaBounds(string input, CancellationToken ct)
    {
        var media = await InspectMedia(input, ct); return (media.Width, media.Height, media.Duration);
    }
    async Task<MediaDetails> InspectMedia(string input, CancellationToken ct)
    {
        var probe = FindTool("ffprobe") ?? throw new InvalidOperationException("Place ffprobe.exe beside ffmpeg.exe.");
        using var json = JsonDocument.Parse(await Process(probe, new[] { "-v", "error", "-show_entries", "stream=codec_type,width,height,sample_rate:stream_side_data=rotation:format=duration", "-of", "json", input }, ct));
        bool video = false, audio = false; int width = 0, height = 0, sampleRate = 48000; double duration = 0;
        foreach (var s in json.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            if (type == "audio" && !audio) { audio = true; if (s.TryGetProperty("sample_rate", out var rate) && int.TryParse(rate.GetString(), out var hz) && hz > 0) sampleRate = hz; }
            if (type == "video" && !video)
            {
                video = true; width = s.TryGetProperty("width", out var w) ? w.GetInt32() : 0; height = s.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                if (s.TryGetProperty("side_data_list", out var data)) foreach (var entry in data.EnumerateArray()) if (entry.TryGetProperty("rotation", out var rotation) && Math.Abs(rotation.GetDouble() % 180) == 90) (width, height) = (height, width);
            }
        }
        if (json.RootElement.TryGetProperty("format", out var f) && f.TryGetProperty("duration", out var d)) double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
        return new(video, audio, width, height, duration, sampleRate);
    }
    async Task ValidateMedia(string file, CancellationToken ct) { var result = await InspectMedia(file, ct); if ((!result.Video && !result.Audio) || result.Duration <= 0) throw new InvalidDataException("The operation produced no playable media. Check the selected time range."); }
    async Task<bool> HasAudio(string input, CancellationToken ct)
    {
        var probe = FindTool("ffprobe"); if (probe == null) throw new InvalidOperationException("Place ffprobe.exe beside ffmpeg.exe to detect audio streams.");
        var result = await Process(probe, new[] { "-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "csv=p=0", input }, ct); return result.Trim().Length > 0;
    }
    async Task JoinVideos(string[] inputs, string target, CancellationToken ct)
    {
        // Normalize dimensions, frame rate and audio so mixed clips can be concatenated reliably.
        var work = Path.GetDirectoryName(target)!; var normalized = new List<string>();
        for (int i = 0; i < inputs.Length; i++)
        {
            var p = Path.Combine(work, $"join-part-{i}.mp4"); var a = MediaArgs(inputs[i]); var audio = await HasAudio(inputs[i], ct);
            if (!audio) a.AddRange(new[] { "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-shortest" });
            a.AddRange(new[] { "-map", "0:v:0", "-map", audio ? "0:a:0" : "1:a:0", "-vf", "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,fps=30,setsar=1", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-ar", "48000", "-ac", "2", p }); await Process(Ffmpeg, a, ct); normalized.Add(p);
        }
        var list = Path.Combine(work, "concat.txt"); File.WriteAllLines(list, normalized.Select(p => "file '" + p.Replace('\\', '/').Replace("'", "'\\''") + "'"), new UTF8Encoding(false));
        await Process(Ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy", target }, ct);
    }
}

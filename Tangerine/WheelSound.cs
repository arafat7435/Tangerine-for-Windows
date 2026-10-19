namespace Tangerine;

// Prepared native voices let the 60 ms ticks overlap. PlaySound's single
// restartable channel can cancel a tick before the audio device renders it.
internal sealed class WheelSound : IDisposable
{
    internal static bool Available => typeof(WheelSound).Assembly.GetManifestResourceNames().Contains("Tangerine.Assets.highlight.wav");
    const int WarmVoices = 8, MaxVoices = 24;
    readonly List<Voice> voices = new();
    GCHandle pcm;
    WaveFormat format;
    uint length;
    bool disposed;
    int completed, interrupted;
    long lastAttempt;
    internal int PlaybackCount { get; private set; }
    internal int CompletedCount => Volatile.Read(ref completed);
    internal int InterruptedCount => Volatile.Read(ref interrupted);
    internal int VoiceCount => voices.Count;

    internal void Prepare()
    {
        if (disposed || voices.Count > 0) return;
        if (lastAttempt != 0 && Environment.TickCount64 - lastAttempt < 500) return;
        lastAttempt = Environment.TickCount64;
        if (!pcm.IsAllocated)
        {
            using var stream = typeof(WheelSound).Assembly.GetManifestResourceStream("Tangerine.Assets.highlight.wav");
            if (stream == null) return;
            using var reader = new BinaryReader(stream); stream.Position = 12;
            byte[]? samples = null;
            while (stream.Position + 8 <= stream.Length)
            {
                var id = Encoding.ASCII.GetString(reader.ReadBytes(4)); var size = reader.ReadUInt32(); var end = stream.Position + size + (size & 1);
                if (end > stream.Length) throw new InvalidDataException("Incomplete wheel sound.");
                if (id == "fmt ") { format = new() { Tag = reader.ReadUInt16(), Channels = reader.ReadUInt16(), SampleRate = reader.ReadUInt32(), BytesPerSecond = reader.ReadUInt32(), BlockAlign = reader.ReadUInt16(), Bits = reader.ReadUInt16() }; }
                if (id == "data") samples = reader.ReadBytes(checked((int)size)); stream.Position = end;
            }
            if (format.Tag != 1 || format.Bits != 16 || samples == null) throw new InvalidDataException("The wheel sound must be 16-bit PCM.");
            length = (uint)samples.Length; pcm = GCHandle.Alloc(samples, GCHandleType.Pinned);
        }
        for (int i = 0; i < WarmVoices; i++) { var voice = CreateVoice(); if (voice == null) break; voices.Add(voice); }
    }

    Voice? CreateVoice() => Voice.Open(ref format, pcm.AddrOfPinnedObject(), length, () => Interlocked.Increment(ref completed));
    internal bool Play()
    {
        if (disposed) return false; Prepare(); if (voices.Count == 0) return false;
        var voice = voices.FirstOrDefault(v => !v.Busy);
        if (voice == null && voices.Count < MaxVoices) { voice = CreateVoice(); if (voice != null) voices.Add(voice); }
        if (voice == null)
        {
            // Only an extreme burst (>24 overlapping ticks) steals the oldest
            // tail, as the reference's bounded voice pool does.
            voice = voices.MinBy(v => v.Started)!; voice.Stop(); Interlocked.Increment(ref interrupted);
        }
        if (!voice.Play())
        {
            // Reopen after a driver failure. Never substitute a system alert.
            ReleaseVoices(); lastAttempt = 0; Prepare(); voice = voices.FirstOrDefault();
            if (voice == null || !voice.Play()) return false;
        }
        PlaybackCount++; return true;
    }

    void ReleaseVoices() { foreach (var voice in voices) voice.Dispose(); voices.Clear(); }
    public void Dispose() { if (disposed) return; disposed = true; ReleaseVoices(); if (pcm.IsAllocated) pcm.Free(); }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormat { internal ushort Tag, Channels; internal uint SampleRate, BytesPerSecond; internal ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader { internal IntPtr Data; internal uint Length, Recorded; internal UIntPtr User; internal uint Flags, Loops; internal IntPtr Next; internal UIntPtr Reserved; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate void WaveCallback(IntPtr output, uint message, UIntPtr instance, IntPtr header, IntPtr reserved);

    sealed class Voice : IDisposable
    {
        static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();
        readonly WaveCallback callback;
        readonly Action completed;
        IntPtr output, header;
        int busy;
        internal bool Busy => Volatile.Read(ref busy) != 0;
        internal long Started { get; private set; }
        Voice(Action completed) { this.completed = completed; callback = Callback; }
        internal static Voice? Open(ref WaveFormat format, IntPtr data, uint length, Action completed)
        {
            var voice = new Voice(completed);
            if (waveOutOpen(out voice.output, new UIntPtr(uint.MaxValue), ref format, voice.callback, UIntPtr.Zero, 0x30000) != 0) return null;
            voice.header = Marshal.AllocHGlobal((int)HeaderSize);
            Marshal.StructureToPtr(new WaveHeader { Data = data, Length = length }, voice.header, false);
            if (waveOutPrepareHeader(voice.output, voice.header, HeaderSize) == 0) return voice;
            voice.Dispose(); return null;
        }
        void Callback(IntPtr output, uint message, UIntPtr instance, IntPtr header, IntPtr reserved)
        {
            // No audio API calls on the driver callback thread.
            if (message == 0x3BD && Interlocked.Exchange(ref busy, 0) == 1) completed();
        }
        internal bool Play()
        {
            Started = Stopwatch.GetTimestamp(); Volatile.Write(ref busy, 1);
            if (waveOutWrite(output, header, HeaderSize) == 0) return true;
            Volatile.Write(ref busy, 0); return false;
        }
        internal void Stop() { Interlocked.Exchange(ref busy, 0); if (output != IntPtr.Zero) waveOutReset(output); }
        public void Dispose()
        {
            Stop();
            if (output != IntPtr.Zero) { if (header != IntPtr.Zero) waveOutUnprepareHeader(output, header, HeaderSize); waveOutClose(output); output = IntPtr.Zero; }
            if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }

    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr output, UIntPtr device, ref WaveFormat format, WaveCallback callback, UIntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr output, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr output, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr output, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr output);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr output);
}

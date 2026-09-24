using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Darkmount.App.Input;

/// <summary>
/// Analyses what the PC is playing (WASAPI loopback of the default speakers; the microphone is never used) into a
/// 0..1 level and 16 frequency bands for audio-reactive lighting. Runs only while an effect needs it.
/// </summary>
public sealed class AudioAnalyzer : IDisposable
{
    public const int BandCount = 16;
    const int FftBits = 11, FftSize = 1 << FftBits;

    readonly object _gate = new();
    readonly float[] _ring = new float[FftSize];
    int _ringPos, _sampleRate = 48000;
    WasapiLoopbackCapture? _capture;
    DateTime _retryAt;

    readonly double[] _bands = new double[BandCount];
    readonly double[] _bandPeak = Enumerable.Repeat(1e-4, BandCount).ToArray();
    double _level, _levelPeak = 1e-4;

    /// <summary>Starts or stops capturing (cheap to call every frame).</summary>
    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (!active) { StopCapture(); return; }
            if (_capture is not null || DateTime.UtcNow < _retryAt) return;
            try
            {
                var capture = new WasapiLoopbackCapture();
                _sampleRate = capture.WaveFormat.SampleRate;
                capture.DataAvailable += OnData;
                capture.RecordingStopped += (_, e) =>
                {
                    if (e.Exception is not null) Log.Write($"Audio capture stopped: {e.Exception.Message}");
                    lock (_gate) { if (_capture == capture) { _capture = null; _retryAt = DateTime.UtcNow.AddSeconds(2); } }
                    capture.Dispose();
                };
                capture.StartRecording();
                _capture = capture;
            }
            catch (Exception e)
            {
                Log.Write($"Audio capture unavailable: {e.Message}");
                _retryAt = DateTime.UtcNow.AddSeconds(10);
            }
        }
    }

    void StopCapture()
    {
        var c = _capture;
        _capture = null;
        try { c?.StopRecording(); } catch (Exception) { /* device gone */ }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture c) return;
        var fmt = c.WaveFormat;
        int channels = Math.Max(1, fmt.Channels);
        lock (_gate)
        {
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            {
                int frames = e.BytesRecorded / (4 * channels);
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++) sum += BitConverter.ToSingle(e.Buffer, (f * channels + ch) * 4);
                    Push(sum / channels);
                }
            }
            else if (fmt.BitsPerSample == 16)
            {
                int frames = e.BytesRecorded / (2 * channels);
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++) sum += BitConverter.ToInt16(e.Buffer, (f * channels + ch) * 2) / 32768f;
                    Push(sum / channels);
                }
            }
        }
    }

    void Push(float s)
    {
        _ring[_ringPos] = s;
        _ringPos = (_ringPos + 1) % FftSize;
    }

    /// <summary>Recomputes level and bands from the latest ~40 ms of audio (call once per lighting frame).</summary>
    public (double Level, IReadOnlyList<double> Bands) Analyse()
    {
        var data = new Complex[FftSize];
        double sumSquares = 0;
        lock (_gate)
        {
            if (_capture is null) { Decay(); return (_level, (double[])_bands.Clone()); }
            for (int i = 0; i < FftSize; i++)
            {
                float s = _ring[(_ringPos + i) % FftSize];
                sumSquares += s * s;
                data[i].X = (float)(s * FastFourierTransform.HannWindow(i, FftSize));
            }
        }
        FastFourierTransform.FFT(true, FftBits, data);

        // Log-spaced bands from 40 Hz to 16 kHz.
        double nyquist = _sampleRate / 2.0, binHz = nyquist / (FftSize / 2);
        for (int b = 0; b < BandCount; b++)
        {
            double lo = 40 * Math.Pow(16000.0 / 40, (double)b / BandCount), hi = 40 * Math.Pow(16000.0 / 40, (double)(b + 1) / BandCount);
            int from = Math.Max(1, (int)(lo / binHz)), to = Math.Min(FftSize / 2 - 1, Math.Max(from, (int)(hi / binHz)));
            double peak = 0;
            for (int i = from; i <= to; i++) peak = Math.Max(peak, Math.Sqrt(data[i].X * data[i].X + data[i].Y * data[i].Y));
            _bandPeak[b] = Math.Max(peak, _bandPeak[b] * 0.995); // automatic gain: slowly forget loud peaks
            double v = Math.Clamp(peak / _bandPeak[b], 0, 1);
            _bands[b] = v > _bands[b] ? v : _bands[b] * 0.8 + v * 0.2; // fast attack, smooth release
        }
        double rms = Math.Sqrt(sumSquares / FftSize);
        _levelPeak = Math.Max(rms, _levelPeak * 0.997);
        double level = rms < 1e-4 ? 0 : Math.Clamp(rms / _levelPeak, 0, 1);
        _level = level > _level ? level : _level * 0.85 + level * 0.15;
        return (_level, (double[])_bands.Clone());
    }

    void Decay()
    {
        _level *= 0.8;
        for (int b = 0; b < BandCount; b++) _bands[b] *= 0.8;
    }

    public void Dispose()
    {
        lock (_gate) StopCapture();
    }
}

/// <summary>Speaker volume (for the volume bar when the dock dial turns) and microphone mute state.</summary>
public sealed class AudioStatus : IDisposable
{
    readonly MMDeviceEnumerator _enumerator = new();
    MMDevice? _speakers, _mic;
    DateTime _nextRefresh;
    long _volumeChangedTicks;

    /// <summary>Speaker volume 0..1 (null when unavailable).</summary>
    public double? Volume { get; private set; }

    public bool SpeakersMuted { get; private set; }

    /// <summary>True when the default microphone is muted.</summary>
    public bool? MicMuted { get; private set; }

    public DateTime VolumeChangedUtc => new(Interlocked.Read(ref _volumeChangedTicks), DateTimeKind.Utc);

    /// <summary>Polls the default devices (call a few times per second from the lighting thread).</summary>
    public void Poll()
    {
        try
        {
            if (DateTime.UtcNow >= _nextRefresh)
            {
                _nextRefresh = DateTime.UtcNow.AddSeconds(5); // follow default-device changes
                _speakers?.Dispose();
                _mic?.Dispose();
                _speakers = _enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : null;
                _mic = _enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : null;
            }
            double? volume = _speakers?.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool muted = _speakers?.AudioEndpointVolume.Mute ?? false;
            if (Volume is { } old && volume is { } now && (Math.Abs(old - now) > 0.001 || muted != SpeakersMuted))
                Interlocked.Exchange(ref _volumeChangedTicks, DateTime.UtcNow.Ticks);
            Volume = volume;
            SpeakersMuted = muted;
            MicMuted = _mic?.AudioEndpointVolume.Mute;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _nextRefresh = DateTime.UtcNow; // device changed; re-acquire next time
        }
    }

    public void Dispose()
    {
        _speakers?.Dispose();
        _mic?.Dispose();
        _enumerator.Dispose();
    }
}

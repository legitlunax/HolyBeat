using System;
using System.Collections.Generic;
using HolyBeat.LiveGuard.Core.Dsp;

namespace HolyBeat.LiveGuard.Core.Analysis;

/// <summary>Snapshot of everything the detector and the UI need to know about a block of audio.</summary>
public sealed class AnalysisSnapshot
{
    public double TimestampMs { get; init; }
    public int SampleRate { get; set; }

    public double MomentaryLufs { get; set; } = -70.0;
    public double ShortTermLufs { get; set; } = -70.0;
    public double IntegratedLufs { get; set; } = -70.0;
    public double TruePeakDb { get; set; } = -140.0;
    public double CrestFactorDb { get; set; }

    /// <summary>Share of the spectrum sitting in the 40-80 Hz pressure band (0..1).</summary>
    public double SubPressure { get; set; }

    /// <summary>How much the 250 Hz - 1.5 kHz mid range is scooped out (0..1).</summary>
    public double MidScoop { get; set; }

    /// <summary>Peak narrow-band prominence inside the 2.5 kHz - 6 kHz pain band, in dB.</summary>
    public double HarshnessDb { get; set; }
    public double HarshFrequencyHz { get; set; }

    /// <summary>Spectral instability (background filter sweeps) (0..1).</summary>
    public double DarkInstability { get; set; }

    /// <summary>Side energy relative to mid energy above 4 kHz (0..1+).</summary>
    public double HighFrequencySideRatio { get; set; }

    /// <summary>Depth of the deepest kick-triggered envelope dip, in dB.</summary>
    public double PumpingDepthDb { get; set; }

    /// <summary>Approximate rate of the pumping modulation, in Hz.</summary>
    public double PumpingRateHz { get; set; }

    public double SpectralFlatness { get; set; }
    public double SpectralCentroidHz { get; set; }

    public float[] SpectrumDb { get; set; } = Array.Empty<float>();
    public float[] BandDb { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Real-time spectrum and feature analyser. Produces the metrics that the profile
/// detector uses to classify a track as Mute / Noise / Dark Noise / Dual-Layer / Safe.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private readonly Fft _fft;
    private readonly double[] _real;
    private readonly double[] _imaginary;
    private readonly double[] _window;
    private readonly float[] _ring;
    private readonly float[] _monoRing;
    private int _ringIndex;
    private int _filled;
    private int _hopCounter;

    private readonly Queue<double> _centroidHistory = new Queue<double>();
    private readonly AnalysisSnapshot _snapshot = new AnalysisSnapshot();
    private float[] _spectrumBuffer = Array.Empty<float>();
    private float[] _smoothedBuffer = Array.Empty<float>();
    private double _bandEnergyReference;
    private float[] _bandShape = Array.Empty<float>();
    private float[] _previousShape = Array.Empty<float>();
    private bool _havePreviousShape;
    private readonly Queue<double> _similarityHistory = new Queue<double>();
    private readonly float[] _bandBuffer = new float[BandCount];
    private int _fftHop = 512;
    private readonly Queue<double> _envelopeHistory = new Queue<double>();
    private double _envelopeAccumulator;
    private int _envelopeSamples;
    private double _peak;
    private double _sumSquares;
    private int _sampleCounter;

    private readonly LoudnessMeter _loudness = new LoudnessMeter();
    private Biquad? _sideHighPass;
    private Biquad? _midHighPass;
    private Biquad? _sideLowPass;

    private int _sampleRate;
    private int _fftSize;

    public const int BandCount = 24;

    public SpectrumAnalyzer(int sampleRate = 48000, int fftSize = 2048)
    {
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _fft = new Fft(fftSize);
        _real = new double[fftSize];
        _imaginary = new double[fftSize];
        _window = new double[fftSize];
        _ring = new float[fftSize];
        _monoRing = new float[fftSize];

        for (int i = 0; i < fftSize; i++)
            _window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));

        Configure(sampleRate, fftSize);
    }

    public void Configure(int sampleRate, int fftSize)
    {
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _loudness.Configure(sampleRate);
        _sideHighPass = Biquad.Create(BiquadKind.HighPass, sampleRate, 4000.0, 0.7071);
        _midHighPass = Biquad.Create(BiquadKind.HighPass, sampleRate, 4000.0, 0.7071);
        _sideLowPass = Biquad.Create(BiquadKind.LowPass, sampleRate, 8000.0, 0.7071);
        _fftHop = Math.Max(256, fftSize / 4);
    }

    public AnalysisSnapshot Analyze(float[] buffer, int channels, int frames, int sampleRate)
    {
        if (sampleRate != _sampleRate)
            Configure(sampleRate, _fftSize);

        var snapshot = _snapshot;
        snapshot.SampleRate = sampleRate;
        double sideEnergy = 0.0;
        double midEnergy = 0.0;

        _loudness.Process(buffer, channels, frames);

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            double left = buffer[baseIndex];
            double right = channels > 1 ? buffer[baseIndex + 1] : left;
            double mono = 0.5 * (left + right);

            double mid = 0.5 * (left + right);
            double side = 0.5 * (left - right);
            double sideHigh = _sideHighPass!.ProcessChannel(side, 0);
            double midHigh = _midHighPass!.ProcessChannel(mid, 0);
            sideEnergy += sideHigh * sideHigh;
            midEnergy += midHigh * midHigh;

            _ring[_ringIndex] = (float)mono;
            _ringIndex = (_ringIndex + 1) % _fftSize;
            if (_filled < _fftSize)
                _filled++;

            _envelopeAccumulator += mono * mono;
            _envelopeSamples++;
            if (_envelopeSamples >= 480) // 10 ms envelope resolution at 48 kHz
            {
                double rms = Math.Sqrt(_envelopeAccumulator / _envelopeSamples);
                _envelopeAccumulator = 0.0;
                _envelopeSamples = 0;
                _envelopeHistory.Enqueue(AudioMath.LinearToDb(rms));
                while (_envelopeHistory.Count > 400)
                    _envelopeHistory.Dequeue();
            }

            double magnitude = Math.Abs(mono);
            if (magnitude > _peak)
                _peak = magnitude;
            _sumSquares += mono * mono;
            _sampleCounter++;
        }

        snapshot.MomentaryLufs = _loudness.MomentaryLufs;
        snapshot.ShortTermLufs = _loudness.ShortTermLufs;
        snapshot.IntegratedLufs = _loudness.IntegratedLufs;
        snapshot.HighFrequencySideRatio = midEnergy > 1e-12 ? sideEnergy / midEnergy : 0.0;

        if (_sampleCounter > 0)
        {
            double rms = Math.Sqrt(_sumSquares / _sampleCounter);
            snapshot.CrestFactorDb = AudioMath.LinearToDb(_peak) - AudioMath.LinearToDb(rms);
        }

        ComputePumping(snapshot);

        _hopCounter += frames;
        if (_hopCounter >= _fftHop)
        {
            _hopCounter = 0;
            ComputeSpectrum(snapshot);
        }

        return snapshot;
    }

    private void ComputePumping(AnalysisSnapshot snapshot)
    {
        if (_envelopeHistory.Count < 20)
            return;

        var values = new List<double>(_envelopeHistory);
        double maxDip = 0.0;
        int dips = 0;
        int window = Math.Min(values.Count, 100); // one second of envelope history

        for (int i = 0; i + window < values.Count; i++)
        {
            double peak = double.MinValue;
            for (int j = i; j < i + window; j++)
                peak = Math.Max(peak, values[j]);

            double dip = 0.0;
            for (int j = i; j < i + window; j++)
                dip = Math.Max(dip, peak - values[j]);

            if (dip > maxDip)
                maxDip = dip;
        }

        for (int i = 1; i < values.Count - 1; i++)
        {
            if (values[i] > values[i - 1] && values[i] > values[i + 1] &&
                values[i] - Math.Min(values[i - 1], values[i + 1]) > 4.0)
            {
                dips++;
            }
        }

        snapshot.PumpingDepthDb = maxDip;
        double seconds = values.Count / 100.0;
        snapshot.PumpingRateHz = seconds > 0 ? dips / seconds : 0.0;
    }

    private void ComputeSpectrum(AnalysisSnapshot snapshot)
    {
        if (_filled < _fftSize)
            return;

        int start = _ringIndex; // oldest sample
        for (int i = 0; i < _fftSize; i++)
        {
            double sample = _ring[(start + i) % _fftSize];
            _real[i] = sample * _window[i];
            _imaginary[i] = 0.0;
        }

        _fft.Transform(_real, _imaginary);

        int bins = _fftSize / 2;
        if (_spectrumBuffer.Length != bins)
            _spectrumBuffer = new float[bins];
        var spectrum = _spectrumBuffer;
        Array.Clear(spectrum, 0, spectrum.Length);
        double total = 0.0;
        double weighted = 0.0;
        double logSum = 0.0;
        double linearSum = 0.0;
        int flatCount = 0;

        for (int i = 1; i < bins; i++)
        {
            double magnitude = Math.Sqrt(_real[i] * _real[i] + _imaginary[i] * _imaginary[i]) / (_fftSize * 0.25);
            double frequency = i * (double)_sampleRate / _fftSize;
            double db = AudioMath.LinearToDb(magnitude);

            spectrum[i] = (float)db;
            total += magnitude;
            weighted += magnitude * frequency;

            if (frequency >= 250.0 && frequency <= 8000.0)
            {
                double power = magnitude * magnitude + 1e-20;
                logSum += Math.Log(power);
                linearSum += power;
                flatCount++;
            }
        }

        snapshot.SpectrumDb = spectrum;

        // The centroid is measured on the 250 Hz - 8 kHz band only: that is where the
        // background filter sweeps of the Dark Noise mode live. Restricting the band keeps
        // a kick pattern (pure level modulation in the sub range) from looking unstable.
        double bandMax = 0.0;
        for (int i = 1; i < bins; i++)
        {
            double frequency = i * (double)_sampleRate / _fftSize;
            if (frequency < 250.0 || frequency > 8000.0)
                continue;

            double magnitude = Math.Sqrt(_real[i] * _real[i] + _imaginary[i] * _imaginary[i]) / (_fftSize * 0.25);
            bandMax = Math.Max(bandMax, magnitude);
        }

        // Only the dominant part of the band describes the spectral shape. Spectral
        // leakage from a very loud sub bass tone must not drag the centroid around.
        double bandFloor = bandMax * 0.0316; // -30 dB below the band peak

        double bandTotal = 0.0;
        double bandWeighted = 0.0;

        int bandBins = 0;
        for (int i = 1; i < bins; i++)
        {
            double frequency = i * (double)_sampleRate / _fftSize;
            if (frequency < 250.0 || frequency > 8000.0)
                continue;
            bandBins++;
        }

        if (_bandShape.Length != bandBins)
        {
            _bandShape = new float[bandBins];
            _previousShape = new float[bandBins];
            _havePreviousShape = false;
        }

        int shapeIndex = 0;
        for (int i = 1; i < bins; i++)
        {
            double frequency = i * (double)_sampleRate / _fftSize;
            if (frequency < 250.0 || frequency > 8000.0)
                continue;

            double magnitude = Math.Sqrt(_real[i] * _real[i] + _imaginary[i] * _imaginary[i]) / (_fftSize * 0.25);
            if (magnitude < bandFloor)
                magnitude = 0.0;

            _bandShape[shapeIndex++] = (float)magnitude;
            bandTotal += magnitude;
            bandWeighted += magnitude * frequency;
        }

        // Only frames that actually carry content in the analysis band describe the
        // spectral shape. A frame with an empty band (for example a pure kick) would
        // otherwise look like the centroid jumped to zero.
        _bandEnergyReference = Math.Max(_bandEnergyReference * 0.995, bandTotal);
        bool bandHasContent = bandTotal > 1e-9 && bandTotal > _bandEnergyReference * 0.05;

        snapshot.SpectralCentroidHz = bandHasContent ? bandWeighted / bandTotal : 0.0;
        snapshot.SpectralFlatness = flatCount > 0 && linearSum > 0
            ? Math.Exp(logSum / flatCount) / (linearSum / flatCount)
            : 0.0;

        ComputeBands(snapshot, spectrum);
        ComputeHarshness(snapshot, spectrum);
        ComputeBalance(snapshot, spectrum);
        ComputeInstability(snapshot);
    }

    private void ComputeBands(AnalysisSnapshot snapshot, float[] spectrum)
    {
        var bands = _bandBuffer;
        double minFrequency = 20.0;
        double maxFrequency = 20000.0;

        for (int band = 0; band < BandCount; band++)
        {
            double low = minFrequency * Math.Pow(maxFrequency / minFrequency, band / (double)BandCount);
            double high = minFrequency * Math.Pow(maxFrequency / minFrequency, (band + 1) / (double)BandCount);
            double sum = 0.0;
            int count = 0;

            for (int i = 1; i < spectrum.Length; i++)
            {
                double frequency = i * (double)_sampleRate / _fftSize;
                if (frequency >= low && frequency < high)
                {
                    double linear = Math.Pow(10.0, spectrum[i] / 20.0);
                    sum += linear * linear;
                    count++;
                }
            }

            double rms = count > 0 ? Math.Sqrt(sum / count) : 0.0;
            bands[band] = (float)AudioMath.LinearToDb(rms);
        }

        snapshot.BandDb = bands;
    }

    private void ComputeHarshness(AnalysisSnapshot snapshot, float[] spectrum)
    {
        int lowBin = Math.Max(1, (int)(2500.0 * _fftSize / _sampleRate));
        int highBin = Math.Min(spectrum.Length - 1, (int)(6000.0 * _fftSize / _sampleRate));

        // Smooth the spectrum first: a single bin of broadband material can sit 10 dB above
        // its neighbours by chance, a real narrow band resonance stays far above them.
        if (_smoothedBuffer.Length != spectrum.Length)
            _smoothedBuffer = new float[spectrum.Length];

        for (int i = 1; i < spectrum.Length; i++)
        {
            double sum = 0.0;
            int count = 0;
            for (int j = Math.Max(1, i - 2); j <= Math.Min(spectrum.Length - 1, i + 2); j++)
            {
                sum += spectrum[j];
                count++;
            }

            _smoothedBuffer[i] = count > 0 ? (float)(sum / count) : spectrum[i];
        }

        double best = 0.0;
        int bestBin = lowBin;

        for (int i = lowBin + 3; i < highBin - 3; i++)
        {
            if (_smoothedBuffer[i] < -70f)
                continue;

            var neighbours = new List<float>();
            for (int j = i - 12; j <= i + 12; j++)
            {
                if (j < 1 || j >= spectrum.Length || Math.Abs(j - i) <= 3)
                    continue;

                neighbours.Add(_smoothedBuffer[j]);
            }

            if (neighbours.Count == 0)
                continue;

            // The 75th percentile is a stable reference for broadband material while a
            // real narrow band resonance still stands far above it.
            neighbours.Sort();
            float reference = neighbours[(int)(neighbours.Count * 0.75)];
            double prominence = _smoothedBuffer[i] - reference;
            if (prominence > best)
            {
                best = prominence;
                bestBin = i;
            }
        }

        snapshot.HarshnessDb = best;
        snapshot.HarshFrequencyHz = bestBin * (double)_sampleRate / _fftSize;
    }

    private void ComputeBalance(AnalysisSnapshot snapshot, float[] spectrum)
    {
        double sub = BandPower(spectrum, 40.0, 80.0);
        double mid = BandPower(spectrum, 250.0, 1500.0);
        double lowMid = BandPower(spectrum, 100.0, 250.0);
        double highMid = BandPower(spectrum, 1500.0, 3000.0);
        double total = BandPower(spectrum, 40.0, 8000.0);

        snapshot.SubPressure = total > 1e-12 ? sub / total : 0.0;

        double neighbourAverage = 0.5 * (lowMid + highMid);
        if (neighbourAverage > 1e-12)
        {
            double ratio = mid / neighbourAverage;
            snapshot.MidScoop = Math.Clamp(1.0 - ratio, 0.0, 1.0);
        }
    }

    private double BandPower(float[] spectrum, double lowHz, double highHz)
    {
        int lowBin = Math.Max(1, (int)(lowHz * _fftSize / _sampleRate));
        int highBin = Math.Min(spectrum.Length - 1, (int)(highHz * _fftSize / _sampleRate));
        double sum = 0.0;

        for (int i = lowBin; i <= highBin; i++)
        {
            double linear = Math.Pow(10.0, spectrum[i] / 20.0);
            sum += linear * linear;
        }

        return sum;
    }

    private void ComputeInstability(AnalysisSnapshot snapshot)
    {
        // Shape stability: how much the 24-band spectral distribution in the 200 Hz - 8 kHz
        // analysis range moves from one frame to the next. Using log-spaced bands instead of
        // raw FFT bins prevents normal pitch/vibrato changes from looking like filter sweeps.
        const int firstBand = 8;
        const int lastBand = 20;
        const int bandSpan = lastBand - firstBand + 1;

        if (_bandShape.Length != bandSpan)
        {
            _bandShape = new float[bandSpan];
            _previousShape = new float[bandSpan];
            _havePreviousShape = false;
        }

        double sum = 0.0;
        for (int b = firstBand; b <= lastBand; b++)
        {
            double mag = Math.Pow(10.0, _bandBuffer[b] / 20.0);
            _bandShape[b - firstBand] = (float)mag;
            sum += mag;
        }

        if (sum <= 1e-12)
        {
            _havePreviousShape = false;
            return;
        }

        for (int i = 0; i < bandSpan; i++)
            _bandShape[i] = (float)(_bandShape[i] / sum);

        double similarity = 1.0;
        if (_havePreviousShape)
        {
            double dot = 0.0;
            double normA = 0.0;
            double normB = 0.0;

            for (int i = 0; i < bandSpan; i++)
            {
                dot += _bandShape[i] * _previousShape[i];
                normA += _bandShape[i] * _bandShape[i];
                normB += _previousShape[i] * _previousShape[i];
            }

            if (normA > 1e-12 && normB > 1e-12)
                similarity = Math.Clamp(dot / Math.Sqrt(normA * normB), 0.0, 1.0);
        }

        Array.Copy(_bandShape, _previousShape, bandSpan);
        _havePreviousShape = true;

        _similarityHistory.Enqueue(similarity);
        while (_similarityHistory.Count > 48)
            _similarityHistory.Dequeue();

        if (_similarityHistory.Count < 8)
            return;

        double mean = 0.0;
        foreach (double value in _similarityHistory)
            mean += value;
        mean /= _similarityHistory.Count;

        snapshot.DarkInstability = Math.Clamp((1.0 - mean) * 35.0, 0.0, 1.0);
    }

    public void Reset()
    {
        Array.Clear(_ring, 0, _ring.Length);
        _ringIndex = 0;
        _filled = 0;
        _centroidHistory.Clear();
        _envelopeHistory.Clear();
        _peak = 0.0;
        _sumSquares = 0.0;
        _sampleCounter = 0;
        _loudness.Reset();
    }
}

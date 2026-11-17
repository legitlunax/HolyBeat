using System.Collections.Generic;

namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// ITU-R BS.1770-4 loudness meter (K-weighting + 400 ms / 3 s windows + gated integrated
/// loudness). It drives the -13 LUFS to -11 LUFS target from the HolyBeat mastering stage.
/// </summary>
public sealed class LoudnessMeter
{
    private const double Offset = -0.691;

    private Biquad? _shelfLeft;
    private Biquad? _shelfRight;
    private Biquad? _rlbLeft;
    private Biquad? _rlbRight;

    private readonly List<double> _blockLoudness = new List<double>();
    private double _blockSum;
    private int _blockCount;
    private int _blockHop = 4800;

    public double MomentaryLufs { get; private set; } = -70.0;
    public double ShortTermLufs { get; private set; } = -70.0;
    public double IntegratedLufs { get; private set; } = -70.0;
    public double LoudnessRange { get; private set; } = 0.0;

    public void Configure(double sampleRate)
    {
        // Parametric K-weighting definition from BS.1770-4.
        _shelfLeft = Biquad.Create(BiquadKind.HighShelf, sampleRate, 1681.9744506, 0.7071752, 3.9998438537);
        _shelfRight = Biquad.Create(BiquadKind.HighShelf, sampleRate, 1681.9744506, 0.7071752, 3.9998438537);
        _rlbLeft = Biquad.Create(BiquadKind.HighPass, sampleRate, 38.135470876, 0.5003270373);
        _rlbRight = Biquad.Create(BiquadKind.HighPass, sampleRate, 38.135470876, 0.5003270373);

        _blockHop = Math.Max(1, (int)(sampleRate * 0.1));
        _blockSum = 0.0;
        _blockCount = 0;
        _blockLoudness.Clear();
    }

    public void Process(float[] buffer, int channels, int frames)
    {
        if (_shelfLeft is null || _shelfRight is null || _rlbLeft is null || _rlbRight is null)
            return;

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            double left = buffer[baseIndex];
            double right = channels > 1 ? buffer[baseIndex + 1] : left;

            left = _shelfLeft.ProcessChannel(left, 0);
            right = _shelfRight.ProcessChannel(right, 0);
            left = _rlbLeft.ProcessChannel(left, 0);
            right = _rlbRight.ProcessChannel(right, 0);

            _blockSum += left * left + right * right;
            _blockCount++;

            if (_blockCount >= _blockHop)
            {
                double meanSquare = _blockSum / _blockCount;
                double lufs = Offset + 10.0 * System.Math.Log10(System.Math.Max(meanSquare, 1e-12));
                _blockLoudness.Add(lufs);
                _blockSum = 0.0;
                _blockCount = 0;
                UpdateWindows();
            }
        }
    }

    private void UpdateWindows()
    {
        int count = _blockLoudness.Count;
        if (count == 0)
            return;

        double momentarySum = 0.0;
        int momentaryCount = System.Math.Min(count, 4);
        for (int i = count - momentaryCount; i < count; i++)
            momentarySum += _blockLoudness[i];
        MomentaryLufs = momentarySum / momentaryCount;

        double shortTermSum = 0.0;
        int shortTermCount = System.Math.Min(count, 30);
        for (int i = count - shortTermCount; i < count; i++)
            shortTermSum += _blockLoudness[i];
        ShortTermLufs = shortTermSum / shortTermCount;

        // Gated integrated loudness: absolute gate at -70 LUFS, relative gate -10 LU below the ungated mean.
        double ungated = 0.0;
        int ungatedCount = 0;
        foreach (double value in _blockLoudness)
        {
            if (value > -70.0)
            {
                ungated += value;
                ungatedCount++;
            }
        }

        if (ungatedCount == 0)
            return;

        double ungatedMean = ungated / ungatedCount;
        double threshold = ungatedMean - 10.0;

        double gatedSum = 0.0;
        int gatedCount = 0;
        foreach (double value in _blockLoudness)
        {
            if (value > -70.0 && value > threshold)
            {
                gatedSum += value;
                gatedCount++;
            }
        }

        if (gatedCount > 0)
            IntegratedLufs = gatedSum / gatedCount;

        LoudnessRange = Math.Max(0.0, ungatedMean - IntegratedLufs);
    }

    public void Reset()
    {
        _blockLoudness.Clear();
        _blockSum = 0.0;
        _blockCount = 0;
        MomentaryLufs = -70.0;
        ShortTermLufs = -70.0;
        IntegratedLufs = -70.0;
        _shelfLeft?.Reset();
        _shelfRight?.Reset();
        _rlbLeft?.Reset();
        _rlbRight?.Reset();
    }
}

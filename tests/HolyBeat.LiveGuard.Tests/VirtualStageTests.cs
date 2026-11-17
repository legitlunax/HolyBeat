using System;
using HolyBeat.LiveGuard.Core.Dsp;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

public class VirtualStageTests
{
    private const int SampleRate = 48000;

    private static float[] StereoSignal()
    {
        var buffer = new float[SampleRate * 2 * 2];
        for (int n = 0; n < SampleRate * 2; n++)
        {
            double t = n / (double)SampleRate;
            buffer[n * 2] = (float)(0.4 * Math.Sin(2.0 * Math.PI * 220.0 * t) + 0.2 * Math.Sin(2.0 * Math.PI * 4000.0 * t));
            buffer[n * 2 + 1] = (float)(0.3 * Math.Sin(2.0 * Math.PI * 330.0 * t) + 0.1 * Math.Sin(2.0 * Math.PI * 900.0 * t));
        }

        return buffer;
    }

    private static VirtualStage Stage(double width, double height, double centre, double lfe, double surround)
    {
        var stage = new VirtualStage();
        stage.Configure(SampleRate, width, height, centre, lfe, surround);
        return stage;
    }

    [Fact]
    public void NeutralSettings_AreCompletelyTransparent()
    {
        float[] signal = StereoSignal();
        float[] copy = new float[signal.Length];
        Array.Copy(signal, copy, signal.Length);

        var stage = Stage(1.0, 0.0, 0.0, 0.0, 0.0);
        stage.Process(copy, 2, copy.Length / 2);

        double worst = 0.0;
        for (int i = 0; i < signal.Length; i++)
            worst = Math.Max(worst, Math.Abs(signal[i] - copy[i]));

        Assert.True(worst < 1e-4, $"The stage must be transparent when neutral, worst difference was {worst:E3}");
    }

    [Fact]
    public void MonoMaterialStaysCentred()
    {
        var buffer = new float[SampleRate * 2 * 2];
        for (int n = 0; n < SampleRate * 2; n++)
        {
            double value = 0.4 * Math.Sin(2.0 * Math.PI * 440.0 * n / SampleRate);
            buffer[n * 2] = (float)value;
            buffer[n * 2 + 1] = (float)value;
        }

        var stage = Stage(1.0, 0.6, 0.8, 0.5, 0.4);
        stage.Process(buffer, 2, buffer.Length / 2);

        for (int i = 0; i < buffer.Length; i += 2)
            Assert.True(Math.Abs(buffer[i] - buffer[i + 1]) < 1e-4, "Mono material must stay centred");
    }

    [Fact]
    public void HardLeftMaterialStaysOnTheLeft()
    {
        var buffer = new float[SampleRate * 2 * 2];
        for (int n = 0; n < SampleRate * 2; n++)
        {
            double value = 0.4 * Math.Sin(2.0 * Math.PI * 440.0 * n / SampleRate);
            buffer[n * 2] = (float)value;
            buffer[n * 2 + 1] = 0f;
        }

        var stage = Stage(1.0, 0.4, 0.5, 0.3, 0.3);
        stage.Process(buffer, 2, buffer.Length / 2);

        double left = SignalHelper.Rms(buffer, 0, buffer.Length);
        double right = 0.0;
        double sumRight = 0.0;
        int count = 0;
        for (int i = 1; i < buffer.Length; i += 2)
        {
            sumRight += buffer[i] * buffer[i];
            count++;
        }
        right = Math.Sqrt(sumRight / count);

        Assert.True(left > right * 1.5, $"The left ear must dominate, left {left:F4} right {right:F4}");
    }

    [Fact]
    public void HeightLayerIsFedByThePresenceBand()
    {
        var presence = new float[SampleRate * 2 * 2];
        var low = new float[SampleRate * 2 * 2];

        for (int n = 0; n < SampleRate * 2; n++)
        {
            double t = n / (double)SampleRate;
            double high = 0.4 * Math.Sin(2.0 * Math.PI * 4000.0 * t);
            double lowValue = 0.4 * Math.Sin(2.0 * Math.PI * 200.0 * t);
            presence[n * 2] = (float)high;
            presence[n * 2 + 1] = (float)high;
            low[n * 2] = (float)lowValue;
            low[n * 2 + 1] = (float)lowValue;
        }

        var stagePresence = Stage(1.0, 1.0, 0.0, 0.0, 0.0);
        stagePresence.Process(presence, 2, presence.Length / 2);

        var stageLow = Stage(1.0, 1.0, 0.0, 0.0, 0.0);
        stageLow.Process(low, 2, low.Length / 2);

        double heightFromPresence = stagePresence.BusLevels[(int)VirtualStage.Bus.HeightLeft];
        double heightFromLow = stageLow.BusLevels[(int)VirtualStage.Bus.HeightLeft];

        Assert.True(heightFromPresence > heightFromLow + 20,
            $"The presence band must feed the height bus, {heightFromPresence:F1} dB vs {heightFromLow:F1} dB");
    }

    [Fact]
    public void RumbleBusIsFedByTheSubBand()
    {
        var sub = new float[SampleRate * 2 * 2];
        var mid = new float[SampleRate * 2 * 2];

        for (int n = 0; n < SampleRate * 2; n++)
        {
            double t = n / (double)SampleRate;
            double lowValue = 0.4 * Math.Sin(2.0 * Math.PI * 60.0 * t);
            double midValue = 0.4 * Math.Sin(2.0 * Math.PI * 600.0 * t);
            sub[n * 2] = (float)lowValue;
            sub[n * 2 + 1] = (float)lowValue;
            mid[n * 2] = (float)midValue;
            mid[n * 2 + 1] = (float)midValue;
        }

        var stageSub = Stage(1.0, 0.0, 0.0, 1.0, 0.0);
        stageSub.Process(sub, 2, sub.Length / 2);

        var stageMid = Stage(1.0, 0.0, 0.0, 1.0, 0.0);
        stageMid.Process(mid, 2, mid.Length / 2);

        double rumbleFromSub = stageSub.BusLevels[(int)VirtualStage.Bus.Lfe];
        double rumbleFromMid = stageMid.BusLevels[(int)VirtualStage.Bus.Lfe];

        Assert.True(rumbleFromSub > rumbleFromMid + 20,
            $"The sub band must feed the rumble bus, {rumbleFromSub:F1} dB vs {rumbleFromMid:F1} dB");
    }

    [Fact]
    public void CentreFocusMovesTheGlueBandIntoTheCentreBus()
    {
        // A mono tone inside the glue band, the way a lead vocal sits in the mix.
        float[] signal = new float[SampleRate * 2 * 2];
        for (int n = 0; n < SampleRate * 2; n++)
        {
            double value = 0.4 * Math.Sin(2.0 * Math.PI * 600.0 * n / SampleRate);
            signal[n * 2] = (float)value;
            signal[n * 2 + 1] = (float)value;
        }

        float[] open = new float[signal.Length];
        float[] centred = new float[signal.Length];
        Array.Copy(signal, open, signal.Length);
        Array.Copy(signal, centred, signal.Length);

        var stageOpen = Stage(1.0, 0.0, 0.0, 0.0, 0.0);
        stageOpen.Process(open, 2, open.Length / 2);

        var stageCentred = Stage(1.0, 0.0, 1.0, 0.0, 0.0);
        stageCentred.Process(centred, 2, centred.Length / 2);

        double centreOpen = stageOpen.BusLevels[(int)VirtualStage.Bus.Centre];
        double centreFocused = stageCentred.BusLevels[(int)VirtualStage.Bus.Centre];

        Assert.True(centreFocused > centreOpen + 20,
            $"The glue band must move into the centre bus, {centreOpen:F1} dB vs {centreFocused:F1} dB");
    }

    [Fact]
    public void StageNeverProducesInvalidSamples()
    {
        float[] signal = StereoSignal();
        var stage = Stage(1.4, 1.0, 1.0, 1.0, 1.0);
        stage.Process(signal, 2, signal.Length / 2);

        foreach (float sample in signal)
            Assert.True(float.IsFinite(sample), "The stage must never emit NaN or infinity");
    }
}

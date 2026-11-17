namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// Stage 4b - the virtual headphone stage.
///
/// The three bands that the research singles out are reused as spatial buses:
/// the 120 - 1500 Hz glue band can be anchored to the centre (so the vocal stays
/// with the mix), the presence band around 2.8 - 5.5 kHz becomes the height layer
/// (the same band that causes headaches, but already de-harshed before it is lifted)
/// and the 40 - 120 Hz pressure band becomes the rumble bus.
///
/// Every bus is a crossfade, not an addition: the content that moves into a bus is
/// taken out of the front image, so with all the spatial controls at their neutral
/// position the stage is completely transparent.
/// </summary>
public sealed class VirtualStage
{
    /// <summary>One rendered bus, exposed so the interface can visualise the stage.</summary>
    public enum Bus
    {
        FrontLeft,
        FrontRight,
        Centre,
        HeightLeft,
        HeightRight,
        SurroundLeft,
        SurroundRight,
        Lfe
    }

    private sealed class DelayLine
    {
        private float[] _buffer = Array.Empty<float>();
        private int _index;
        private float _fraction;

        public void Configure(int sampleRate, double delayMs)
        {
            int capacity = 1024;
            while (capacity < sampleRate * delayMs / 1000.0 + 8)
                capacity <<= 1;

            _buffer = new float[capacity];
            _index = 0;
            _fraction = (float)(sampleRate * delayMs / 1000.0 - (int)(sampleRate * delayMs / 1000.0));
        }

        public float Process(float sample)
        {
            _buffer[_index] = sample;
            int readIndex = _index - 1;
            if (readIndex < 0)
                readIndex += _buffer.Length;

            float delayed = _buffer[readIndex];
            float next = _buffer[(readIndex + 1) % _buffer.Length];
            float result = delayed + (next - delayed) * _fraction;

            _index = (_index + 1) % _buffer.Length;
            return result;
        }

        public void Reset() => Array.Clear(_buffer, 0, _buffer.Length);
    }

    private readonly DelayLine _heightLeftToRight = new DelayLine();
    private readonly DelayLine _heightRightToLeft = new DelayLine();
    private readonly DelayLine _surroundLeftToRight = new DelayLine();
    private readonly DelayLine _surroundRightToLeft = new DelayLine();

    private Biquad[] _glueBand = Array.Empty<Biquad>();
    private Biquad[] _heightBand = Array.Empty<Biquad>();
    private Biquad[] _lowBand = Array.Empty<Biquad>();
    private Biquad[] _surroundBand = Array.Empty<Biquad>();
    private Biquad? _surroundToneLeft;
    private Biquad? _surroundToneRight;

    public bool Enabled { get; set; } = true;

    /// <summary>0 = mono stage (everything centred), 1 = original stereo width.</summary>
    public double Width { get; set; } = 1.0;

    /// <summary>How much of the presence band is lifted into the height layer.</summary>
    public double Height { get; set; } = 0.35;

    /// <summary>How much of the glue band is moved out of the stereo image into the centre.</summary>
    public double CentreFocus { get; set; } = 0.5;

    /// <summary>How much of the sub band is routed to the rumble bus.</summary>
    public double LfeMix { get; set; }

    /// <summary>How much of the side energy is routed to the surround layer.</summary>
    public double SurroundMix { get; set; }

    /// <summary>Smoothed level per bus, for the stage visualisation.</summary>
    public double[] BusLevels { get; } = new double[8];

    public void Configure(double sampleRate, double width, double height, double centreFocus,
        double lfeMix, double surroundMix)
    {
        Width = width;
        Height = height;
        CentreFocus = centreFocus;
        LfeMix = lfeMix;
        SurroundMix = surroundMix;

        int rate = (int)Math.Max(8000, sampleRate);

        // The glue band is the 120 - 1500 Hz range the research calls the mid range glue.
        _glueBand = new[]
        {
            Biquad.Create(BiquadKind.HighPass, rate, 120.0, 0.7071),
            Biquad.Create(BiquadKind.HighPass, rate, 120.0, 0.7071),
            Biquad.Create(BiquadKind.LowPass, rate, 1500.0, 0.7071),
            Biquad.Create(BiquadKind.LowPass, rate, 1500.0, 0.7071)
        };

        // The height layer is fed by the presence band that also carries the ear pain.
        _heightBand = new[]
        {
            Biquad.Create(BiquadKind.HighPass, rate, 1800.0, 0.7071),
            Biquad.Create(BiquadKind.HighPass, rate, 1800.0, 0.7071),
            Biquad.Create(BiquadKind.LowPass, rate, 7000.0, 0.7071),
            Biquad.Create(BiquadKind.LowPass, rate, 7000.0, 0.7071)
        };

        // The rumble bus is the 40 - 120 Hz pressure band.
        _lowBand = new[]
        {
            Biquad.Create(BiquadKind.HighPass, rate, 40.0, 0.7071),
            Biquad.Create(BiquadKind.HighPass, rate, 40.0, 0.7071),
            Biquad.Create(BiquadKind.LowPass, rate, 120.0, 0.7071),
            Biquad.Create(BiquadKind.LowPass, rate, 120.0, 0.7071)
        };

        // The surround layer takes the side energy above 800 Hz.
        _surroundBand = new[]
        {
            Biquad.Create(BiquadKind.HighPass, rate, 800.0, 0.7071)
        };

        // Surrounds sit behind the listener, so they are darker as well as wider.
        _surroundToneLeft = Biquad.Create(BiquadKind.LowPass, rate, 5200.0, 0.7071);
        _surroundToneRight = Biquad.Create(BiquadKind.LowPass, rate, 5200.0, 0.7071);

        _heightLeftToRight.Configure(rate, 0.22);
        _heightRightToLeft.Configure(rate, 0.22);
        _surroundLeftToRight.Configure(rate, 0.55);
        _surroundRightToLeft.Configure(rate, 0.55);
    }

    public void Process(float[] buffer, int channels, int frames)
    {
        if (!Enabled || channels < 2 || frames <= 0)
            return;

        double width = Math.Clamp(Width, 0.0, 1.5);
        double height = Math.Clamp(Height, 0.0, 1.0);
        double centreFocus = Math.Clamp(CentreFocus, 0.0, 1.0);
        double lfeMix = Math.Clamp(LfeMix, 0.0, 1.0);
        double surroundMix = Math.Clamp(SurroundMix, 0.0, 1.0);

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            double left = buffer[baseIndex];
            double right = buffer[baseIndex + 1];

            double mid = 0.5 * (left + right);
            double side = 0.5 * (left - right);

            // The content that moves into a spatial bus leaves the front image.
            double centreContent = Filter(_glueBand, mid) * centreFocus;
            double heightContent = Filter(_heightBand, mid) * height;
            double lfeContent = Filter(_lowBand, mid) * lfeMix;
            double surroundContent = Filter(_surroundBand, side) * surroundMix;

            double frontMid = mid - centreContent - heightContent - lfeContent;
            double frontSide = side - surroundContent;

            double frontLeft = frontMid + frontSide * width;
            double frontRight = frontMid - frontSide * width;

            double surroundLeft = surroundContent * width;
            double surroundRight = -surroundContent * width;

            surroundLeft = _surroundToneLeft!.ProcessChannel(surroundLeft, 0);
            surroundRight = _surroundToneRight!.ProcessChannel(surroundRight, 0);

            double outLeft = frontLeft + centreContent + lfeContent + heightContent * 0.9 + surroundLeft * 0.8;
            double outRight = frontRight + centreContent + lfeContent + heightContent * 0.9 + surroundRight * 0.8;

            // Inter-aural delays place the height and surround layers behind and above.
            outRight += _heightLeftToRight.Process((float)(heightContent * 0.15));
            outLeft += _heightRightToLeft.Process((float)(heightContent * 0.15));
            outRight += _surroundLeftToRight.Process((float)(surroundLeft * 0.8));
            outLeft += _surroundRightToLeft.Process((float)(surroundRight * 0.8));

            buffer[baseIndex] = (float)outLeft;
            buffer[baseIndex + 1] = (float)outRight;

            double[] levels = BusLevels;
            levels[(int)Bus.FrontLeft] = Smooth(levels[(int)Bus.FrontLeft], frontLeft);
            levels[(int)Bus.FrontRight] = Smooth(levels[(int)Bus.FrontRight], frontRight);
            levels[(int)Bus.Centre] = Smooth(levels[(int)Bus.Centre], centreContent);
            levels[(int)Bus.HeightLeft] = Smooth(levels[(int)Bus.HeightLeft], heightContent);
            levels[(int)Bus.HeightRight] = Smooth(levels[(int)Bus.HeightRight], heightContent);
            levels[(int)Bus.SurroundLeft] = Smooth(levels[(int)Bus.SurroundLeft], surroundLeft);
            levels[(int)Bus.SurroundRight] = Smooth(levels[(int)Bus.SurroundRight], surroundRight);
            levels[(int)Bus.Lfe] = Smooth(levels[(int)Bus.Lfe], lfeContent);
        }
    }

    private static double Filter(Biquad[] sections, double input)
    {
        double value = input;
        foreach (var section in sections)
            value = section.ProcessChannel(value, 0);
        return value;
    }

    private static double Smooth(double current, double value)
    {
        double target = AudioMath.LinearToDb(Math.Abs(value));
        if (target < -80.0)
            target = -80.0;
        return current + 0.25 * (target - current);
    }

    public void Reset()
    {
        foreach (var section in _glueBand) section.Reset();
        foreach (var section in _heightBand) section.Reset();
        foreach (var section in _lowBand) section.Reset();
        foreach (var section in _surroundBand) section.Reset();
        _surroundToneLeft?.Reset();
        _surroundToneRight?.Reset();
        _heightLeftToRight.Reset();
        _heightRightToLeft.Reset();
        _surroundLeftToRight.Reset();
        _surroundRightToLeft.Reset();
        Array.Clear(BusLevels, 0, BusLevels.Length);
    }
}

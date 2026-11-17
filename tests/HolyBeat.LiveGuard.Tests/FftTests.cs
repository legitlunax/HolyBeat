using System;
using HolyBeat.LiveGuard.Core.Analysis;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

public class FftTests
{
    [Fact]
    public void Transform_PlacesABinCentredToneInTheCorrectBin()
    {
        var fft = new Fft(2048);
        var real = new double[2048];
        var imaginary = new double[2048];

        for (int i = 0; i < 2048; i++)
            real[i] = Math.Sin(2.0 * Math.PI * 170.0 * i / 2048.0);

        fft.Transform(real, imaginary);

        int peak = 0;
        double max = 0;
        for (int i = 1; i < 1024; i++)
        {
            double magnitude = Math.Sqrt(real[i] * real[i] + imaginary[i] * imaginary[i]);
            if (magnitude > max)
            {
                max = magnitude;
                peak = i;
            }
        }

        Assert.Equal(170, peak);
        Assert.True(max > 900, $"Expected a strong peak, magnitude was {max:F1}");
    }

    [Fact]
    public void Transform_ImpulseProducesAFlatSpectrum()
    {
        var fft = new Fft(2048);
        var real = new double[2048];
        var imaginary = new double[2048];
        real[0] = 1.0;

        fft.Transform(real, imaginary);

        for (int i = 0; i < 2048; i++)
        {
            double magnitude = Math.Sqrt(real[i] * real[i] + imaginary[i] * imaginary[i]);
            Assert.True(Math.Abs(magnitude - 1.0) < 1e-9, $"Bin {i} should be 1.0, was {magnitude:F6}");
        }
    }

    [Fact]
    public void Transform_HasConjugateSymmetryForRealInput()
    {
        var fft = new Fft(512);
        var real = new double[512];
        var imaginary = new double[512];
        var random = new Random(7);

        for (int i = 0; i < 512; i++)
            real[i] = (float)(random.NextDouble() * 2.0 - 1.0);

        fft.Transform(real, imaginary);

        for (int k = 1; k < 256; k++)
        {
            Assert.True(Math.Abs(real[k] - real[512 - k]) < 1e-9, $"Real parts differ at bin {k}");
            Assert.True(Math.Abs(imaginary[k] + imaginary[512 - k]) < 1e-9, $"Imaginary parts differ at bin {k}");
        }
    }
}

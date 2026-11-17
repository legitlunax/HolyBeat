using System;

namespace HolyBeat.LiveGuard.Core.Analysis;

/// <summary>Iterative radix-2 Cooley-Tukey FFT.</summary>
public sealed class Fft
{
    private readonly int _size;
    private readonly double[] _cosTable;
    private readonly double[] _sinTable;
    private readonly int[] _reverse;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentException("FFT size must be a power of two.", nameof(size));

        _size = size;
        _cosTable = new double[size / 2];
        _sinTable = new double[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            _cosTable[i] = Math.Cos(2.0 * Math.PI * i / size);
            _sinTable[i] = Math.Sin(2.0 * Math.PI * i / size);
        }

        _reverse = new int[size];

        // Exact bit count: Math.Log(size, 2) can truncate 11 to 10 and corrupt the permutation.
        int bits = 0;
        while ((1 << bits) < size)
            bits++;
        for (int i = 0; i < size; i++)
        {
            int value = 0;
            for (int bit = 0; bit < bits; bit++)
            {
                value = (value << 1) | ((i >> bit) & 1);
            }
            _reverse[i] = value;
        }
    }

    public int Size => _size;

    public void Transform(double[] real, double[] imaginary)
    {
        for (int i = 0; i < _size; i++)
        {
            int j = _reverse[i];
            if (j > i)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (int span = 2; span <= _size; span <<= 1)
        {
            int half = span / 2;
            int step = _size / span;

            for (int start = 0; start < _size; start += span)
            {
                int tableIndex = 0;
                for (int k = 0; k < half; k++)
                {
                    double cos = _cosTable[tableIndex];
                    double sin = -_sinTable[tableIndex];
                    tableIndex += step;

                    int even = start + k;
                    int odd = even + half;

                    double realEven = real[even];
                    double imaginaryEven = imaginary[even];
                    double realOdd = real[odd];
                    double imaginaryOdd = imaginary[odd];

                    // The first element of the pair keeps the sum, the second the difference.
                    real[even] = realEven + (realOdd * cos - imaginaryOdd * sin);
                    imaginary[even] = imaginaryEven + (realOdd * sin + imaginaryOdd * cos);
                    real[odd] = realEven - (realOdd * cos - imaginaryOdd * sin);
                    imaginary[odd] = imaginaryEven - (realOdd * sin + imaginaryOdd * cos);
                }
            }
        }
    }
}

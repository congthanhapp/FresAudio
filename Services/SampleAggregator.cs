using System;
using NAudio.Wave;
using NAudio.Dsp;

namespace FresAudio.Services
{
    public class SampleAggregator : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly int fftLength;
        private readonly Complex[] fftBuffer;
        private readonly FftEventArgs fftArgs;
        private int fftPos;
        private int m;

        public event EventHandler<FftEventArgs> FftCalculated;

        public SampleAggregator(ISampleProvider source, int fftLength = 1024)
        {
            this.source = source;
            this.fftLength = fftLength;
            this.fftBuffer = new Complex[fftLength];
            this.fftArgs = new FftEventArgs(fftBuffer);
            this.m = (int)Math.Log(fftLength, 2.0);
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);
            
            for (int n = 0; n < samplesRead; n++)
            {
                Add(buffer[n + offset]);
            }
            return samplesRead;
        }

        private void Add(float value)
        {
            fftBuffer[fftPos].X = (float)(value * FastFourierTransform.HammingWindow(fftPos, fftLength));
            fftBuffer[fftPos].Y = 0;
            fftPos++;
            if (fftPos >= fftLength)
            {
                fftPos = 0;
                FastFourierTransform.FFT(true, m, fftBuffer);
                FftCalculated?.Invoke(this, fftArgs);
            }
        }
    }

    public class FftEventArgs : EventArgs
    {
        public FftEventArgs(Complex[] result) { Result = result; }
        public Complex[] Result { get; private set; }
    }
}

using System;
using NAudio.Wave;
using SoundTouch;

namespace FresAudio.Services
{
    public class SoundTouchSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider sourceProvider;
        private readonly SoundTouchProcessor soundTouch;
        private readonly float[] sourceBuffer;
        private float tempo = 1.0f;
        private readonly object lockObj = new object();

        public SoundTouchSampleProvider(ISampleProvider sourceProvider)
        {
            this.sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
            
            soundTouch = new SoundTouchProcessor
            {
                SampleRate = sourceProvider.WaveFormat.SampleRate,
                Channels = sourceProvider.WaveFormat.Channels,
                Tempo = 1.0f,
                Pitch = 1.0f,
                Rate = 1.0f
            };

            int initialBufferSize = 8192 * sourceProvider.WaveFormat.Channels;
            sourceBuffer = new float[initialBufferSize];
        }

        public WaveFormat WaveFormat => sourceProvider.WaveFormat;

        public float PlaybackSpeed
        {
            get => tempo;
            set
            {
                lock (lockObj)
                {
                    tempo = Math.Clamp(value, 0.25f, 3.0f);
                    soundTouch.Tempo = tempo;
                }
            }
        }

        public void Clear()
        {
            lock (lockObj)
            {
                soundTouch.Clear();
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (lockObj)
            {
                int channels = WaveFormat.Channels;
                int samplesNeeded = count / channels;
                int totalSamplesRead = 0;

                // Tối ưu: Nếu đang ở tốc độ chuẩn 1.0x và bộ đệm SoundTouch đang rỗng, cho âm thanh đi thẳng (Direct Pass-through)
                if (Math.Abs(tempo - 1.0f) < 0.001f && soundTouch.AvailableSamples == 0)
                {
                    return sourceProvider.Read(buffer, offset, count);
                }

                while (totalSamplesRead < samplesNeeded)
                {
                    int samplesToGet = samplesNeeded - totalSamplesRead;
                    Span<float> destSpan = buffer.AsSpan(offset + totalSamplesRead * channels, samplesToGet * channels);
                    
                    int samplesReceived = soundTouch.ReceiveSamples(destSpan, samplesToGet);
                    if (samplesReceived > 0)
                    {
                        totalSamplesRead += samplesReceived;
                    }
                    else
                    {
                        int readFromSource = sourceProvider.Read(sourceBuffer, 0, sourceBuffer.Length);
                        if (readFromSource == 0)
                        {
                            soundTouch.Flush();
                            int remainingToGet = samplesNeeded - totalSamplesRead;
                            Span<float> flushSpan = buffer.AsSpan(offset + totalSamplesRead * channels, remainingToGet * channels);
                            samplesReceived = soundTouch.ReceiveSamples(flushSpan, remainingToGet);
                            if (samplesReceived > 0)
                            {
                                totalSamplesRead += samplesReceived;
                            }
                            break;
                        }

                        soundTouch.PutSamples(sourceBuffer.AsSpan(0, readFromSource), readFromSource / channels);
                    }
                }

                return totalSamplesRead * channels;
            }
        }
    }
}

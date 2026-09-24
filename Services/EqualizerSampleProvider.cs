using System;
using NAudio.Dsp;
using NAudio.Wave;
using FresAudio.Models;

namespace FresAudio.Services
{
    public class EqualizerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly int channels;
        private readonly int sampleRate;
        private readonly object filterLock = new object();

        // 10 Center Frequencies
        public static readonly float[] BandFrequencies = new float[]
        {
            32f, 64f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f
        };

        private readonly BiQuadFilter[,] filters; // [channel, band]
        private readonly BiQuadFilter[] bassFilters; // [channel]
        private readonly BiQuadFilter[] vocalFilters; // [channel]

        private float preampDb = 0f;
        private float preampFactor = 1.0f;
        private readonly float[] bandGains = new float[10];
        private float bassBoost = 0f;
        private float surround3D = 0f;
        private float vocalClarity = 0f;
        private bool isEnabled = true;

        public WaveFormat WaveFormat => source.WaveFormat;

        public bool IsEnabled
        {
            get => isEnabled;
            set => isEnabled = value;
        }

        public EqualizerSampleProvider(ISampleProvider source)
        {
            this.source = source;
            this.channels = source.WaveFormat.Channels;
            this.sampleRate = source.WaveFormat.SampleRate;

            filters = new BiQuadFilter[channels, BandFrequencies.Length];
            bassFilters = new BiQuadFilter[channels];
            vocalFilters = new BiQuadFilter[channels];

            UpdatePreamp(0f);
            RecreateFilters();
        }

        public void ApplySettings(EqualizerSettings settings)
        {
            if (settings == null) return;

            lock (filterLock)
            {
                isEnabled = settings.IsEnabled;
                preampDb = Math.Clamp(settings.PreampDb, -12f, 12f);
                preampFactor = (float)Math.Pow(10, preampDb / 20.0);

                if (settings.BandGainsDb != null)
                {
                    for (int i = 0; i < Math.Min(10, settings.BandGainsDb.Length); i++)
                    {
                        bandGains[i] = Math.Clamp(settings.BandGainsDb[i], -12f, 12f);
                    }
                }

                bassBoost = Math.Clamp(settings.BassBoost, 0f, 1f);
                surround3D = Math.Clamp(settings.Surround3D, 0f, 1f);
                vocalClarity = Math.Clamp(settings.VocalClarity, 0f, 1f);

                RecreateFilters();
            }
        }

        public void UpdateBand(int bandIndex, float gainDb)
        {
            if (bandIndex < 0 || bandIndex >= BandFrequencies.Length) return;

            lock (filterLock)
            {
                bandGains[bandIndex] = Math.Clamp(gainDb, -12f, 12f);
                for (int ch = 0; ch < channels; ch++)
                {
                    filters[ch, bandIndex] = CreateBandFilter(bandIndex, bandGains[bandIndex]);
                }
            }
        }

        public void UpdatePreamp(float gainDb)
        {
            lock (filterLock)
            {
                preampDb = Math.Clamp(gainDb, -12f, 12f);
                preampFactor = (float)Math.Pow(10, preampDb / 20.0);
            }
        }

        public void UpdateBassBoost(float value)
        {
            lock (filterLock)
            {
                bassBoost = Math.Clamp(value, 0f, 1f);
                float boostGainDb = bassBoost * 8.0f; // Max +8dB for low-end punch
                for (int ch = 0; ch < channels; ch++)
                {
                    bassFilters[ch] = BiQuadFilter.PeakingEQ(sampleRate, 75f, 1.0f, boostGainDb);
                }
            }
        }

        public void UpdateVocalClarity(float value)
        {
            lock (filterLock)
            {
                vocalClarity = Math.Clamp(value, 0f, 1f);
                float vocalGainDb = vocalClarity * 6.0f; // Max +6dB presence
                for (int ch = 0; ch < channels; ch++)
                {
                    vocalFilters[ch] = BiQuadFilter.PeakingEQ(sampleRate, 2800f, 1.2f, vocalGainDb);
                }
            }
        }

        public void UpdateSurround3D(float value)
        {
            lock (filterLock)
            {
                surround3D = Math.Clamp(value, 0f, 1f);
            }
        }

        private void RecreateFilters()
        {
            for (int ch = 0; ch < channels; ch++)
            {
                for (int band = 0; band < BandFrequencies.Length; band++)
                {
                    filters[ch, band] = CreateBandFilter(band, bandGains[band]);
                }

                float boostGainDb = bassBoost * 8.0f;
                bassFilters[ch] = BiQuadFilter.PeakingEQ(sampleRate, 75f, 1.0f, boostGainDb);

                float vocalGainDb = vocalClarity * 6.0f;
                vocalFilters[ch] = BiQuadFilter.PeakingEQ(sampleRate, 2800f, 1.2f, vocalGainDb);
            }
        }

        private BiQuadFilter CreateBandFilter(int bandIndex, float gainDb)
        {
            float freq = BandFrequencies[bandIndex];
            if (bandIndex == 0)
            {
                return BiQuadFilter.LowShelf(sampleRate, 40f, 1.0f, gainDb);
            }
            if (bandIndex == BandFrequencies.Length - 1)
            {
                return BiQuadFilter.HighShelf(sampleRate, 14000f, 1.0f, gainDb);
            }
            return BiQuadFilter.PeakingEQ(sampleRate, freq, 1.4f, gainDb);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);
            if (samplesRead == 0 || !isEnabled)
            {
                return samplesRead;
            }

            lock (filterLock)
            {
                int bandCount = BandFrequencies.Length;
                float currentPreamp = preampFactor;
                float currentSurround = surround3D;
                bool hasSurround = currentSurround > 0.01f && channels == 2;
                bool hasBass = bassBoost > 0.01f;
                bool hasVocal = vocalClarity > 0.01f;

                for (int n = 0; n < samplesRead; n += channels)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float sample = buffer[offset + n + ch];

                        // 1. Dải tần số 10-Band EQ
                        for (int band = 0; band < bandCount; band++)
                        {
                            var filter = filters[ch, band];
                            if (filter != null)
                            {
                                sample = filter.Transform(sample);
                            }
                        }

                        // 2. Bass Boost
                        if (hasBass && bassFilters[ch] != null)
                        {
                            sample = bassFilters[ch].Transform(sample);
                        }

                        // 3. Vocal Clarity
                        if (hasVocal && vocalFilters[ch] != null)
                        {
                            sample = vocalFilters[ch].Transform(sample);
                        }

                        // 4. Preamp Gain
                        sample *= currentPreamp;

                        buffer[offset + n + ch] = sample;
                    }

                    // 5. 3D Surround Sound (Mid/Side processing cho Stereo)
                    if (hasSurround)
                    {
                        float left = buffer[offset + n];
                        float right = buffer[offset + n + 1];

                        float mid = (left + right) * 0.5f;
                        float side = (left - right) * 0.5f;

                        float sideBoost = 1.0f + (currentSurround * 1.25f);
                        float midAtten = 1.0f - (currentSurround * 0.12f);

                        float newLeft = (mid * midAtten) + (side * sideBoost);
                        float newRight = (mid * midAtten) - (side * sideBoost);

                        // Soft limiting chống clipping
                        buffer[offset + n] = Math.Clamp(newLeft, -1.0f, 1.0f);
                        buffer[offset + n + 1] = Math.Clamp(newRight, -1.0f, 1.0f);
                    }
                    else
                    {
                        for (int ch = 0; ch < channels; ch++)
                        {
                            buffer[offset + n + ch] = Math.Clamp(buffer[offset + n + ch], -1.0f, 1.0f);
                        }
                    }
                }
            }

            return samplesRead;
        }
    }
}

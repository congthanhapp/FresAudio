using System;
using System.Collections.Generic;
using System.Linq;

namespace FresAudio.Models
{
    public enum EqualizerScope
    {
        Global = 0,
        Song = 1,
        Folder = 2,
        Playlist = 3
    }

    public class EqualizerPreset
    {
        public string Name { get; set; } = "Flat";
        public float PreampDb { get; set; } = 0.0f;
        public float[] Bands { get; set; } = new float[10]; // 32Hz, 64Hz, 125Hz, 250Hz, 500Hz, 1kHz, 2kHz, 4kHz, 8kHz, 16kHz
        public float BassBoost { get; set; } = 0.0f;       // 0.0 - 1.0
        public float Surround3D { get; set; } = 0.0f;      // 0.0 - 1.0
        public float VocalClarity { get; set; } = 0.0f;    // 0.0 - 1.0
        public bool IsCustom { get; set; } = false;

        public EqualizerPreset()
        {
            Bands = new float[10];
        }

        public EqualizerPreset(string name, float preamp, float[] bands, float bassBoost = 0f, float surround = 0f, float vocal = 0f, bool isCustom = false)
        {
            Name = name;
            PreampDb = preamp;
            Bands = new float[10];
            if (bands != null)
            {
                for (int i = 0; i < Math.Min(10, bands.Length); i++)
                {
                    Bands[i] = bands[i];
                }
            }
            BassBoost = bassBoost;
            Surround3D = surround;
            VocalClarity = vocal;
            IsCustom = isCustom;
        }

        public static List<EqualizerPreset> GetBuiltInPresets()
        {
            return new List<EqualizerPreset>
            {
                new EqualizerPreset("Flat", 0f, new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 0f, 0f, 0f),
                new EqualizerPreset("Bass Boost", -2f, new float[] { 6.0f, 5.0f, 3.5f, 1.5f, 0f, 0f, 0f, 0f, 1.0f, 1.5f }, 0.45f, 0f, 0f),
                new EqualizerPreset("Bass Extreme", -3.5f, new float[] { 9.0f, 7.5f, 5.0f, 2.0f, 0f, -1.0f, 0f, 1.0f, 2.0f, 2.0f }, 0.75f, 0f, 0f),
                new EqualizerPreset("Vocal / Acoustic", 0f, new float[] { -2.0f, -1.0f, 0f, 2.0f, 4.0f, 4.5f, 3.5f, 2.0f, 1.0f, 0f }, 0f, 0.15f, 0.5f),
                new EqualizerPreset("Pop", -1f, new float[] { -1.5f, 1.0f, 3.0f, 4.0f, 3.0f, 0f, -1.0f, -1.0f, 1.5f, 2.5f }, 0.2f, 0.2f, 0.2f),
                new EqualizerPreset("Rock", -2f, new float[] { 5.0f, 3.5f, 1.5f, -1.0f, -2.0f, 0.5f, 2.5f, 3.5f, 4.5f, 4.5f }, 0.3f, 0.25f, 0.2f),
                new EqualizerPreset("EDM / Dance", -2.5f, new float[] { 6.0f, 5.0f, 2.5f, 0f, 0f, 2.0f, 3.5f, 4.5f, 5.0f, 4.0f }, 0.5f, 0.3f, 0.1f),
                new EqualizerPreset("Jazz", 0f, new float[] { 3.0f, 2.0f, 1.0f, 2.0f, -1.0f, -1.0f, 0f, 1.5f, 2.5f, 3.0f }, 0.1f, 0.3f, 0.2f),
                new EqualizerPreset("Classical", 0f, new float[] { 4.0f, 3.0f, 2.0f, 1.5f, -1.0f, -1.0f, 0f, 2.0f, 3.0f, 3.5f }, 0f, 0.4f, 0.1f),
                new EqualizerPreset("Treble Boost", -1f, new float[] { 0f, 0f, 0f, 0f, 0f, 1.0f, 2.5f, 4.5f, 6.5f, 8.0f }, 0f, 0.1f, 0.4f),
                new EqualizerPreset("Gaming / Surround 3D", -1.5f, new float[] { 3.5f, 2.5f, 0f, -1.0f, 0f, 1.5f, 2.0f, 3.5f, 4.0f, 4.5f }, 0.35f, 0.65f, 0.3f)
            };
        }
    }

    public class EqualizerSettings
    {
        public bool IsEnabled { get; set; } = true;
        public string PresetName { get; set; } = "Flat";
        public float PreampDb { get; set; } = 0.0f;
        public float[] BandGainsDb { get; set; } = new float[10];
        public float BassBoost { get; set; } = 0.0f;
        public float Surround3D { get; set; } = 0.0f;
        public float VocalClarity { get; set; } = 0.0f;
        public EqualizerScope Scope { get; set; } = EqualizerScope.Global;

        public EqualizerSettings()
        {
            BandGainsDb = new float[10];
        }

        public EqualizerSettings Clone()
        {
            var clone = new EqualizerSettings
            {
                IsEnabled = this.IsEnabled,
                PresetName = this.PresetName ?? "Flat",
                PreampDb = this.PreampDb,
                BassBoost = this.BassBoost,
                Surround3D = this.Surround3D,
                VocalClarity = this.VocalClarity,
                Scope = this.Scope,
                BandGainsDb = new float[10]
            };
            if (this.BandGainsDb != null)
            {
                for (int i = 0; i < Math.Min(10, this.BandGainsDb.Length); i++)
                {
                    clone.BandGainsDb[i] = this.BandGainsDb[i];
                }
            }
            return clone;
        }

        public void ApplyPreset(EqualizerPreset preset)
        {
            if (preset == null) return;
            PresetName = preset.Name;
            PreampDb = preset.PreampDb;
            BassBoost = preset.BassBoost;
            Surround3D = preset.Surround3D;
            VocalClarity = preset.VocalClarity;
            IsEnabled = true;
            for (int i = 0; i < 10; i++)
            {
                BandGainsDb[i] = i < preset.Bands.Length ? preset.Bands[i] : 0f;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;
using FresAudio.Models;

namespace FresAudio.Services
{
    public class AudioPlayerService : IDisposable
    {
        private IWavePlayer waveOut;
        private WaveStream audioFileReader;
        private SoundTouchSampleProvider soundTouchProvider;
        private EqualizerSampleProvider equalizerProvider;
        private SampleAggregator sampleAggregator;
        private VolumeSampleProvider volumeProvider;
        private float playbackSpeed = 1.0f;
        private EqualizerSettings currentEqualizerSettings = new EqualizerSettings();
        
        public event EventHandler<FftEventArgs> FftCalculated;
        public event EventHandler<StoppedEventArgs> PlaybackStopped;
        
        public int SelectedDeviceNumber { get; set; } = -1;
        public bool IsPlaying => waveOut != null && waveOut.PlaybackState == PlaybackState.Playing;
        
        public float PlaybackSpeed
        {
            get => playbackSpeed;
            set
            {
                playbackSpeed = Math.Clamp(value, 0.25f, 3.0f);
                if (soundTouchProvider != null)
                {
                    soundTouchProvider.PlaybackSpeed = playbackSpeed;
                }
            }
        }

        public float Volume
        {
            get => volumeProvider?.Volume ?? 1.0f;
            set
            {
                if (volumeProvider != null)
                {
                    volumeProvider.Volume = value;
                }
            }
        }
        
        public TimeSpan CurrentTime
        {
            get => audioFileReader?.CurrentTime ?? TimeSpan.Zero;
            set
            {
                if (audioFileReader != null)
                {
                    audioFileReader.CurrentTime = value;
                    soundTouchProvider?.Clear();
                }
            }
        }
        
        public TimeSpan TotalTime => audioFileReader?.TotalTime ?? TimeSpan.Zero;
        public bool HasAudio => audioFileReader != null;

        public void ApplyEqualizerSettings(EqualizerSettings settings)
        {
            if (settings == null) return;
            currentEqualizerSettings = settings.Clone();
            equalizerProvider?.ApplySettings(currentEqualizerSettings);
        }

        public void UpdateEqualizerBand(int bandIndex, float gainDb)
        {
            if (currentEqualizerSettings != null && bandIndex >= 0 && bandIndex < 10)
            {
                currentEqualizerSettings.BandGainsDb[bandIndex] = gainDb;
            }
            equalizerProvider?.UpdateBand(bandIndex, gainDb);
        }

        public void UpdateEqualizerPreamp(float preampDb)
        {
            if (currentEqualizerSettings != null)
            {
                currentEqualizerSettings.PreampDb = preampDb;
            }
            equalizerProvider?.UpdatePreamp(preampDb);
        }

        public void SetEqualizerEnabled(bool enabled)
        {
            if (currentEqualizerSettings != null)
            {
                currentEqualizerSettings.IsEnabled = enabled;
            }
            if (equalizerProvider != null)
            {
                equalizerProvider.IsEnabled = enabled;
            }
        }

        public void UpdateBassBoost(float value)
        {
            if (currentEqualizerSettings != null)
            {
                currentEqualizerSettings.BassBoost = value;
            }
            equalizerProvider?.UpdateBassBoost(value);
        }

        public void UpdateSurround3D(float value)
        {
            if (currentEqualizerSettings != null)
            {
                currentEqualizerSettings.Surround3D = value;
            }
            equalizerProvider?.UpdateSurround3D(value);
        }

        public void UpdateVocalClarity(float value)
        {
            if (currentEqualizerSettings != null)
            {
                currentEqualizerSettings.VocalClarity = value;
            }
            equalizerProvider?.UpdateVocalClarity(value);
        }

        public void Play(string filePath, float initialVolume = 1.0f)
        {
            DisposeCurrentAudio();

            try
            {
                try
                {
                    audioFileReader = new AudioFileReader(filePath);
                }
                catch
                {
                    audioFileReader = new MediaFoundationReader(filePath);
                }

                ISampleProvider sampleProvider = audioFileReader.ToSampleProvider();
                if (sampleProvider.WaveFormat.SampleRate != 44100)
                {
                    sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 44100);
                }

                soundTouchProvider = new SoundTouchSampleProvider(sampleProvider)
                {
                    PlaybackSpeed = playbackSpeed
                };

                equalizerProvider = new EqualizerSampleProvider(soundTouchProvider);
                if (currentEqualizerSettings != null)
                {
                    equalizerProvider.ApplySettings(currentEqualizerSettings);
                }

                sampleAggregator = new SampleAggregator(equalizerProvider);
                sampleAggregator.FftCalculated += (s, e) => FftCalculated?.Invoke(this, e);
                
                volumeProvider = new VolumeSampleProvider(sampleAggregator) { Volume = initialVolume };

                InitializeWaveOut();
                waveOut.Play();
            }
            catch (Exception ex)
            {
                throw new Exception($"Không thể phát bài hát: {ex.Message}");
            }
        }

        public void Play()
        {
            if (waveOut != null && waveOut.PlaybackState != PlaybackState.Playing)
            {
                waveOut.Play();
            }
        }

        public void Pause()
        {
            if (waveOut != null && waveOut.PlaybackState == PlaybackState.Playing)
            {
                waveOut.Pause();
            }
        }

        public void Stop()
        {
            if (waveOut != null)
            {
                waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                try { waveOut.Stop(); } catch { }
                DisposeCurrentAudio();
            }
        }

        public void ChangeDevice(int newDeviceNumber)
        {
            SelectedDeviceNumber = newDeviceNumber;
            if (waveOut == null || audioFileReader == null || volumeProvider == null) return;

            bool wasPlaying = waveOut.PlaybackState == PlaybackState.Playing;
            
            waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
            try { waveOut.Stop(); } catch { }
            waveOut.Dispose();
            waveOut = null;

            InitializeWaveOut();
            
            if (wasPlaying)
            {
                waveOut.Play();
            }
        }

        private void InitializeWaveOut()
        {
            try
            {
                if (SelectedDeviceNumber < 0)
                {
                    waveOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
                }
                else
                {
                    var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                    var endpoints = new List<NAudio.CoreAudioApi.MMDevice>(enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active));
                    
                    if (SelectedDeviceNumber < endpoints.Count)
                    {
                        waveOut = new WasapiOut(endpoints[SelectedDeviceNumber], NAudio.CoreAudioApi.AudioClientShareMode.Shared, false, 100);
                    }
                    else
                    {
                        waveOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
                    }
                }
            }
            catch
            {
                try
                {
                    waveOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
                }
                catch
                {
                    waveOut = null;
                }
            }

            if (waveOut != null)
            {
                try
                {
                    waveOut.Init(volumeProvider);
                    waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                }
                catch
                {
                    waveOut.Dispose();
                    waveOut = null;
                }
            }
        }

        private void WaveOut_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            PlaybackStopped?.Invoke(this, e);
        }

        private void DisposeCurrentAudio()
        {
            if (waveOut != null)
            {
                waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                try { waveOut.Stop(); } catch { }
                waveOut.Dispose();
                waveOut = null;
            }
            
            if (audioFileReader != null)
            {
                audioFileReader.Dispose();
                audioFileReader = null;
            }

            if (soundTouchProvider != null)
            {
                soundTouchProvider.Clear();
                soundTouchProvider = null;
            }

            equalizerProvider = null;
        }

        public void Dispose()
        {
            try
            {
                if (_deviceEnumerator != null && _notificationClient != null)
                {
                    _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                    _deviceEnumerator.Dispose();
                }
            }
            catch { }
            
            DisposeCurrentAudio();
        }

        public List<string> GetAudioDevices()
        {
            var devices = new List<string>();
            devices.Add("Mặc định hệ thống");
            
            try
            {
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    devices.Add(endpoint.FriendlyName);
                }
            }
            catch { }
            
            return devices;
        }

        public event EventHandler DefaultDeviceChanged;

        private NAudio.CoreAudioApi.MMDeviceEnumerator _deviceEnumerator;
        private NotificationClient _notificationClient;

        public AudioPlayerService()
        {
            try
            {
                _deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                _notificationClient = new NotificationClient(this);
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
            }
            catch { }
        }

        private class NotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
        {
            private readonly AudioPlayerService _service;
            public NotificationClient(AudioPlayerService service) { _service = service; }
            public void OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow flow, NAudio.CoreAudioApi.Role role, string defaultDeviceId)
            {
                if (flow == NAudio.CoreAudioApi.DataFlow.Render && role == NAudio.CoreAudioApi.Role.Multimedia)
                {
                    _service.DefaultDeviceChanged?.Invoke(_service, EventArgs.Empty);
                }
            }
            public void OnDeviceAdded(string pwstrDeviceId) { }
            public void OnDeviceRemoved(string deviceId) { }
            public void OnDeviceStateChanged(string deviceId, NAudio.CoreAudioApi.DeviceState newState) { }
            public void OnPropertyValueChanged(string pwstrDeviceId, NAudio.CoreAudioApi.PropertyKey key) { }
        }
    }
}

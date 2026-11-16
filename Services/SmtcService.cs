using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Storage.Streams;

namespace FresAudio.Services
{
    public class SmtcService : IDisposable
    {
        private SystemMediaTransportControls? _smtc;
        private InMemoryRandomAccessStream? _currentThumbnailStream;

        public event Action? PlayRequested;
        public event Action? PauseRequested;
        public event Action? NextRequested;
        public event Action? PreviousRequested;
        public event Action? StopRequested;

        public bool IsInitialized => _smtc != null;

        public void Initialize(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || _smtc != null) return;

            try
            {
                _smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
                if (_smtc != null)
                {
                    _smtc.IsEnabled = true;
                    _smtc.IsPlayEnabled = true;
                    _smtc.IsPauseEnabled = true;
                    _smtc.IsNextEnabled = true;
                    _smtc.IsPreviousEnabled = true;
                    _smtc.IsStopEnabled = true;

                    _smtc.ButtonPressed += Smtc_ButtonPressed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmtcService] Initialization error: {ex.Message}");
            }
        }

        private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            try
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        PlayRequested?.Invoke();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        PauseRequested?.Invoke();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        NextRequested?.Invoke();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        PreviousRequested?.Invoke();
                        break;
                    case SystemMediaTransportControlsButton.Stop:
                        StopRequested?.Invoke();
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmtcService] Button press error: {ex.Message}");
            }
        }

        public void SetPlaybackStatus(MediaPlaybackStatus status)
        {
            if (_smtc == null) return;
            try
            {
                _smtc.PlaybackStatus = status;
            }
            catch { }
        }

        public async Task UpdateMetadataAsync(string title, string artist, string? albumTitle = null, byte[]? albumArtBytes = null)
        {
            if (_smtc == null) return;

            try
            {
                var updater = _smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = string.IsNullOrWhiteSpace(title) ? "FresAudio" : title;
                updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(artist) ? "FresAudio" : artist;
                if (!string.IsNullOrWhiteSpace(albumTitle))
                {
                    updater.MusicProperties.AlbumTitle = albumTitle;
                }

                if (albumArtBytes != null && albumArtBytes.Length > 0)
                {
                    _currentThumbnailStream?.Dispose();
                    _currentThumbnailStream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(_currentThumbnailStream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(albumArtBytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(_currentThumbnailStream);
                }
                else
                {
                    _currentThumbnailStream?.Dispose();
                    _currentThumbnailStream = null;
                    updater.Thumbnail = null;
                }

                updater.Update();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmtcService] Update metadata error: {ex.Message}");
            }
        }

        public void UpdateTimeline(TimeSpan position, TimeSpan duration)
        {
            if (_smtc == null) return;
            try
            {
                if (duration > TimeSpan.Zero && position <= duration)
                {
                    var timeline = new SystemMediaTransportControlsTimelineProperties
                    {
                        StartTime = TimeSpan.Zero,
                        EndTime = duration,
                        Position = position,
                        MinSeekTime = TimeSpan.Zero,
                        MaxSeekTime = duration
                    };
                    _smtc.UpdateTimelineProperties(timeline);
                }
            }
            catch { }
        }

        public void Clear()
        {
            if (_smtc == null) return;
            try
            {
                _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                var updater = _smtc.DisplayUpdater;
                updater.MusicProperties.Title = "";
                updater.MusicProperties.Artist = "";
                updater.MusicProperties.AlbumTitle = "";
                _currentThumbnailStream?.Dispose();
                _currentThumbnailStream = null;
                updater.Thumbnail = null;
                updater.Update();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_smtc != null)
            {
                try
                {
                    _smtc.ButtonPressed -= Smtc_ButtonPressed;
                    _smtc.IsEnabled = false;
                }
                catch { }
                _smtc = null;
            }
            _currentThumbnailStream?.Dispose();
            _currentThumbnailStream = null;
        }
    }
}

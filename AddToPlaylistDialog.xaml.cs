using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FresAudio.Models;
using FresAudio.Services;

namespace FresAudio
{
    public class SelectablePlaylistItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int SongCount { get; set; }
        public string SongCountText => $"{SongCount} bài hát";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        public bool IsOriginallyChecked { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class AddToPlaylistDialog : Window
    {
        private readonly SettingsService _settingsService;
        private readonly List<string> _targetSongPaths = new List<string>();
        private readonly ObservableCollection<SelectablePlaylistItem> _playlistItems = new ObservableCollection<SelectablePlaylistItem>();

        public AddToPlaylistDialog(string songPath, string songDisplayName, SettingsService settingsService)
            : this(new List<string> { songPath }, songDisplayName, false, settingsService)
        {
        }

        public AddToPlaylistDialog(List<string> songPaths, string displayName, bool isFolder, SettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _targetSongPaths = songPaths ?? new List<string>();

            txtTargetType.Text = isFolder ? "Thư mục đang chọn:" : "Bài hát đang chọn:";
            txtTargetName.Text = displayName ?? "";

            LoadPlaylists();
        }

        private void LoadPlaylists()
        {
            _playlistItems.Clear();
            var customPlaylists = _settingsService.CurrentSettings.CustomPlaylists ?? new List<CustomPlaylist>();

            if (customPlaylists.Count == 0)
            {
                pnlNoPlaylists.Visibility = Visibility.Visible;
                lstPlaylists.Visibility = Visibility.Collapsed;
            }
            else
            {
                pnlNoPlaylists.Visibility = Visibility.Collapsed;
                lstPlaylists.Visibility = Visibility.Visible;

                foreach (var pl in customPlaylists)
                {
                    // Đã có tất cả bài hát mục tiêu trong playlist này chưa
                    bool hasAllSongs = _targetSongPaths.Count > 0 && _targetSongPaths.All(p => pl.SongPaths.Contains(p, StringComparer.OrdinalIgnoreCase));

                    _playlistItems.Add(new SelectablePlaylistItem
                    {
                        Id = pl.Id,
                        Name = pl.Name,
                        SongCount = pl.SongPaths.Count,
                        IsChecked = hasAllSongs,
                        IsOriginallyChecked = hasAllSongs
                    });
                }
            }

            lstPlaylists.ItemsSource = _playlistItems;
            UpdateSummary();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void PlaylistItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is SelectablePlaylistItem item)
            {
                item.IsChecked = !item.IsChecked;
                UpdateSummary();
            }
        }

        private void PlaylistCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int selectedCount = _playlistItems.Count(p => p.IsChecked);
            txtSelectedSummary.Text = $"Đã chọn: {selectedCount} playlist";
            btnConfirm.Content = selectedCount > 0 ? $"LƯU ({selectedCount})" : "LƯU";
        }

        public Action? OnPlaylistsSaved { get; set; }

        private void BtnCreateNewPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var existingNames = _settingsService.CurrentSettings.CustomPlaylists?.Select(p => p.Name).ToList();
            var dialog = new CreatePlaylistDialog(existingPlaylistNames: existingNames)
            {
                Owner = this,
                OnConfirmed = (d) =>
                {
                    if (!string.IsNullOrWhiteSpace(d.PlaylistName))
                    {
                        var newPl = new CustomPlaylist
                        {
                            Name = d.PlaylistName,
                            SongPaths = new List<string>(_targetSongPaths)
                        };

                        if (_settingsService.CurrentSettings.CustomPlaylists == null)
                        {
                            _settingsService.CurrentSettings.CustomPlaylists = new List<CustomPlaylist>();
                        }
                        _settingsService.CurrentSettings.CustomPlaylists.Add(newPl);
                        _settingsService.SaveSettings();

                        LoadPlaylists();
                    }
                }
            };
            dialog.Show();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            var customPlaylists = _settingsService.CurrentSettings.CustomPlaylists ?? new List<CustomPlaylist>();
            int addedCount = 0;

            foreach (var item in _playlistItems)
            {
                var targetPl = customPlaylists.FirstOrDefault(p => p.Id == item.Id);
                if (targetPl == null) continue;

                if (item.IsChecked)
                {
                    // Thêm các bài chưa có vào playlist này
                    foreach (var path in _targetSongPaths)
                    {
                        if (!targetPl.SongPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            targetPl.SongPaths.Add(path);
                            addedCount++;
                        }
                    }
                }
                else if (item.IsOriginallyChecked && !item.IsChecked)
                {
                    // Bỏ chọn bài khỏi playlist này
                    foreach (var path in _targetSongPaths)
                    {
                        targetPl.SongPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            _settingsService.SaveSettings();
            try { DialogResult = true; } catch { }
            OnPlaylistsSaved?.Invoke();
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try { DialogResult = false; } catch { }
            Close();
        }
    }
}

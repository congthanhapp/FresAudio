using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FresAudio.Models;
using FresAudio.Services;

namespace FresAudio
{
    public class SongScopeItem
    {
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public string CleanTitle { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public override string ToString() => DisplayName;
    }

    public class FolderScopeItem
    {
        public string FolderPath { get; set; }
        public string FolderName { get; set; }
        public override string ToString() => FolderName;
    }

    public class PlaylistScopeItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    public partial class EqualizerWindow : Window
    {
        private readonly SettingsService settingsService;
        private readonly AudioPlayerService audioPlayerService;
        private readonly PlaylistService playlistService;

        private string currentSongPath;
        private string currentSongTitle;
        private string currentFolderPath;
        private string currentPlaylistId;
        private string currentPlaylistName;

        private EqualizerSettings editingSettings;
        private EqualizerScope activeScope = EqualizerScope.Global;
        private bool isUpdatingUI = true;

        private Slider[] bandSliders;
        private TextBlock[] bandTexts;

        public EqualizerWindow(
            SettingsService settingsService,
            AudioPlayerService audioPlayerService,
            PlaylistService playlistService,
            string currentSongPath = "",
            string currentSongTitle = "",
            string currentFolderPath = "",
            string currentPlaylistId = "",
            string currentPlaylistName = "",
            EqualizerScope initialScope = EqualizerScope.Global)
        {
            isUpdatingUI = true;
            this.settingsService = settingsService;
            this.audioPlayerService = audioPlayerService;
            this.playlistService = playlistService;
            this.currentSongPath = currentSongPath ?? "";
            this.currentSongTitle = string.IsNullOrEmpty(currentSongTitle) ? (string.IsNullOrEmpty(currentSongPath) ? "" : System.IO.Path.GetFileNameWithoutExtension(currentSongPath)) : currentSongTitle;
            this.currentFolderPath = currentFolderPath ?? "";
            this.currentPlaylistId = currentPlaylistId ?? "";
            this.currentPlaylistName = currentPlaylistName ?? "";

            InitializeComponent();

            bandSliders = new Slider[]
            {
                sliderBand0, sliderBand1, sliderBand2, sliderBand3, sliderBand4,
                sliderBand5, sliderBand6, sliderBand7, sliderBand8, sliderBand9
            };

            bandTexts = new TextBlock[]
            {
                txtBand0, txtBand1, txtBand2, txtBand3, txtBand4,
                txtBand5, txtBand6, txtBand7, txtBand8, txtBand9
            };

            InitTargetPickers();
            LoadPresets();
            SetScope(initialScope);
            isUpdatingUI = false;
        }

        public void RefreshTargetLists()
        {
            InitTargetPickers();
        }

        public void UpdatePlayingSong(string newSongPath, string newSongTitle, string newFolderPath = "", string newPlaylistId = "")
        {
            if (string.IsNullOrEmpty(newSongPath)) return;

            var previousSelected = cboSongPicker?.SelectedItem as SongScopeItem;
            bool wasPlayingSelected = (previousSelected == null || previousSelected.IsCurrentlyPlaying || string.Equals(previousSelected.FilePath, currentSongPath, StringComparison.OrdinalIgnoreCase));

            currentSongPath = newSongPath;
            currentSongTitle = string.IsNullOrEmpty(newSongTitle) ? System.IO.Path.GetFileNameWithoutExtension(newSongPath) : newSongTitle;
            if (!string.IsNullOrEmpty(newFolderPath)) currentFolderPath = newFolderPath;
            if (!string.IsNullOrEmpty(newPlaylistId)) currentPlaylistId = newPlaylistId;

            RefreshSongPicker(newSongPath, newSongTitle);

            if (activeScope == EqualizerScope.Song && wasPlayingSelected)
            {
                SetScope(EqualizerScope.Song);
            }
        }

        private void InitTargetPickers()
        {
            // 1. Songs Picker (Bài đang phát luôn ở ĐẦU DANH SÁCH)
            RefreshSongPicker();

            // 2. Folders Picker
            var folderList = new List<FolderScopeItem>();
            if (playlistService != null && playlistService.LoadedFolders != null)
            {
                foreach (var folder in playlistService.LoadedFolders)
                {
                    string fName = System.IO.Path.GetFileName(folder);
                    if (string.IsNullOrEmpty(fName)) fName = folder;
                    folderList.Add(new FolderScopeItem { FolderPath = folder, FolderName = fName });
                }
            }

            if (folderList.Count == 0 && !string.IsNullOrEmpty(currentFolderPath))
            {
                string fName = System.IO.Path.GetFileName(currentFolderPath);
                if (string.IsNullOrEmpty(fName)) fName = currentFolderPath;
                folderList.Add(new FolderScopeItem { FolderPath = currentFolderPath, FolderName = fName });
            }

            cboFolderPicker.ItemsSource = folderList;
            var matchedFolder = folderList.FirstOrDefault(f => string.Equals(f.FolderPath, currentFolderPath, StringComparison.OrdinalIgnoreCase));
            cboFolderPicker.SelectedItem = matchedFolder ?? folderList.FirstOrDefault();

            // 3. Playlists Picker
            var playlistItems = new List<PlaylistScopeItem>();
            if (settingsService?.CurrentSettings?.CustomPlaylists != null)
            {
                foreach (var pl in settingsService.CurrentSettings.CustomPlaylists)
                {
                    playlistItems.Add(new PlaylistScopeItem { Id = pl.Id, Name = pl.Name });
                }
            }

            cboPlaylistPicker.ItemsSource = playlistItems;
            var matchedPl = playlistItems.FirstOrDefault(p => string.Equals(p.Id, currentPlaylistId, StringComparison.OrdinalIgnoreCase));
            cboPlaylistPicker.SelectedItem = matchedPl ?? playlistItems.FirstOrDefault();

            if (folderList.Count == 0)
            {
                rbScopeFolder.IsEnabled = false;
                rbScopeFolder.ToolTip = "Chưa có thư mục nhạc nào được nạp";
            }
            if (playlistItems.Count == 0)
            {
                rbScopePlaylist.IsEnabled = false;
                rbScopePlaylist.ToolTip = "Chưa có playlist nào được tạo";
            }
        }

        private void RefreshSongPicker(string playingSongPath = null, string playingSongTitle = null)
        {
            if (!string.IsNullOrEmpty(playingSongPath))
            {
                currentSongPath = playingSongPath;
                currentSongTitle = string.IsNullOrEmpty(playingSongTitle) ? System.IO.Path.GetFileNameWithoutExtension(playingSongPath) : playingSongTitle;
            }

            var previousSelected = cboSongPicker?.SelectedItem as SongScopeItem;
            string previousPath = previousSelected?.FilePath ?? currentSongPath;
            bool wasPlayingSelected = (previousSelected == null || previousSelected.IsCurrentlyPlaying);

            var songList = new List<SongScopeItem>();

            // 1. Đặt bài hát đang phát ở VỊ TRÍ ĐẦU TIÊN (INDEX 0)
            if (!string.IsNullOrEmpty(currentSongPath))
            {
                string cleanName = !string.IsNullOrEmpty(currentSongTitle) ? currentSongTitle : System.IO.Path.GetFileNameWithoutExtension(currentSongPath);
                songList.Add(new SongScopeItem
                {
                    FilePath = currentSongPath,
                    DisplayName = $"▶ [Đang phát] {cleanName}",
                    CleanTitle = cleanName,
                    IsCurrentlyPlaying = true
                });
            }

            // 2. Thêm tất cả các bài hát còn lại trong danh sách
            if (playlistService != null && playlistService.PlaylistPaths != null)
            {
                for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
                {
                    string path = playlistService.PlaylistPaths[i];
                    if (string.Equals(path, currentSongPath, StringComparison.OrdinalIgnoreCase)) continue;

                    string name = (i < playlistService.DisplayPlaylist.Count) ? playlistService.DisplayPlaylist[i] : System.IO.Path.GetFileNameWithoutExtension(path);
                    songList.Add(new SongScopeItem
                    {
                        FilePath = path,
                        DisplayName = name,
                        CleanTitle = name,
                        IsCurrentlyPlaying = false
                    });
                }
            }

            bool oldUpdating = isUpdatingUI;
            isUpdatingUI = true;

            cboSongPicker.ItemsSource = null;
            cboSongPicker.ItemsSource = songList;

            if (wasPlayingSelected || string.IsNullOrEmpty(previousPath))
            {
                cboSongPicker.SelectedIndex = songList.Count > 0 ? 0 : -1;
            }
            else
            {
                var match = songList.FirstOrDefault(s => string.Equals(s.FilePath, previousPath, StringComparison.OrdinalIgnoreCase));
                cboSongPicker.SelectedItem = match ?? (songList.Count > 0 ? songList[0] : null);
            }

            rbScopeSong.IsEnabled = (songList.Count > 0);
            if (songList.Count == 0)
            {
                rbScopeSong.ToolTip = "Danh sách bài hát hiện đang trống";
            }

            isUpdatingUI = oldUpdating;
        }

        private void SetScope(EqualizerScope scope)
        {
            activeScope = scope;
            isUpdatingUI = true;

            var currentSettings = settingsService?.CurrentSettings;
            var globalEq = currentSettings?.GlobalEqualizer ?? new EqualizerSettings { IsEnabled = true };

            switch (scope)
            {
                case EqualizerScope.Global:
                    if (rbScopeGlobal != null) rbScopeGlobal.IsChecked = true;
                    if (panelTargetPicker != null) panelTargetPicker.Visibility = Visibility.Collapsed;
                    if (borderSongPicker != null) borderSongPicker.Visibility = Visibility.Collapsed;
                    if (borderFolderPicker != null) borderFolderPicker.Visibility = Visibility.Collapsed;
                    if (borderPlaylistPicker != null) borderPlaylistPicker.Visibility = Visibility.Collapsed;
                    editingSettings = globalEq.Clone();
                    break;

                case EqualizerScope.Song:
                    if (rbScopeSong != null) rbScopeSong.IsChecked = true;
                    if (panelTargetPicker != null) panelTargetPicker.Visibility = Visibility.Visible;
                    if (lblPickerTitle != null) lblPickerTitle.Text = "Chọn bài hát:";
                    if (borderSongPicker != null) borderSongPicker.Visibility = Visibility.Visible;
                    if (borderFolderPicker != null) borderFolderPicker.Visibility = Visibility.Collapsed;
                    if (borderPlaylistPicker != null) borderPlaylistPicker.Visibility = Visibility.Collapsed;

                    var selSong = cboSongPicker?.SelectedItem as SongScopeItem;
                    string songPathToUse = selSong?.FilePath ?? currentSongPath;

                    bool hasSongEq = currentSettings?.SongEqualizers != null && !string.IsNullOrEmpty(songPathToUse) && currentSettings.SongEqualizers.ContainsKey(songPathToUse);
                    if (hasSongEq && currentSettings.SongEqualizers.TryGetValue(songPathToUse, out var sEq) && sEq != null)
                    {
                        editingSettings = sEq.Clone();
                    }
                    else
                    {
                        editingSettings = globalEq.Clone();
                    }
                    break;

                case EqualizerScope.Folder:
                    if (rbScopeFolder != null) rbScopeFolder.IsChecked = true;
                    if (panelTargetPicker != null) panelTargetPicker.Visibility = Visibility.Visible;
                    if (lblPickerTitle != null) lblPickerTitle.Text = "Chọn thư mục:";
                    if (borderSongPicker != null) borderSongPicker.Visibility = Visibility.Collapsed;
                    if (borderFolderPicker != null) borderFolderPicker.Visibility = Visibility.Visible;
                    if (borderPlaylistPicker != null) borderPlaylistPicker.Visibility = Visibility.Collapsed;

                    var selFolder = cboFolderPicker?.SelectedItem as FolderScopeItem;
                    string folderPathToUse = selFolder?.FolderPath ?? currentFolderPath;

                    bool hasFolderEq = currentSettings?.FolderEqualizers != null && !string.IsNullOrEmpty(folderPathToUse) && currentSettings.FolderEqualizers.ContainsKey(folderPathToUse);
                    if (hasFolderEq && currentSettings.FolderEqualizers.TryGetValue(folderPathToUse, out var fEq) && fEq != null)
                    {
                        editingSettings = fEq.Clone();
                    }
                    else
                    {
                        editingSettings = globalEq.Clone();
                    }
                    break;

                case EqualizerScope.Playlist:
                    if (rbScopePlaylist != null) rbScopePlaylist.IsChecked = true;
                    if (panelTargetPicker != null) panelTargetPicker.Visibility = Visibility.Visible;
                    if (lblPickerTitle != null) lblPickerTitle.Text = "Chọn Playlist:";
                    if (borderSongPicker != null) borderSongPicker.Visibility = Visibility.Collapsed;
                    if (borderFolderPicker != null) borderFolderPicker.Visibility = Visibility.Collapsed;
                    if (borderPlaylistPicker != null) borderPlaylistPicker.Visibility = Visibility.Visible;

                    var selPl = cboPlaylistPicker?.SelectedItem as PlaylistScopeItem;
                    string plIdToUse = selPl?.Id ?? currentPlaylistId;

                    bool hasPlEq = currentSettings?.PlaylistEqualizers != null && !string.IsNullOrEmpty(plIdToUse) && currentSettings.PlaylistEqualizers.ContainsKey(plIdToUse);
                    if (hasPlEq && currentSettings.PlaylistEqualizers.TryGetValue(plIdToUse, out var pEq) && pEq != null)
                    {
                        editingSettings = pEq.Clone();
                    }
                    else
                    {
                        editingSettings = globalEq.Clone();
                    }
                    break;
            }

            if (editingSettings == null) editingSettings = new EqualizerSettings { IsEnabled = true };

            SyncUIToSettings();
            isUpdatingUI = false;
            ApplyToAudioService();
            DrawSplineCurve();
        }

        private void CboSongPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingUI || activeScope != EqualizerScope.Song) return;
            var sel = cboSongPicker.SelectedItem as SongScopeItem;
            if (sel != null)
            {
                SetScope(EqualizerScope.Song);
            }
        }

        private void CboFolderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingUI || activeScope != EqualizerScope.Folder) return;
            var sel = cboFolderPicker.SelectedItem as FolderScopeItem;
            if (sel != null)
            {
                currentFolderPath = sel.FolderPath;
                SetScope(EqualizerScope.Folder);
            }
        }

        private void CboPlaylistPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingUI || activeScope != EqualizerScope.Playlist) return;
            var sel = cboPlaylistPicker.SelectedItem as PlaylistScopeItem;
            if (sel != null)
            {
                currentPlaylistId = sel.Id;
                currentPlaylistName = sel.Name;
                SetScope(EqualizerScope.Playlist);
            }
        }

        private void LoadPresets()
        {
            if (settingsService == null || cboPresets == null) return;
            var presets = settingsService.GetAllPresets();
            var list = presets != null ? presets.Select(p => p.Name).ToList() : new List<string>();

            // Nếu editingSettings đang mang một tên "(Tùy chỉnh)" mà chưa có trong danh sách thì bổ sung vào để ComboBox hiển thị chuẩn
            if (editingSettings != null && !string.IsNullOrEmpty(editingSettings.PresetName))
            {
                if (editingSettings.PresetName.EndsWith("(Tùy chỉnh)") && !list.Contains(editingSettings.PresetName))
                {
                    list.Add(editingSettings.PresetName);
                }
            }

            cboPresets.ItemsSource = list;
        }

        private void SyncUIToSettings()
        {
            if (editingSettings == null || bandSliders == null || bandTexts == null) return;

            LoadPresets();

            if (sliderPreamp != null) sliderPreamp.Value = editingSettings.PreampDb;
            if (txtPreampVal != null) txtPreampVal.Text = FormatDb(editingSettings.PreampDb);

            for (int i = 0; i < 10; i++)
            {
                float gain = (editingSettings.BandGainsDb != null && i < editingSettings.BandGainsDb.Length) 
                    ? editingSettings.BandGainsDb[i] : 0f;
                if (i < bandSliders.Length && bandSliders[i] != null) bandSliders[i].Value = gain;
                if (i < bandTexts.Length && bandTexts[i] != null) bandTexts[i].Text = FormatDb(gain);
            }

            if (sliderBassBoost != null) sliderBassBoost.Value = editingSettings.BassBoost * 100f;
            if (txtBassBoostVal != null) txtBassBoostVal.Text = $"{(int)(editingSettings.BassBoost * 100)}%";

            if (sliderSurround != null) sliderSurround.Value = editingSettings.Surround3D * 100f;
            if (txtSurroundVal != null) txtSurroundVal.Text = $"{(int)(editingSettings.Surround3D * 100)}%";

            if (sliderVocal != null) sliderVocal.Value = editingSettings.VocalClarity * 100f;
            if (txtVocalVal != null) txtVocalVal.Text = $"{(int)(editingSettings.VocalClarity * 100)}%";

            if (cboPresets != null)
            {
                cboPresets.SelectedItem = editingSettings.PresetName;
                UpdatePresetActionButtonsVisibility(editingSettings.PresetName);
            }
        }

        private bool IsTargetCurrentlyPlaying()
        {
            switch (activeScope)
            {
                case EqualizerScope.Global:
                    if (string.IsNullOrEmpty(currentSongPath)) return true;
                    settingsService.GetEffectiveEqualizer(currentSongPath, currentFolderPath, currentPlaylistId, out var effectiveScope);
                    return effectiveScope == EqualizerScope.Global;

                case EqualizerScope.Song:
                    var selSong = cboSongPicker?.SelectedItem as SongScopeItem;
                    string targetSongPath = selSong?.FilePath ?? currentSongPath;
                    return !string.IsNullOrEmpty(currentSongPath) && string.Equals(targetSongPath, currentSongPath, StringComparison.OrdinalIgnoreCase);

                case EqualizerScope.Folder:
                    var selFolder = cboFolderPicker?.SelectedItem as FolderScopeItem;
                    string targetFolder = selFolder?.FolderPath ?? currentFolderPath;
                    return !string.IsNullOrEmpty(currentFolderPath) && string.Equals(targetFolder, currentFolderPath, StringComparison.OrdinalIgnoreCase);

                case EqualizerScope.Playlist:
                    var selPl = cboPlaylistPicker?.SelectedItem as PlaylistScopeItem;
                    string targetPl = selPl?.Id ?? currentPlaylistId;
                    return !string.IsNullOrEmpty(currentPlaylistId) && string.Equals(targetPl, currentPlaylistId, StringComparison.OrdinalIgnoreCase);

                default:
                    return false;
            }
        }

        private void UpdatePresetActionButtonsVisibility(string presetName)
        {
            bool isCustom = settingsService?.CurrentSettings?.CustomPresets != null &&
                            settingsService.CurrentSettings.CustomPresets.Any(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
            if (btnRenamePreset != null) btnRenamePreset.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            if (btnDeletePreset != null) btnDeletePreset.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        private string FormatDb(float db)
        {
            if (Math.Abs(db) < 0.05f) return "0 dB";
            return (db > 0 ? "+" : "") + db.ToString("0.#") + " dB";
        }

        private void ApplyToAudioService()
        {
            if (editingSettings == null) return;
            SaveCurrentScopeSettings();

            // CHỈ can thiệp vào luồng âm thanh đang phát nếu đối tượng đang chỉnh CHÍNH LÀ đối tượng đang nghe!
            if (IsTargetCurrentlyPlaying())
            {
                audioPlayerService?.ApplyEqualizerSettings(editingSettings);
            }
            else
            {
                // Đảm bảo bài đang phát tiếp tục phát đúng EQ hiệu lực của nó!
                var effectiveEq = settingsService?.GetEffectiveEqualizer(currentSongPath, currentFolderPath, currentPlaylistId, out _);
                if (effectiveEq != null)
                {
                    audioPlayerService?.ApplyEqualizerSettings(effectiveEq);
                }
            }
        }

        private void SaveCurrentScopeSettings()
        {
            if (editingSettings == null || settingsService == null) return;

            switch (activeScope)
            {
                case EqualizerScope.Global:
                    settingsService.SaveEqualizer(EqualizerScope.Global, "", editingSettings);
                    break;
                case EqualizerScope.Song:
                    var selSong = cboSongPicker?.SelectedItem as SongScopeItem;
                    string songPathToSave = selSong?.FilePath ?? currentSongPath;
                    if (!string.IsNullOrEmpty(songPathToSave))
                    {
                        settingsService.SaveEqualizer(EqualizerScope.Song, songPathToSave, editingSettings);
                    }
                    break;
                case EqualizerScope.Folder:
                    var selFolder = cboFolderPicker?.SelectedItem as FolderScopeItem;
                    string folderPathToSave = selFolder?.FolderPath ?? currentFolderPath;
                    if (!string.IsNullOrEmpty(folderPathToSave))
                    {
                        settingsService.SaveEqualizer(EqualizerScope.Folder, folderPathToSave, editingSettings);
                    }
                    break;
                case EqualizerScope.Playlist:
                    var selPl = cboPlaylistPicker?.SelectedItem as PlaylistScopeItem;
                    string plIdToSave = selPl?.Id ?? currentPlaylistId;
                    if (!string.IsNullOrEmpty(plIdToSave))
                    {
                        settingsService.SaveEqualizer(EqualizerScope.Playlist, plIdToSave, editingSettings);
                    }
                    break;
            }
        }

        private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingUI || cboPresets == null || cboPresets.SelectedItem == null || editingSettings == null || settingsService == null) return;
            string selectedPresetName = cboPresets.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedPresetName)) return;

            UpdatePresetActionButtonsVisibility(selectedPresetName);

            // Nếu chọn một preset "(Tùy chỉnh)" mà chính là preset đang chỉnh thì không cần nạp lại từ mẫu gốc
            if (selectedPresetName.EndsWith("(Tùy chỉnh)") && string.Equals(selectedPresetName, editingSettings.PresetName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var allPresets = settingsService.GetAllPresets();
            var preset = allPresets?.FirstOrDefault(p => string.Equals(p.Name, selectedPresetName, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                isUpdatingUI = true;
                editingSettings.ApplyPreset(preset);
                SyncUIToSettings();
                isUpdatingUI = false;

                ApplyToAudioService();
                DrawSplineCurve();
            }
        }

        private void BtnRenamePreset_Click(object sender, RoutedEventArgs e)
        {
            string selectedPresetName = cboPresets?.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedPresetName)) return;

            var inputDialog = new RenameFolderDialog(selectedPresetName, "Preset Equalizer")
            {
                Owner = this,
                Title = "Đổi Tên Preset Equalizer"
            };

            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.NewFolderName))
            {
                string newName = inputDialog.NewFolderName.Trim();
                if (string.Equals(selectedPresetName, newName, StringComparison.OrdinalIgnoreCase)) return;

                bool success = settingsService.RenameCustomPreset(selectedPresetName, newName);
                if (success)
                {
                    LoadPresets();
                    if (cboPresets != null) cboPresets.SelectedItem = newName;
                    if (editingSettings != null) editingSettings.PresetName = newName;
                    UpdatePresetActionButtonsVisibility(newName);
                    SaveCurrentScopeSettings();
                }
            }
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            string selectedPresetName = cboPresets?.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedPresetName)) return;

            var res = MessageBox.Show($"Bạn có chắc chắn muốn xóa Preset \"{selectedPresetName}\" không?", "Xác nhận xóa Preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                settingsService.DeleteCustomPreset(selectedPresetName);
                LoadPresets();
                if (cboPresets != null) cboPresets.SelectedItem = "Flat";
            }
        }

        private void RbScope_Checked(object sender, RoutedEventArgs e)
        {
            if (isUpdatingUI) return;

            if (rbScopeGlobal?.IsChecked == true) SetScope(EqualizerScope.Global);
            else if (rbScopeSong?.IsChecked == true) SetScope(EqualizerScope.Song);
            else if (rbScopeFolder?.IsChecked == true) SetScope(EqualizerScope.Folder);
            else if (rbScopePlaylist?.IsChecked == true) SetScope(EqualizerScope.Playlist);
        }

        private void MarkAsCustomized()
        {
            if (isUpdatingUI || cboPresets == null || editingSettings == null || settingsService == null) return;

            string currentName = editingSettings.PresetName;
            if (string.IsNullOrEmpty(currentName)) currentName = cboPresets.SelectedItem as string ?? "Flat";

            // 1. Nếu đã mang hậu tố "(Tùy chỉnh)" thì không sinh thêm gì nữa
            if (currentName.EndsWith("(Tùy chỉnh)")) return;

            // 2. Nếu là Preset do người dùng tự tạo: KHÔNG sinh thêm Preset mới, cập nhật trực tiếp vào Preset đó
            bool isCustom = settingsService.CurrentSettings?.CustomPresets != null &&
                            settingsService.CurrentSettings.CustomPresets.Any(p => string.Equals(p.Name, currentName, StringComparison.OrdinalIgnoreCase));
            if (isCustom)
            {
                var customPreset = settingsService.CurrentSettings.CustomPresets.FirstOrDefault(p => string.Equals(p.Name, currentName, StringComparison.OrdinalIgnoreCase));
                if (customPreset != null)
                {
                    customPreset.PreampDb = editingSettings.PreampDb;
                    customPreset.Bands = (float[])editingSettings.BandGainsDb.Clone();
                    customPreset.BassBoost = editingSettings.BassBoost;
                    customPreset.Surround3D = editingSettings.Surround3D;
                    customPreset.VocalClarity = editingSettings.VocalClarity;
                    settingsService.SaveSettings();
                }
                return;
            }

            // 3. Nếu là Preset mặc định hệ thống (Flat, Pop, Rock...): sinh ra "[Tên] (Tùy chỉnh)"
            string customName = $"{currentName} (Tùy chỉnh)";
            editingSettings.PresetName = customName;

            isUpdatingUI = true;
            LoadPresets();
            cboPresets.SelectedItem = customName;
            UpdatePresetActionButtonsVisibility(customName);
            isUpdatingUI = false;
        }

        private void SliderPreamp_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingUI || editingSettings == null) return;
            float val = (float)e.NewValue;
            editingSettings.PreampDb = val;
            if (txtPreampVal != null) txtPreampVal.Text = FormatDb(val);
            MarkAsCustomized();
            if (IsTargetCurrentlyPlaying())
            {
                audioPlayerService?.UpdateEqualizerPreamp(val);
            }
            SaveCurrentScopeSettings();
            DrawSplineCurve();
        }

        private void SliderBand_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingUI || editingSettings == null || bandTexts == null) return;
            var slider = sender as Slider;
            if (slider == null || slider.Tag == null) return;

            if (int.TryParse(slider.Tag.ToString(), out int bandIdx) && bandIdx >= 0 && bandIdx < 10)
            {
                float val = (float)e.NewValue;
                if (editingSettings.BandGainsDb == null || editingSettings.BandGainsDb.Length != 10) editingSettings.BandGainsDb = new float[10];
                editingSettings.BandGainsDb[bandIdx] = val;
                if (bandIdx < bandTexts.Length && bandTexts[bandIdx] != null) bandTexts[bandIdx].Text = FormatDb(val);
                
                MarkAsCustomized();

                if (IsTargetCurrentlyPlaying())
                {
                    audioPlayerService?.UpdateEqualizerBand(bandIdx, val);
                }

                SaveCurrentScopeSettings();
                DrawSplineCurve();
            }
        }

        private void SliderBassBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingUI || editingSettings == null) return;
            float val = (float)(e.NewValue / 100.0);
            editingSettings.BassBoost = val;
            if (txtBassBoostVal != null) txtBassBoostVal.Text = $"{(int)e.NewValue}%";
            MarkAsCustomized();
            if (IsTargetCurrentlyPlaying())
            {
                audioPlayerService?.UpdateBassBoost(val);
            }
            SaveCurrentScopeSettings();
        }

        private void SliderSurround_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingUI || editingSettings == null) return;
            float val = (float)(e.NewValue / 100.0);
            editingSettings.Surround3D = val;
            if (txtSurroundVal != null) txtSurroundVal.Text = $"{(int)e.NewValue}%";
            MarkAsCustomized();
            if (IsTargetCurrentlyPlaying())
            {
                audioPlayerService?.UpdateSurround3D(val);
            }
            SaveCurrentScopeSettings();
        }

        private void SliderVocal_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingUI || editingSettings == null) return;
            float val = (float)(e.NewValue / 100.0);
            editingSettings.VocalClarity = val;
            if (txtVocalVal != null) txtVocalVal.Text = $"{(int)e.NewValue}%";
            MarkAsCustomized();
            if (IsTargetCurrentlyPlaying())
            {
                audioPlayerService?.UpdateVocalClarity(val);
            }
            SaveCurrentScopeSettings();
        }

        private void CanvasCurve_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawSplineCurve();
        }

        private void DrawSplineCurve()
        {
            try
            {
                if (canvasCurve == null || canvasCurve.ActualWidth <= 10 || canvasCurve.ActualHeight <= 10 || editingSettings == null) return;

                canvasCurve.Children.Clear();

                double width = canvasCurve.ActualWidth;
                double height = canvasCurve.ActualHeight;
                double midY = height / 2.0;

                int count = 10;
                Point[] points = new Point[count];

                for (int i = 0; i < count; i++)
                {
                    double x = (width / (count - 1)) * i;
                    float gain = (editingSettings.BandGainsDb != null && i < editingSettings.BandGainsDb.Length)
                        ? editingSettings.BandGainsDb[i]
                        : 0f;

                    // Scale gain from -12dB..+12dB to height
                    double y = midY - (gain / 12.0) * (midY - 8);
                    points[i] = new Point(x, Math.Clamp(y, 4, height - 4));
                }

                // Create Bézier Spline Path
                PathFigure figure = new PathFigure { StartPoint = points[0], IsClosed = false };
                PathFigure fillFigure = new PathFigure { StartPoint = new Point(points[0].X, height), IsClosed = true };
                fillFigure.Segments.Add(new LineSegment(points[0], false));

                for (int i = 0; i < count - 1; i++)
                {
                    Point p0 = i > 0 ? points[i - 1] : points[i];
                    Point p1 = points[i];
                    Point p2 = points[i + 1];
                    Point p3 = i < count - 2 ? points[i + 2] : p2;

                    Point cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                    Point cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

                    var bezier = new BezierSegment(cp1, cp2, p2, true);
                    figure.Segments.Add(bezier);
                    fillFigure.Segments.Add(bezier);
                }

                fillFigure.Segments.Add(new LineSegment(new Point(points[count - 1].X, height), false));

                // Gradient Fill under curve
                PathGeometry fillGeom = new PathGeometry();
                fillGeom.Figures.Add(fillFigure);

                var fillBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 59, 130, 246), 0.0));
                fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(10, 59, 130, 246), 1.0));

                Path fillPath = new Path
                {
                    Data = fillGeom,
                    Fill = fillBrush
                };
                canvasCurve.Children.Add(fillPath);

                Brush accentBrush = (Brush)Application.Current.TryFindResource("AccentPrimary") 
                    ?? (Brush)this.TryFindResource("AccentPrimary") 
                    ?? new SolidColorBrush(Color.FromRgb(59, 130, 246));

                // Curve stroke
                PathGeometry curveGeom = new PathGeometry();
                curveGeom.Figures.Add(figure);

                Path strokePath = new Path
                {
                    Data = curveGeom,
                    Stroke = accentBrush,
                    StrokeThickness = 2.5,
                    StrokeLineJoin = PenLineJoin.Round
                };
                canvasCurve.Children.Add(strokePath);

                // Draw circular dots at each frequency point
                for (int i = 0; i < count; i++)
                {
                    Ellipse dot = new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = accentBrush,
                        Stroke = Brushes.White,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(dot, points[i].X - 3);
                    Canvas.SetTop(dot, points[i].Y - 3);
                    canvasCurve.Children.Add(dot);
                }
            }
            catch { }
        }

        private void BtnResetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (editingSettings == null || settingsService == null) return;
            string presetName = cboPresets?.SelectedItem as string;
            if (string.IsNullOrEmpty(presetName)) presetName = editingSettings.PresetName ?? "Flat";

            isUpdatingUI = true;

            // 1. Kiểm tra xem có phải là Preset mặc định hệ thống (hoặc bản Tùy chỉnh của Preset mặc định) không
            string baseName = presetName.Replace("(Tùy chỉnh)", "").Trim();
            var builtIn = EqualizerPreset.GetBuiltInPresets().FirstOrDefault(p => string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase));

            if (builtIn != null)
            {
                // Khôi phục về đúng thông số chuẩn gốc của Preset mặc định đó (Pop, Rock, EDM, Flat...)
                editingSettings.ApplyPreset(builtIn);
                editingSettings.PresetName = builtIn.Name;
                LoadPresets();
                if (cboPresets != null) cboPresets.SelectedItem = builtIn.Name;
            }
            else
            {
                // Đối với Preset do người dùng tự tạo: Đặt lại toàn bộ 10 cần faders và DSP về 0
                editingSettings.PreampDb = 0f;
                editingSettings.BandGainsDb = new float[10];
                editingSettings.BassBoost = 0f;
                editingSettings.Surround3D = 0f;
                editingSettings.VocalClarity = 0f;

                if (settingsService.CurrentSettings?.CustomPresets != null)
                {
                    var customPreset = settingsService.CurrentSettings.CustomPresets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
                    if (customPreset != null)
                    {
                        customPreset.PreampDb = 0f;
                        customPreset.Bands = new float[10];
                        customPreset.BassBoost = 0f;
                        customPreset.Surround3D = 0f;
                        customPreset.VocalClarity = 0f;
                        settingsService.SaveSettings();
                    }
                }
            }

            SyncUIToSettings();
            isUpdatingUI = false;

            ApplyToAudioService();
            DrawSplineCurve();
        }

        private void BtnCreatePreset_Click(object sender, RoutedEventArgs e)
        {
            if (editingSettings == null || settingsService == null) return;
            string defaultName = "Preset Mới";
            var inputDialog = new RenameFolderDialog(defaultName, "Cấu hình Equalizer")
            {
                Owner = this,
                Title = "Tạo Preset Equalizer Mới"
            };

            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.NewFolderName))
            {
                string presetName = inputDialog.NewFolderName.Trim();
                var newPreset = new EqualizerPreset(
                    presetName,
                    editingSettings.PreampDb,
                    (float[])editingSettings.BandGainsDb.Clone(),
                    editingSettings.BassBoost,
                    editingSettings.Surround3D,
                    editingSettings.VocalClarity,
                    isCustom: true
                );

                settingsService.SaveCustomPreset(newPreset);
                editingSettings.PresetName = presetName;

                isUpdatingUI = true;
                LoadPresets();
                if (cboPresets != null) cboPresets.SelectedItem = presetName;
                UpdatePresetActionButtonsVisibility(presetName);
                isUpdatingUI = false;

                SaveCurrentScopeSettings();
            }
        }

        private void BtnResetAllEq_Click(object sender, RoutedEventArgs e)
        {
            if (settingsService?.CurrentSettings == null) return;

            var res = MessageBox.Show(
                "Bạn có chắc chắn muốn xóa toàn bộ các Preset do bạn tự tạo và đặt lại toàn bộ Equalizer về Flat mặc định không?",
                "Xác nhận xóa tất cả Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                // Xóa toàn bộ preset tự tạo
                settingsService.CurrentSettings.CustomPresets?.Clear();

                // Reset all scope specific settings
                settingsService.CurrentSettings.SongEqualizers?.Clear();
                settingsService.CurrentSettings.FolderEqualizers?.Clear();
                settingsService.CurrentSettings.PlaylistEqualizers?.Clear();

                // Reset global equalizer to Flat
                var flat = new EqualizerPreset("Flat", 0f, new float[10], 0f, 0f, 0f);
                settingsService.CurrentSettings.GlobalEqualizer = new EqualizerSettings();
                settingsService.CurrentSettings.GlobalEqualizer.ApplyPreset(flat);

                // Save to appsettings.json
                settingsService.SaveSettings();

                // Reload presets and UI
                LoadPresets();
                if (cboPresets != null) cboPresets.SelectedItem = "Flat";
                SetScope(activeScope);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}

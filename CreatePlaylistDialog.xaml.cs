using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace FresAudio
{
    public class SelectableSongItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public string FolderName { get; set; }

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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class CreatePlaylistDialog : Window
    {
        public string PlaylistName => txtPlaylistName.Text.Trim();
        public List<string> SelectedSongPaths => _songList.Where(s => s.IsChecked).Select(s => s.FilePath).ToList();
        public Action<CreatePlaylistDialog>? OnConfirmed { get; set; }

        private ObservableCollection<SelectableSongItem> _songList = new ObservableCollection<SelectableSongItem>();
        private bool _isRename = false;
        private bool _isEdit = false;
        private string _defaultName = "";
        private List<string> _existingPlaylistNames = null;

        public CreatePlaylistDialog(List<SelectableSongItem> availableSongs = null, string defaultName = "", bool isRename = false, string initialSelectedSongPath = null, bool isEdit = false, List<string> existingPlaylistNames = null)
        {
            InitializeComponent();
            _isRename = isRename;
            _isEdit = isEdit;
            _defaultName = defaultName ?? "";
            _existingPlaylistNames = existingPlaylistNames;

            if (_isRename)
            {
                txtDialogTitle.Text = "ĐỔI TÊN PLAYLIST";
                btnConfirm.Content = "LƯU TÊN";
                iconHeader.Kind = PackIconKind.RenameBox;
                pnlSongSelection.Visibility = Visibility.Collapsed;
                this.Height = 220;
            }
            else
            {
                if (_isEdit)
                {
                    pnlPlaylistName.Visibility = Visibility.Collapsed;
                    txtDialogTitle.Text = !string.IsNullOrEmpty(defaultName) ? $"THÊM NHẠC: {defaultName.ToUpper()}" : "THÊM NHẠC VÀO PLAYLIST";
                    iconHeader.Kind = PackIconKind.PlaylistPlus;
                    this.Height = 490;
                }

                if (availableSongs != null && availableSongs.Count > 0)
                {
                    foreach (var s in availableSongs)
                    {
                        if (!string.IsNullOrEmpty(initialSelectedSongPath) && string.Equals(s.FilePath, initialSelectedSongPath, StringComparison.OrdinalIgnoreCase))
                        {
                            s.IsChecked = true;
                        }
                        _songList.Add(s);
                    }
                }
                lstSelectableSongs.ItemsSource = _songList;
                UpdateSelectedSummary();
            }

            if (!string.IsNullOrEmpty(defaultName))
            {
                txtPlaylistName.Text = defaultName;
                txtPlaylistName.SelectAll();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtPlaylistName.Focus();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TxtPlaylistName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool selectAll = chkSelectAll.IsChecked == true;
            foreach (var item in _songList)
            {
                item.IsChecked = selectAll;
            }
            UpdateSelectedSummary();
        }

        private void SongItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is SelectableSongItem song)
            {
                song.IsChecked = !song.IsChecked;
                UpdateSelectedSummary();
            }
        }

        private void SongCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectedSummary();
        }

        private void UpdateSelectedSummary()
        {
            int selectedCount = _songList.Count(s => s.IsChecked);
            txtSelectedSummary.Text = $"Đã chọn: {selectedCount} bài";
            if (!_isRename)
            {
                if (_isEdit)
                {
                    btnConfirm.Content = selectedCount > 0 ? $"LƯU PLAYLIST ({selectedCount})" : "LƯU PLAYLIST";
                }
                else
                {
                    btnConfirm.Content = selectedCount > 0 ? $"TẠO PLAYLIST ({selectedCount})" : "TẠO PLAYLIST";
                }
            }
            chkSelectAll.IsChecked = (_songList.Count > 0 && selectedCount == _songList.Count) ? true : (selectedCount > 0 ? (bool?)null : false);
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void Confirm()
        {
            if (!_isEdit)
            {
                string name = txtPlaylistName.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Vui lòng nhập tên playlist.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPlaylistName.Focus();
                    return;
                }

                if (string.Equals(name, "Tổng Playlist", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Tên \"Tổng Playlist\" là tên hệ thống, vui lòng chọn tên khác.", "Tên Không Hợp Lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPlaylistName.Focus();
                    txtPlaylistName.SelectAll();
                    return;
                }

                if (_existingPlaylistNames != null && _existingPlaylistNames.Any(n => !string.Equals(n, _defaultName, StringComparison.OrdinalIgnoreCase) && string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"Tên playlist \"{name}\" đã tồn tại, vui lòng chọn tên khác.", "Trùng Tên Playlist", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPlaylistName.Focus();
                    txtPlaylistName.SelectAll();
                    return;
                }
            }

            try { DialogResult = true; } catch { }
            OnConfirmed?.Invoke(this);
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try { DialogResult = false; } catch { }
            Close();
        }
    }
}

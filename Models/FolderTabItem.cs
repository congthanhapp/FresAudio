using System.ComponentModel;

namespace FresAudio.Models
{
    public class FolderTabItem : INotifyPropertyChanged
    {
        private string _folderPath;
        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (_folderPath != value)
                {
                    _folderPath = value;
                    OnPropertyChanged(nameof(FolderPath));
                    OnPropertyChanged(nameof(ToolTipText));
                }
            }
        }

        public bool IsAllTab { get; set; }
        public bool IsCustomPlaylist { get; set; }
        public string PlaylistId { get; set; }

        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(HeaderName));
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(ToolTipText));
                }
            }
        }

        private int _songCount;
        public int SongCount
        {
            get => _songCount;
            set
            {
                if (_songCount != value)
                {
                    _songCount = value;
                    OnPropertyChanged(nameof(SongCount));
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string HeaderName => IsAllTab ? (IsCustomPlaylist ? "Tổng Playlist" : "Tất cả") : Name;
        public string CountText => $"({SongCount})";
        public string DisplayName => IsAllTab ? (IsCustomPlaylist ? $"Tổng Playlist ({SongCount})" : $"Tất cả ({SongCount})") : $"{Name} ({SongCount})";

        public string ToolTipText => IsAllTab ? (IsCustomPlaylist ? $"Tổng số Playlist ({SongCount} playlist)" : $"Tất cả bài hát ({SongCount} bài)") : (IsCustomPlaylist ? $"Playlist: {Name}\n({SongCount} bài hát)" : $"{FolderPath}\n({SongCount} bài hát)");

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}

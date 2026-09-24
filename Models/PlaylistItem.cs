using System.ComponentModel;

namespace FresAudio.Models
{
    public class PlaylistItem : INotifyPropertyChanged
    {
        public int OriginalIndex { get; set; }
        public string DisplayName { get; set; }
        public string SearchString { get; set; }
        public string FilePath { get; set; }
        public string FolderPath { get; set; }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}


using System.Collections.Generic;

namespace FresAudio.Models
{
    public class AppSettings
    {
        public List<string> LastFolders { get; set; } = new List<string>();
        public HashSet<string> RemovedSongs { get; set; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        public bool IsDarkMode { get; set; } = true;
        public int ThemeMode { get; set; } = 1; // 0: Light, 1: Dark, 2: Transparent
        public bool IsShuffle { get; set; } = false;
        public int RepeatMode { get; set; } = 0; // 0: Off, 1: All, 2: One
        public List<CustomPlaylist> CustomPlaylists { get; set; } = new List<CustomPlaylist>();
        public EqualizerSettings GlobalEqualizer { get; set; } = new EqualizerSettings();
        public Dictionary<string, EqualizerSettings> SongEqualizers { get; set; } = new Dictionary<string, EqualizerSettings>(System.StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, EqualizerSettings> FolderEqualizers { get; set; } = new Dictionary<string, EqualizerSettings>(System.StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, EqualizerSettings> PlaylistEqualizers { get; set; } = new Dictionary<string, EqualizerSettings>(System.StringComparer.OrdinalIgnoreCase);
        public List<EqualizerPreset> CustomPresets { get; set; } = new List<EqualizerPreset>();
    }
}

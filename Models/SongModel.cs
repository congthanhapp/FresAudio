using System;

namespace FresAudio.Models
{
    public class SongModel
    {
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public TimeSpan Duration { get; set; }
        public string DisplayName { get; set; }

        public SongModel(string filePath, string displayName)
        {
            FilePath = filePath;
            DisplayName = displayName;
        }
    }
}

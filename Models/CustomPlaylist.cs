using System;
using System.Collections.Generic;

namespace FresAudio.Models
{
    public class CustomPlaylist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Playlist mới";
        public List<string> SongPaths { get; set; } = new List<string>();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FresAudio.Models;

namespace FresAudio.Services
{
    public class PlaylistService
    {
        private readonly SettingsService settingsService;
        
        public List<string> PlaylistPaths { get; private set; } = new List<string>();
        public List<string> DisplayPlaylist { get; private set; } = new List<string>();
        public List<string> PlaylistFolderOrigins { get; private set; } = new List<string>();
        public List<string> LoadedFolders { get; private set; } = new List<string>();
        
        public PlaylistService(SettingsService settingsService)
        {
            this.settingsService = settingsService;
        }

        public async Task<(List<string> paths, List<string> names)> LoadPlaylistAsync(string folder, List<string> explicitFiles = null, bool append = false, bool recurse = true, bool insertAtTop = false)
        {
            HashSet<string> existingPaths = null;
            if (append)
            {
                existingPaths = new HashSet<string>(PlaylistPaths, StringComparer.OrdinalIgnoreCase);
            }

            if (!append)
            {
                PlaylistPaths.Clear();
                DisplayPlaylist.Clear();
                PlaylistFolderOrigins.Clear();
                LoadedFolders.Clear();
            }

            if (explicitFiles == null && LoadedFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))) return (new List<string>(), new List<string>());
            
            var (newPaths, newDisplayNames, existingMatchedFiles) = await Task.Run(() =>
            {
                List<string> files;

                if (explicitFiles != null)
                {
                    files = explicitFiles;
                }
                else
                {
                    if (!recurse)
                    {
                        files = Directory.GetFiles(folder)
                            .Where(f => {
                                var ext = Path.GetExtension(f);
                                bool isAudio = ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                       ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                                       ext.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
                                       ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                                       ext.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
                                       ext.Equals(".wma", StringComparison.OrdinalIgnoreCase);
                                return isAudio && new FileInfo(f).Length > 100 * 1024;
                            }).ToList();
                    }
                    else
                    {
                        var enumerable = new System.IO.Enumeration.FileSystemEnumerable<string>(
                            folder,
                            (ref System.IO.Enumeration.FileSystemEntry entry) => entry.ToFullPath(),
                            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                        {
                            ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) =>
                            {
                                if (entry.IsDirectory) return false;
                                var ext = Path.GetExtension(entry.FileName);
                                bool isAudio = ext.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                       ext.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                       ext.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                                       ext.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                                       ext.EndsWith(".aac", StringComparison.OrdinalIgnoreCase) ||
                                       ext.EndsWith(".wma", StringComparison.OrdinalIgnoreCase);
                                return isAudio && entry.Length > 100 * 1024;
                            }
                        };
                        files = new List<string>();
                        foreach (var f in enumerable) files.Add(f);
                    }
                }

                var tempItems = new List<(string File, string DisplayName)>();
                var matchedExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    if (append && existingPaths != null && existingPaths.Contains(file))
                    {
                        matchedExisting.Add(file);
                        continue;
                    }

                    string displayName = Path.GetFileName(file);
                    try
                    {
                        using (var tfile = TagLib.File.Create(file))
                        {
                            string title = tfile.Tag.Title;
                            string artist = tfile.Tag.FirstPerformer;

                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                displayName = title;
                                if (!string.IsNullOrWhiteSpace(artist))
                                {
                                    displayName += " - " + artist;
                                }
                            }
                        }
                    }
                    catch { }

                    tempItems.Add((file, displayName));
                }

                tempItems = tempItems.OrderBy(x => x.DisplayName).ToList();

                var tempPaths = tempItems.Select(x => x.File).ToList();
                var tempNames = tempItems.Select(x => x.DisplayName).ToList();

                return (tempPaths, tempNames, matchedExisting);
            });

            // Cập nhật quyền sở hữu (FolderOrigin) cho các file đã tồn tại nhưng thuộc thư mục này
            if (existingMatchedFiles != null && existingMatchedFiles.Count > 0)
            {
                for (int i = 0; i < PlaylistPaths.Count; i++)
                {
                    if (existingMatchedFiles.Contains(PlaylistPaths[i]))
                    {
                        if (i < PlaylistFolderOrigins.Count)
                        {
                            PlaylistFolderOrigins[i] = folder;
                        }
                    }
                }
            }

            if (insertAtTop && append)
            {
                PlaylistPaths.InsertRange(0, newPaths);
                DisplayPlaylist.InsertRange(0, newDisplayNames);
                PlaylistFolderOrigins.InsertRange(0, Enumerable.Repeat(folder, newPaths.Count));
            }
            else
            {
                PlaylistPaths.AddRange(newPaths);
                DisplayPlaylist.AddRange(newDisplayNames);
                PlaylistFolderOrigins.AddRange(Enumerable.Repeat(folder, newPaths.Count));
            }
            
            if (!LoadedFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
            {
                LoadedFolders.Add(folder);
            }
            
            settingsService.AddLastFolder(folder);
            
            return (newPaths, newDisplayNames);
        }

        public void ClearPlaylist()
        {
            PlaylistPaths.Clear();
            DisplayPlaylist.Clear();
            PlaylistFolderOrigins.Clear();
            LoadedFolders.Clear();
            settingsService.ClearLastFolders();
        }

        public void RemoveFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;

            for (int i = PlaylistPaths.Count - 1; i >= 0; i--)
            {
                if (i < PlaylistFolderOrigins.Count && string.Equals(PlaylistFolderOrigins[i], folder, StringComparison.OrdinalIgnoreCase))
                {
                    PlaylistPaths.RemoveAt(i);
                    DisplayPlaylist.RemoveAt(i);
                    PlaylistFolderOrigins.RemoveAt(i);
                }
            }

            LoadedFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            settingsService.RemoveLastFolder(folder);
        }

        public void RemoveSong(int index)
        {
            if (index >= 0 && index < PlaylistPaths.Count)
            {
                PlaylistPaths.RemoveAt(index);
                DisplayPlaylist.RemoveAt(index);
                if (index < PlaylistFolderOrigins.Count)
                {
                    PlaylistFolderOrigins.RemoveAt(index);
                }
            }
        }
        
        public int GetSongIndex(string filePath)
        {
            return PlaylistPaths.FindIndex(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        }
    }
}

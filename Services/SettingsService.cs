using System;
using System.IO;
using System.Text.Json;
using FresAudio.Models;

namespace FresAudio.Services
{
    public class SettingsService
    {
        private readonly string settingsFilePath;

        public AppSettings CurrentSettings { get; private set; }

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "FresAudio");
            
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            
            settingsFilePath = Path.Combine(appFolder, "appsettings.json");
            CurrentSettings = LoadSettings();
        }

        public AppSettings LoadSettings()
        {
            AppSettings settings = null;
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    settings = JsonSerializer.Deserialize<AppSettings>(json);
                }
            }
            catch { }

            if (settings == null) settings = new AppSettings();

            // Khởi tạo và kiểm tra an toàn toàn bộ dữ liệu Equalizer
            if (settings.GlobalEqualizer == null) settings.GlobalEqualizer = new EqualizerSettings { IsEnabled = true };
            if (settings.GlobalEqualizer.BandGainsDb == null || settings.GlobalEqualizer.BandGainsDb.Length != 10)
            {
                var fixedBands = new float[10];
                if (settings.GlobalEqualizer.BandGainsDb != null)
                {
                    for (int i = 0; i < Math.Min(10, settings.GlobalEqualizer.BandGainsDb.Length); i++) fixedBands[i] = settings.GlobalEqualizer.BandGainsDb[i];
                }
                settings.GlobalEqualizer.BandGainsDb = fixedBands;
            }
            if (settings.SongEqualizers == null) settings.SongEqualizers = new Dictionary<string, EqualizerSettings>(StringComparer.OrdinalIgnoreCase);
            if (settings.FolderEqualizers == null) settings.FolderEqualizers = new Dictionary<string, EqualizerSettings>(StringComparer.OrdinalIgnoreCase);
            if (settings.PlaylistEqualizers == null) settings.PlaylistEqualizers = new Dictionary<string, EqualizerSettings>(StringComparer.OrdinalIgnoreCase);
            if (settings.CustomPresets == null) settings.CustomPresets = new List<EqualizerPreset>();

            return settings;
        }

        public void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, json);
            }
            catch { }
        }
        
        // Helper methods for easy access
        public void AddLastFolder(string folder)
        {
            if (!CurrentSettings.LastFolders.Contains(folder))
            {
                CurrentSettings.LastFolders.Add(folder);
                SaveSettings();
            }
        }
        
        public void RemoveLastFolder(string folder)
        {
            if (CurrentSettings.LastFolders.Remove(folder))
            {
                SaveSettings();
            }
        }

        public void ClearLastFolders()
        {
            CurrentSettings.LastFolders.Clear();
            SaveSettings();
        }
        
        // Equalizer helpers
        public EqualizerSettings GetEffectiveEqualizer(string songPath, string folderPath, string playlistId, out EqualizerScope effectiveScope)
        {
            // 1. Ưu tiên cấp 1: Từng bài hát
            if (!string.IsNullOrEmpty(songPath) && CurrentSettings.SongEqualizers != null && 
                CurrentSettings.SongEqualizers.TryGetValue(songPath, out var songEq) && songEq != null)
            {
                effectiveScope = EqualizerScope.Song;
                var eq = songEq.Clone();
                eq.Scope = EqualizerScope.Song;
                return eq;
            }

            // 2. Ưu tiên cấp 2: Playlist đang phát
            if (!string.IsNullOrEmpty(playlistId) && CurrentSettings.PlaylistEqualizers != null && 
                CurrentSettings.PlaylistEqualizers.TryGetValue(playlistId, out var plEq) && plEq != null)
            {
                effectiveScope = EqualizerScope.Playlist;
                var eq = plEq.Clone();
                eq.Scope = EqualizerScope.Playlist;
                return eq;
            }

            // 3. Ưu tiên cấp 3: Thư mục chứa bài hát
            if (!string.IsNullOrEmpty(folderPath) && CurrentSettings.FolderEqualizers != null && 
                CurrentSettings.FolderEqualizers.TryGetValue(folderPath, out var folderEq) && folderEq != null)
            {
                effectiveScope = EqualizerScope.Folder;
                var eq = folderEq.Clone();
                eq.Scope = EqualizerScope.Folder;
                return eq;
            }

            // 4. Mặc định cấp 4: Toàn app
            effectiveScope = EqualizerScope.Global;
            if (CurrentSettings.GlobalEqualizer == null)
            {
                CurrentSettings.GlobalEqualizer = new EqualizerSettings();
            }
            var globalEq = CurrentSettings.GlobalEqualizer.Clone();
            globalEq.Scope = EqualizerScope.Global;
            return globalEq;
        }

        public void SaveEqualizer(EqualizerScope scope, string targetKey, EqualizerSettings settings)
        {
            if (settings == null) return;
            var toSave = settings.Clone();
            toSave.Scope = scope;

            switch (scope)
            {
                case EqualizerScope.Global:
                    CurrentSettings.GlobalEqualizer = toSave;
                    break;
                case EqualizerScope.Song:
                    if (!string.IsNullOrEmpty(targetKey))
                    {
                        if (CurrentSettings.SongEqualizers == null) CurrentSettings.SongEqualizers = new Dictionary<string, EqualizerSettings>(StringComparer.OrdinalIgnoreCase);
                        CurrentSettings.SongEqualizers[targetKey] = toSave;
                    }
                    break;
                case EqualizerScope.Folder:
                    if (!string.IsNullOrEmpty(targetKey))
                    {
                        if (CurrentSettings.FolderEqualizers == null) CurrentSettings.FolderEqualizers = new Dictionary<string, EqualizerSettings>(StringComparer.OrdinalIgnoreCase);
                        CurrentSettings.FolderEqualizers[targetKey] = toSave;
                    }
                    break;
                case EqualizerScope.Playlist:
                    if (!string.IsNullOrEmpty(targetKey))
                    {
                        if (CurrentSettings.PlaylistEqualizers == null) CurrentSettings.PlaylistEqualizers = new Dictionary<string, EqualizerSettings>(StringComparer.OrdinalIgnoreCase);
                        CurrentSettings.PlaylistEqualizers[targetKey] = toSave;
                    }
                    break;
            }
            SaveSettings();
        }

        public void RemoveCustomEqualizer(EqualizerScope scope, string targetKey)
        {
            if (string.IsNullOrEmpty(targetKey)) return;

            switch (scope)
            {
                case EqualizerScope.Song:
                    if (CurrentSettings.SongEqualizers != null) CurrentSettings.SongEqualizers.Remove(targetKey);
                    break;
                case EqualizerScope.Folder:
                    if (CurrentSettings.FolderEqualizers != null) CurrentSettings.FolderEqualizers.Remove(targetKey);
                    break;
                case EqualizerScope.Playlist:
                    if (CurrentSettings.PlaylistEqualizers != null) CurrentSettings.PlaylistEqualizers.Remove(targetKey);
                    break;
            }
            SaveSettings();
        }

        public List<EqualizerPreset> GetAllPresets()
        {
            var list = EqualizerPreset.GetBuiltInPresets();
            if (CurrentSettings.CustomPresets != null)
            {
                list.AddRange(CurrentSettings.CustomPresets);
            }
            return list;
        }

        public void SaveCustomPreset(EqualizerPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name)) return;
            if (CurrentSettings.CustomPresets == null)
            {
                CurrentSettings.CustomPresets = new List<EqualizerPreset>();
            }

            preset.IsCustom = true;
            int idx = CurrentSettings.CustomPresets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                CurrentSettings.CustomPresets[idx] = preset;
            }
            else
            {
                CurrentSettings.CustomPresets.Add(preset);
            }
            SaveSettings();
        }

        public void DeleteCustomPreset(string presetName)
        {
            if (CurrentSettings.CustomPresets != null)
            {
                CurrentSettings.CustomPresets.RemoveAll(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
                SaveSettings();
            }
        }

        public bool RenameCustomPreset(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || CurrentSettings.CustomPresets == null) return false;
            newName = newName.Trim();
            var preset = CurrentSettings.CustomPresets.FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                preset.Name = newName;
                SaveSettings();
                return true;
            }
            return false;
        }
    }
}

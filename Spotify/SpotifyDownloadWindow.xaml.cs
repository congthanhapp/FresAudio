using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NAudio.Wave;

namespace FresAudio.Spotify
{
    public class SpotifyTrackItem
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string? Album { get; set; }
        public int? Year { get; set; }
        public string CoverUrl { get; set; } = string.Empty;
        public string DurationText { get; set; } = "--:--";
    }

    public partial class SpotifyDownloadWindow : Window
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private string _destinationFolder;
        public string DestinationFolder => _destinationFolder;
        private readonly List<string> _availableFoldersList = new List<string>();
        public string? DownloadedFilePath { get; private set; }
        public event EventHandler<string>? DownloadCompleted;

        private bool _isPlaylist = false;
        private string? _playlistTitle;
        private SpotifyTrackItem? _singleTrack;
        private readonly List<SpotifyTrackItem> _playlistItems = new List<SpotifyTrackItem>();
        private CancellationTokenSource? _downloadCts;

        static SpotifyDownloadWindow()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            }
        }

        public SpotifyDownloadWindow(string destinationFolder, IEnumerable<string>? availableFolders = null)
        {
            InitializeComponent();
            _destinationFolder = destinationFolder;

            InitializeFolderList(destinationFolder, availableFolders);

            txtStatus.Text = string.Empty;

            Loaded += (s, e) =>
            {
                txtUrl.Focus();
                CheckClipboardForSpotifyUrl();
            };
        }

        private void InitializeFolderList(string destinationFolder, IEnumerable<string>? availableFolders)
        {
            _availableFoldersList.Clear();
            if (availableFolders != null)
            {
                foreach (var f in availableFolders)
                {
                    if (!string.IsNullOrWhiteSpace(f) && !_availableFoldersList.Contains(f, StringComparer.OrdinalIgnoreCase))
                        _availableFoldersList.Add(f);
                }
            }

            if (!string.IsNullOrWhiteSpace(destinationFolder) && !_availableFoldersList.Contains(destinationFolder, StringComparer.OrdinalIgnoreCase))
            {
                _availableFoldersList.Insert(0, destinationFolder);
            }
            
            if (_availableFoldersList.Count == 0)
            {
                string defaultMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                _availableFoldersList.Add(defaultMusic);
            }

            _destinationFolder = !string.IsNullOrWhiteSpace(destinationFolder) ? destinationFolder : _availableFoldersList[0];

            cboDestinationFolder.ItemsSource = null;
            cboDestinationFolder.ItemsSource = _availableFoldersList;
            cboDestinationFolder.SelectedItem = _destinationFolder;
        }

        private void CheckClipboardForSpotifyUrl()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    if (Regex.IsMatch(text, @"spotify\.com\/(?:intl-[a-z]+\/)?(track|playlist|album)\/([a-zA-Z0-9]+)"))
                    {
                        txtUrl.Text = text;
                        txtUrl.SelectAll();
                        BtnFetch_Click(this, new RoutedEventArgs());
                    }
                }
            }
            catch { }
        }

        private void CboDestinationFolder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboDestinationFolder.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                _destinationFolder = selected;
            }
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new FolderBrowserWindow { Owner = this };
                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedFolderPath))
                {
                    string chosen = dialog.SelectedFolderPath;
                    if (!_availableFoldersList.Contains(chosen, StringComparer.OrdinalIgnoreCase))
                    {
                        _availableFoldersList.Add(chosen);
                        cboDestinationFolder.ItemsSource = null;
                        cboDestinationFolder.ItemsSource = _availableFoldersList;
                    }
                    cboDestinationFolder.SelectedItem = chosen;
                    _destinationFolder = chosen;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi chọn thư mục: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
            {
                _downloadCts.Cancel();
            }
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
            {
                _downloadCts.Cancel();
            }
            this.Close();
        }

        private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnFetch_Click(this, new RoutedEventArgs());
            }
        }

        private async void BtnFetch_Click(object sender, RoutedEventArgs e)
        {
            string input = txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            btnFetch.IsEnabled = false;
            btnDownload.IsEnabled = false;
            infoPanel.Visibility = Visibility.Collapsed;
            progressContainer.Visibility = Visibility.Visible;
            progressDownload.Value = 0;
            txtStatus.Visibility = Visibility.Visible;
            txtStatus.Text = "Đang kết nối đến Spotify...";

            _isPlaylist = false;
            _playlistTitle = null;
            _singleTrack = null;
            _playlistItems.Clear();

            try
            {
                var spotifyMatch = Regex.Match(input, @"spotify\.com\/(?:intl-[a-z]+\/)?(track|playlist|album)\/([a-zA-Z0-9]+)");

                if (spotifyMatch.Success)
                {
                    string type = spotifyMatch.Groups[1].Value.ToLowerInvariant();
                    string id = spotifyMatch.Groups[2].Value;

                    if (type == "playlist" || type == "album")
                    {
                        await FetchSpotifyPlaylistOrAlbumAsync(type, id);
                    }
                    else
                    {
                        await FetchSpotifyTrackAsync(id, input);
                    }
                }
                else
                {
                    throw new Exception("Vui lòng dán đúng đường dẫn (link) bài hát, album hoặc playlist từ Spotify (ví dụ: https://open.spotify.com/track/...).");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể lấy thông tin Spotify: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Visibility = Visibility.Collapsed;
            }
            finally
            {
                btnFetch.IsEnabled = true;
            }
        }

        private async Task FetchSpotifyTrackAsync(string trackId, string originalUrl)
        {
            string embedUrl = $"https://open.spotify.com/embed/track/{trackId}";
            using var req = new HttpRequestMessage(HttpMethod.Get, embedUrl);
            var res = await _httpClient.SendAsync(req);
            var html = await res.Content.ReadAsStringAsync();

            string title = "Bài hát Spotify";
            string artist = "Spotify Artist";
            string coverUrl = "";
            string durationText = "--:--";
            string? albumName = null;
            int? releaseYear = null;

            var match = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline);
            if (match.Success)
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                var props = doc.RootElement.GetProperty("props").GetProperty("pageProps");
                if (props.TryGetProperty("state", out var state) && state.TryGetProperty("data", out var data) && data.TryGetProperty("entity", out var entity))
                {
                    title = entity.TryGetProperty("name", out var n) ? n.GetString() ?? "" : (entity.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "Spotify Track");
                    if (entity.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                    {
                        var artistNames = new List<string>();
                        foreach (var a in artists.EnumerateArray()) if (a.TryGetProperty("name", out var an)) artistNames.Add(an.GetString() ?? "");
                        if (artistNames.Count > 0) artist = string.Join(", ", artistNames);
                    }
                    if (entity.TryGetProperty("duration", out var dur) && dur.TryGetInt64(out long durMs))
                    {
                        var ts = TimeSpan.FromMilliseconds(durMs);
                        durationText = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
                    }
                    if (entity.TryGetProperty("releaseDate", out var rd) && rd.TryGetProperty("isoString", out var iso))
                    {
                        if (DateTime.TryParse(iso.GetString(), out var dt)) releaseYear = dt.Year;
                    }
                    if (entity.TryGetProperty("visualIdentity", out var vi) && vi.TryGetProperty("image", out var images) && images.ValueKind == JsonValueKind.Array)
                    {
                        string bestImg = "";
                        int maxW = 0;
                        foreach (var img in images.EnumerateArray())
                        {
                            int w = img.TryGetProperty("maxWidth", out var mw) ? mw.GetInt32() : 0;
                            string u = img.TryGetProperty("url", out var ur) ? ur.GetString() ?? "" : "";
                            if (w >= maxW && !string.IsNullOrEmpty(u)) { maxW = w; bestImg = u; }
                        }
                        if (!string.IsNullOrEmpty(bestImg)) coverUrl = bestImg;
                    }
                }
            }

            if (string.IsNullOrEmpty(coverUrl) || title == "Bài hát Spotify")
            {
                try
                {
                    string oembedUrl = $"https://open.spotify.com/oembed?url={Uri.EscapeDataString(originalUrl)}";
                    using var oReq = new HttpRequestMessage(HttpMethod.Get, oembedUrl);
                    var oRes = await _httpClient.SendAsync(oReq);
                    var oJson = await oRes.Content.ReadAsStringAsync();
                    using var oDoc = JsonDocument.Parse(oJson);
                    if (oDoc.RootElement.TryGetProperty("title", out var ot)) title = ot.GetString() ?? title;
                    if (oDoc.RootElement.TryGetProperty("thumbnail_url", out var othumb)) coverUrl = othumb.GetString() ?? coverUrl;
                }
                catch { }
            }

            _singleTrack = new SpotifyTrackItem
            {
                Title = title,
                Artist = artist,
                CoverUrl = coverUrl,
                Album = albumName,
                Year = releaseYear,
                DurationText = durationText
            };

            txtTitle.Text = title;
            txtChannel.Text = artist;
            txtDuration.Text = durationText;

            if (!string.IsNullOrEmpty(coverUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(coverUrl, UriKind.Absolute);
                    bitmap.EndInit();
                    imgThumbnail.Source = bitmap;
                }
                catch { imgThumbnail.Source = null; }
            }
            else imgThumbnail.Source = null;

            infoPanel.Visibility = Visibility.Visible;
            btnDownload.IsEnabled = true;
            txtStatus.Visibility = Visibility.Collapsed;
        }

        private async Task FetchSpotifyPlaylistOrAlbumAsync(string type, string id)
        {
            string embedUrl = $"https://open.spotify.com/embed/{type}/{id}";
            using var req = new HttpRequestMessage(HttpMethod.Get, embedUrl);
            var res = await _httpClient.SendAsync(req);
            var html = await res.Content.ReadAsStringAsync();

            string playlistTitle = type == "album" ? "Spotify Album" : "Spotify Playlist";
            string playlistAuthor = "Spotify";
            string playlistCoverUrl = "";

            var match = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline);
            if (!match.Success) throw new Exception("Không thể đọc danh sách từ Spotify.");

            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            var props = doc.RootElement.GetProperty("props").GetProperty("pageProps");
            if (!props.TryGetProperty("state", out var state) || !state.TryGetProperty("data", out var data) || !data.TryGetProperty("entity", out var entity)) throw new Exception("Dữ liệu không hợp lệ.");

            playlistTitle = entity.TryGetProperty("name", out var n) ? n.GetString() ?? "" : (entity.TryGetProperty("title", out var t) ? t.GetString() ?? "" : playlistTitle);
            playlistAuthor = entity.TryGetProperty("subtitle", out var st) ? st.GetString() ?? "" : (entity.TryGetProperty("authors", out var au) ? au.GetString() ?? "Spotify" : "Spotify");

            if (entity.TryGetProperty("visualIdentity", out var vi) && vi.TryGetProperty("image", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in images.EnumerateArray()) if (img.TryGetProperty("url", out var u)) playlistCoverUrl = u.GetString() ?? "";
            }
            else if (entity.TryGetProperty("coverArt", out var ca) && ca.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (var src in sources.EnumerateArray()) if (src.TryGetProperty("url", out var u)) playlistCoverUrl = u.GetString() ?? "";
            }

            if (entity.TryGetProperty("trackList", out var trackList) && trackList.ValueKind == JsonValueKind.Array)
            {
                foreach (var tr in trackList.EnumerateArray())
                {
                    string trTitle = tr.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                    string trArtist = tr.TryGetProperty("subtitle", out var sub) ? sub.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(trTitle)) continue;
                    string durText = "--:--";
                    if (tr.TryGetProperty("duration", out var dur) && dur.TryGetInt64(out long durMs))
                    {
                        var ts = TimeSpan.FromMilliseconds(durMs);
                        durText = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
                    }
                    _playlistItems.Add(new SpotifyTrackItem
                    {
                        Title = trTitle, Artist = trArtist, DurationText = durText,
                        CoverUrl = playlistCoverUrl, Album = type == "album" ? playlistTitle : null
                    });
                }
            }

            _isPlaylist = true;
            _playlistTitle = playlistTitle;
            txtTitle.Text = playlistTitle;
            txtChannel.Text = $"{playlistAuthor} • {_playlistItems.Count} bài hát";
            txtDuration.Text = $"{(type == "album" ? "Album" : "Playlist")} ({_playlistItems.Count} bài)";
            
            if (!string.IsNullOrEmpty(playlistCoverUrl))
            {
                try {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.UriSource = new Uri(playlistCoverUrl, UriKind.Absolute); bitmap.EndInit();
                    imgThumbnail.Source = bitmap;
                } catch { imgThumbnail.Source = null; }
            } else imgThumbnail.Source = null;

            infoPanel.Visibility = Visibility.Visible;
            btnDownload.IsEnabled = true;
            txtStatus.Visibility = Visibility.Collapsed;
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_singleTrack == null && (!_isPlaylist || _playlistItems.Count == 0)) return;

            btnDownload.IsEnabled = false;
            btnFetch.IsEnabled = false;
            txtUrl.IsEnabled = false;
            progressDownload.Value = 0;
            txtStatus.Visibility = Visibility.Visible;

            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            try
            {
                if (_isPlaylist && _playlistItems.Count > 0)
                {
                    int total = _playlistItems.Count;
                    int successCount = 0;
                    for (int i = 0; i < total; i++)
                    {
                        if (token.IsCancellationRequested) break;
                        var item = _playlistItems[i];
                        txtStatus.Text = $"[{i + 1}/{total}] Đang tải: {item.Title}...";
                        AnimateProgress((double)i / total * 100.0);
                        try
                        {
                            string safeFileName = GetSafeFileName(item.Title, item.Artist);
                            string finalFile = Path.Combine(_destinationFolder, safeFileName + ".mp3");
                            if (File.Exists(finalFile) && new FileInfo(finalFile).Length > 1024)
                            {
                                successCount++;
                                continue;
                            }
                            await DownloadSingleTrackAsync(item.Title, item.Artist, item.CoverUrl, item.Album, item.Year, token, false);
                            successCount++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                    AnimateProgress(100);
                    txtStatus.Text = $"Đã hoàn tất tải {successCount}/{total} bài hát!";
                }
                else if (_singleTrack != null)
                {
                    await DownloadSingleTrackAsync(_singleTrack.Title, _singleTrack.Artist, _singleTrack.CoverUrl, _singleTrack.Album, _singleTrack.Year, token, true);
                    AnimateProgress(100);
                    txtStatus.Text = $"Đã tải xong '{_singleTrack.Title}'!";
                }
            }
            catch (OperationCanceledException) { txtStatus.Text = "Đã hủy quá trình tải."; }
            catch (Exception ex) { MessageBox.Show("Lỗi tải: " + ex.Message); }
            finally
            {
                btnDownload.IsEnabled = true; btnFetch.IsEnabled = true; txtUrl.IsEnabled = true;
            }
        }

        private static string GetSafeFileName(string title, string artist)
        {
            string combined = !string.IsNullOrEmpty(artist) ? $"{artist} - {title}" : title;
            return string.Join("_", combined.Split(Path.GetInvalidFileNameChars()));
        }

        private static async Task<string> EnsureYtDlpAsync()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string toolsDir = Path.Combine(appDir, "Tools");
            string ytdlPath = Path.Combine(toolsDir, "yt-dlp.exe");
            if (File.Exists(ytdlPath)) return ytdlPath;
            Directory.CreateDirectory(toolsDir);
            using var client = new HttpClient();
            using var s = await client.GetStreamAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
            using var fs = new FileStream(ytdlPath, FileMode.Create);
            await s.CopyToAsync(fs);
            return ytdlPath;
        }

        private void NormalizeAndEncodeToMp3(string inputFile, string outputFile, int bitrate = 320000)
        {
            try
            {
                using var reader = new AudioFileReader(inputFile);
                float maxPeak = 0.0f;
                float[] buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++) { float abs = Math.Abs(buffer[i]); if (abs > maxPeak) maxPeak = abs; }
                }
                reader.Position = 0;
                float gain = (maxPeak > 0.0001f) ? (0.98f / maxPeak) : 1.0f;
                ISampleProvider provider = (Math.Abs(gain - 1.0f) > 0.02f) ? new NAudio.Wave.SampleProviders.VolumeSampleProvider(reader) { Volume = gain } : (ISampleProvider)reader;
                MediaFoundationEncoder.EncodeToMp3(provider.ToWaveProvider(), outputFile, bitrate);
            }
            catch { try { File.Copy(inputFile, outputFile, true); } catch { } }
        }

        private async Task DownloadSingleTrackAsync(string title, string artist, string coverUrl, string? albumName, int? year, CancellationToken token = default, bool updateUi = true)
        {
            string safeFileName = GetSafeFileName(title, artist);
            string finalFile = Path.Combine(_destinationFolder, safeFileName + ".mp3");
            string downloadedMediaFile = "";

            token.ThrowIfCancellationRequested();

            if (updateUi)
            {
                AnimateProgress(20);
                txtStatus.Text = $"Đang tải luồng âm thanh cho '{title}'...";
            }

            string searchQuery = !string.IsNullOrEmpty(artist) ? $"{artist} - {title}" : title;
            string ytdlPath = await EnsureYtDlpAsync();
            var ytdl = new YoutubeDLSharp.YoutubeDL
            {
                YoutubeDLPath = ytdlPath,
                OutputFolder = Path.GetTempPath(),
                OutputFileTemplate = $"fres_spot_{DateTime.Now.Ticks}.%(ext)s"
            };

            string[] searchQueries = new[]
            {
                $"ytsearch1:{searchQuery} Official Audio",
                $"ytsearch1:{searchQuery} Audio",
                $"ytsearch1:{searchQuery}"
            };

            string[] clients = new[] { "android", "mweb", "web_creator", "tvhtml5_simply_embedded_player" };

            foreach (var sq in searchQueries)
            {
                token.ThrowIfCancellationRequested();
                foreach (var cl in clients)
                {
                    token.ThrowIfCancellationRequested();
                    var opt = new YoutubeDLSharp.Options.OptionSet
                    {
                        Format = "bestaudio/best",
                        ExtractorArgs = $"youtube:player_client={cl}"
                    };

                    var res = await ytdl.RunVideoDownload(sq, overrideOptions: opt, ct: token);
                    if (res.Success && !string.IsNullOrEmpty(res.Data) && File.Exists(res.Data))
                    {
                        downloadedMediaFile = res.Data;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(downloadedMediaFile) && File.Exists(downloadedMediaFile))
                {
                    break;
                }
            }

            token.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(downloadedMediaFile) || !File.Exists(downloadedMediaFile))
            {
                throw new Exception("Không thể tìm thấy luồng âm thanh bài hát này.");
            }

            if (updateUi)
            {
                AnimateProgress(70);
                txtStatus.Text = "Đang chuẩn hóa âm lượng & xuất MP3 320 kbps...";
            }

            await Task.Run(() =>
            {
                NormalizeAndEncodeToMp3(downloadedMediaFile, finalFile, 320000);
            }, token);

            try { File.Delete(downloadedMediaFile); } catch { }

            token.ThrowIfCancellationRequested();

            if (updateUi)
            {
                AnimateProgress(85);
                txtStatus.Text = "Đang gắn thẻ thông tin & ảnh bìa Spotify...";
            }

            await Task.Run(async () =>
            {
                try
                {
                    var tfile = TagLib.File.Create(finalFile);
                    tfile.Tag.Title = title.Trim();
                    
                    if (!string.IsNullOrEmpty(artist))
                    {
                        tfile.Tag.Performers = new[] { artist.Trim() };
                        tfile.Tag.AlbumArtists = new[] { artist.Trim() };
                    }
                    if (!string.IsNullOrEmpty(albumName))
                    {
                        tfile.Tag.Album = albumName.Trim();
                    }
                    if (year.HasValue && year.Value > 1900)
                    {
                        tfile.Tag.Year = (uint)year.Value;
                    }

                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        using var httpClient = new HttpClient();
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                        var imageBytes = await httpClient.GetByteArrayAsync(coverUrl, token);
                        var picture = new TagLib.Picture(new TagLib.ByteVector(imageBytes))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            Description = "Artwork",
                            MimeType = "image/jpeg"
                        };
                        tfile.Tag.Pictures = new TagLib.IPicture[] { picture };
                    }

                    tfile.Save();
                }
                catch { }
            }, token);

            DownloadedFilePath = finalFile;
            DownloadCompleted?.Invoke(this, finalFile);
        }

        private void AnimateProgress(double value)
        {
            Dispatcher.Invoke(() =>
            {
                var anim = new DoubleAnimation(value, TimeSpan.FromMilliseconds(250));
                progressDownload.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
            });
        }
    }
}

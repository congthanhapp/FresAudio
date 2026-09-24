using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NAudio.Wave;

namespace FresAudio.Soundcloud
{
    public partial class SoundcloudDownloadWindow : Window
    {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });

        private static string _cachedClientId = "UMY1dzQ68n2QbCuypNe8JOivmV2FO2Ep";
        private SoundcloudTrackInfo? _currentTrack;
        private string _destinationFolder;
        public string DestinationFolder => _destinationFolder;
        private readonly List<string> _availableFoldersList = new List<string>();
        public event EventHandler<string>? DownloadCompleted;
        public string? DownloadedFilePath { get; private set; }
        private string? _currentSearchInput;
        private int _currentSearchOffset = 0;
        private bool _canLoadMoreSoundcloud = false;
        public ObservableCollection<SoundcloudTrackInfo> SearchResults { get; } = new ObservableCollection<SoundcloudTrackInfo>();

        public class SoundcloudTrackInfo
        {
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public int DurationMs { get; set; }
            public string DurationText { get; set; } = string.Empty;
            public string CoverUrl { get; set; } = string.Empty;
            public string StreamUrl { get; set; } = string.Empty;
            public string TranscodingUrl { get; set; } = string.Empty;
            public string PermalinkUrl { get; set; } = string.Empty;
        }

        static SoundcloudDownloadWindow()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
        }

        public SoundcloudDownloadWindow(string destinationFolder, IEnumerable<string>? availableFolders = null)
        {
            InitializeComponent();
            _destinationFolder = destinationFolder;

            InitializeFolderList(destinationFolder, availableFolders);

            lstSearchResults.ItemsSource = SearchResults;
            txtStatus.Text = string.Empty;
            progressContainer.Visibility = Visibility.Collapsed;

            Loaded += (s, e) =>
            {
                txtUrl.Focus();
                CheckClipboardForSoundcloudUrl();
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

        private void CheckClipboardForSoundcloudUrl()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    if (text.Contains("soundcloud.com", StringComparison.OrdinalIgnoreCase))
                    {
                        txtUrl.Text = text;
                        txtUrl.CaretIndex = text.Length;
                        BtnFetch_Click(this, new RoutedEventArgs());
                    }
                }
            }
            catch { }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnFetch_Click(this, new RoutedEventArgs());
            }
        }

        private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtUrl.Text) && infoPanel.Visibility == Visibility.Collapsed && searchPanel.Visibility == Visibility.Collapsed)
            {
                welcomePanel.Visibility = Visibility.Visible;
                progressContainer.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string query)
            {
                txtUrl.Text = query;
                BtnFetch_Click(this, new RoutedEventArgs());
            }
        }

        private void SearchResultsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                double newOffset = scrollViewer.VerticalOffset - (e.Delta / 3.0);
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableHeight)));
                e.Handled = true;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void AnimateProgress(double toValue)
        {
            Dispatcher.Invoke(() =>
            {
                var animation = new DoubleAnimation
                {
                    To = toValue,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                progressDownload.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, animation);
            });
        }

        private async Task<string> EnsureClientIdAsync()
        {
            if (!string.IsNullOrEmpty(_cachedClientId))
            {
                return _cachedClientId;
            }

            try
            {
                var html = await _httpClient.GetStringAsync("https://soundcloud.com/");
                var match = Regex.Match(html, @"window\.__sc_hydration\s*=\s*(\[.*?\]);</script>", RegexOptions.Singleline);
                if (match.Success)
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value);
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("hydratable", out var hydratable) && hydratable.GetString() == "apiClient")
                        {
                            _cachedClientId = item.GetProperty("data").GetProperty("id").GetString() ?? "";
                            return _cachedClientId;
                        }
                    }
                }
            }
            catch { }

            _cachedClientId = "UMY1dzQ68n2QbCuypNe8JOivmV2FO2Ep";
            return _cachedClientId;
        }

        private async void BtnFetch_Click(object sender, RoutedEventArgs e)
        {
            string input = txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                txtStatus.Visibility = Visibility.Visible;
                txtStatus.Text = "Vui lòng nhập tên bài hát hoặc đường dẫn SoundCloud.";
                return;
            }

            btnFetch.IsEnabled = false;
            btnDownload.Visibility = Visibility.Collapsed;
            btnDownload.IsEnabled = false;
            btnLoadMore.Visibility = Visibility.Collapsed;
            scrollSearchResults.ScrollToTop();
            welcomePanel.Visibility = Visibility.Collapsed;
            infoPanel.Visibility = Visibility.Collapsed;
            searchPanel.Visibility = Visibility.Collapsed;
            progressContainer.Visibility = Visibility.Visible;
            progressDownload.Value = 0;
            txtStatus.Visibility = Visibility.Visible;
            txtStatus.Text = "Đang tìm kiếm thông tin SoundCloud...";

            SearchResults.Clear();
            _currentTrack = null;
            _currentSearchInput = null;
            _currentSearchOffset = 0;

            bool isUrl = input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         input.Contains("soundcloud.com", StringComparison.OrdinalIgnoreCase);

            try
            {
                if (isUrl)
                {
                    _currentTrack = await FetchSoundcloudTrackAsync(input);

                    txtTitle.Text = _currentTrack.Title;
                    txtChannel.Text = _currentTrack.Artist;
                    txtDuration.Text = _currentTrack.DurationText;

                    if (!string.IsNullOrEmpty(_currentTrack.CoverUrl))
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(_currentTrack.CoverUrl, UriKind.Absolute);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            imgThumbnail.Source = bitmap;
                        }
                        catch
                        {
                            imgThumbnail.Source = null;
                        }
                    }
                    else
                    {
                        imgThumbnail.Source = null;
                    }

                    infoPanel.Visibility = Visibility.Visible;
                    btnDownload.Visibility = Visibility.Visible;
                    btnDownload.IsEnabled = true;
                    txtStatus.Text = "Tìm thấy bài hát! Nhấn nút 'TẢI XUỐNG MP3' bên dưới để tải.";
                }
                else
                {
                    // Keyword Search
                    txtStatus.Text = $"Đang tìm kiếm '{input}' trên SoundCloud...";
                    _currentSearchInput = input;
                    _currentSearchOffset = 0;

                    await LoadMoreSoundcloudResultsAsync(20);

                    if (SearchResults.Count > 0)
                    {
                        searchPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        welcomePanel.Visibility = Visibility.Visible;
                        txtStatus.Text = "Không tìm thấy kết quả phù hợp trên SoundCloud.";
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Lỗi: {ex.Message}";
                welcomePanel.Visibility = Visibility.Visible;
            }
            finally
            {
                btnFetch.IsEnabled = true;
            }
        }

        private async Task<bool> LoadMoreSoundcloudResultsAsync(int limit = 20)
        {
            if (string.IsNullOrEmpty(_currentSearchInput)) return false;

            string clientId = await EnsureClientIdAsync();
            string searchUrl = $"https://api-v2.soundcloud.com/search/tracks?q={Uri.EscapeDataString(_currentSearchInput)}&client_id={clientId}&limit={limit}&offset={_currentSearchOffset}";
            var json = await _httpClient.GetStringAsync(searchUrl);
            using var doc = JsonDocument.Parse(json);

            int loadedThisBatch = 0;
            if (doc.RootElement.TryGetProperty("collection", out var collection))
            {
                foreach (var track in collection.EnumerateArray())
                {
                    loadedThisBatch++;
                    string title = track.TryGetProperty("title", out var t) ? t.GetString() ?? "SoundCloud Track" : "SoundCloud Track";
                    string artist = track.TryGetProperty("user", out var uObj) && uObj.TryGetProperty("username", out var a) ? a.GetString() ?? "" : "";
                    int durationMs = track.TryGetProperty("duration", out var dur) ? dur.GetInt32() : 0;
                    var timeSpan = TimeSpan.FromMilliseconds(durationMs);
                    string durText = timeSpan.Hours > 0 ? timeSpan.ToString(@"hh\:mm\:ss") : timeSpan.ToString(@"mm\:ss");

                    string coverUrl = "";
                    if (track.TryGetProperty("artwork_url", out var art) && !string.IsNullOrEmpty(art.GetString()))
                    {
                        coverUrl = art.GetString()!.Replace("-large.jpg", "-t500x500.jpg");
                    }
                    else if (track.TryGetProperty("user", out var userObj) && userObj.TryGetProperty("avatar_url", out var av) && !string.IsNullOrEmpty(av.GetString()))
                    {
                        coverUrl = av.GetString()!.Replace("-large.jpg", "-t500x500.jpg");
                    }

                    string transcodingUrl = "";
                    if (track.TryGetProperty("media", out var media) && media.TryGetProperty("transcodings", out var transcodings))
                    {
                        foreach (var trans in transcodings.EnumerateArray())
                        {
                            string protocol = trans.TryGetProperty("format", out var f) && f.TryGetProperty("protocol", out var pr) ? pr.GetString() ?? "" : "";
                            string tUrl = trans.TryGetProperty("url", out var tu) ? tu.GetString() ?? "" : "";

                            if (protocol == "progressive" && !string.IsNullOrEmpty(tUrl))
                            {
                                transcodingUrl = tUrl;
                                break;
                            }
                            else if (string.IsNullOrEmpty(transcodingUrl) && !string.IsNullOrEmpty(tUrl))
                            {
                                transcodingUrl = tUrl;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(transcodingUrl))
                    {
                        SearchResults.Add(new SoundcloudTrackInfo
                        {
                            Title = title,
                            Artist = artist,
                            DurationMs = durationMs,
                            DurationText = durText,
                            CoverUrl = coverUrl,
                            TranscodingUrl = transcodingUrl,
                            PermalinkUrl = track.TryGetProperty("permalink_url", out var pl) ? pl.GetString() ?? "" : ""
                        });
                    }
                }
            }

            _currentSearchOffset += limit;

            if (SearchResults.Count > 0)
            {
                txtResultCount.Text = $"KẾT QUẢ TÌM KIẾM ({SearchResults.Count} BÀI HÁT)";
                txtStatus.Text = $"Tìm thấy {SearchResults.Count} kết quả. Nhấn 'TẢI' trên bài hát để tải về máy.";
            }

            _canLoadMoreSoundcloud = loadedThisBatch >= limit;
            btnLoadMore.Visibility = _canLoadMoreSoundcloud ? Visibility.Visible : Visibility.Collapsed;
            return _canLoadMoreSoundcloud;
        }

        private async void BtnLoadMore_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSearchInput) || !btnLoadMore.IsEnabled) return;

            try
            {
                btnLoadMore.IsEnabled = false;
                iconLoadMore.Kind = MaterialDesignThemes.Wpf.PackIconKind.DotsHorizontal;
                txtLoadMore.Text = "Đang tải...";

                await LoadMoreSoundcloudResultsAsync(20);
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Lỗi khi tải thêm kết quả: " + ex.Message;
            }
            finally
            {
                btnLoadMore.IsEnabled = true;
                iconLoadMore.Kind = MaterialDesignThemes.Wpf.PackIconKind.ChevronDown;
                txtLoadMore.Text = "Xem thêm";
            }
        }

        private async Task<SoundcloudTrackInfo> FetchSoundcloudTrackAsync(string rawUrl)
        {
            string clientId = await EnsureClientIdAsync();

            // 1. Follow redirect if needed
            using var headReq = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            using var headRes = await _httpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);
            string finalUrl = headRes.RequestMessage?.RequestUri?.ToString() ?? rawUrl;

            var uri = new Uri(finalUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2)
            {
                throw new Exception("Đường dẫn SoundCloud không hợp lệ. Vui lòng nhập link bài hát.");
            }

            string artistSlug = segments[0];
            string trackSlug = segments[1];
            string query = $"{artistSlug} {trackSlug.Replace('-', ' ')}";

            string searchUrl = $"https://api-v2.soundcloud.com/search/tracks?q={Uri.EscapeDataString(query)}&client_id={clientId}&limit=10";
            var json = await _httpClient.GetStringAsync(searchUrl);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("collection", out var collection) || collection.GetArrayLength() == 0)
            {
                throw new Exception("Không tìm thấy thông tin bài hát trên SoundCloud.");
            }

            JsonElement matchedTrack = default;
            foreach (var track in collection.EnumerateArray())
            {
                string permalinkUrl = track.TryGetProperty("permalink_url", out var pUrl) ? pUrl.GetString() ?? "" : "";
                string permalink = track.TryGetProperty("permalink", out var p) ? p.GetString() ?? "" : "";
                string userSlug = track.TryGetProperty("user", out var u) && u.TryGetProperty("permalink", out var up) ? up.GetString() ?? "" : "";

                if (permalinkUrl.Equals(finalUrl, StringComparison.OrdinalIgnoreCase) ||
                    (permalink.Equals(trackSlug, StringComparison.OrdinalIgnoreCase) && userSlug.Equals(artistSlug, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedTrack = track;
                    break;
                }
            }

            if (matchedTrack.ValueKind == JsonValueKind.Undefined && collection.GetArrayLength() > 0)
            {
                matchedTrack = collection[0];
            }

            if (matchedTrack.ValueKind == JsonValueKind.Undefined)
            {
                throw new Exception("Không tìm thấy dữ liệu stream âm thanh của bài hát.");
            }

            int durationMs = matchedTrack.TryGetProperty("duration", out var dur) ? dur.GetInt32() : 0;
            var timeSpan = TimeSpan.FromMilliseconds(durationMs);
            string durText = timeSpan.Hours > 0 ? timeSpan.ToString(@"hh\:mm\:ss") : timeSpan.ToString(@"mm\:ss");

            var trackInfo = new SoundcloudTrackInfo
            {
                Title = matchedTrack.TryGetProperty("title", out var t) ? t.GetString() ?? "SoundCloud Track" : "SoundCloud Track",
                Artist = matchedTrack.TryGetProperty("user", out var uObj) && uObj.TryGetProperty("username", out var a) ? a.GetString() ?? "SoundCloud Artist" : "SoundCloud Artist",
                DurationMs = durationMs,
                DurationText = durText,
                PermalinkUrl = matchedTrack.TryGetProperty("permalink_url", out var pl) ? pl.GetString() ?? finalUrl : finalUrl
            };

            if (matchedTrack.TryGetProperty("artwork_url", out var art) && !string.IsNullOrEmpty(art.GetString()))
            {
                trackInfo.CoverUrl = art.GetString()!.Replace("-large.jpg", "-t500x500.jpg");
            }
            else if (matchedTrack.TryGetProperty("user", out var userObj) && userObj.TryGetProperty("avatar_url", out var av) && !string.IsNullOrEmpty(av.GetString()))
            {
                trackInfo.CoverUrl = av.GetString()!.Replace("-large.jpg", "-t500x500.jpg");
            }

            if (matchedTrack.TryGetProperty("media", out var media) && media.TryGetProperty("transcodings", out var transcodings))
            {
                string? selectedTranscodingUrl = null;
                foreach (var trans in transcodings.EnumerateArray())
                {
                    string protocol = trans.TryGetProperty("format", out var f) && f.TryGetProperty("protocol", out var pr) ? pr.GetString() ?? "" : "";
                    string tUrl = trans.TryGetProperty("url", out var tu) ? tu.GetString() ?? "" : "";

                    if (protocol == "progressive" && !string.IsNullOrEmpty(tUrl))
                    {
                        selectedTranscodingUrl = tUrl;
                        break;
                    }
                    else if (selectedTranscodingUrl == null && !string.IsNullOrEmpty(tUrl))
                    {
                        selectedTranscodingUrl = tUrl;
                    }
                }

                trackInfo.TranscodingUrl = selectedTranscodingUrl ?? "";

                if (!string.IsNullOrEmpty(selectedTranscodingUrl))
                {
                    string streamReq = $"{selectedTranscodingUrl}?client_id={clientId}";
                    var streamJson = await _httpClient.GetStringAsync(streamReq);
                    using var sDoc = JsonDocument.Parse(streamJson);
                    if (sDoc.RootElement.TryGetProperty("url", out var directUrl))
                    {
                        trackInfo.StreamUrl = directUrl.GetString() ?? "";
                    }
                }
            }

            if (string.IsNullOrEmpty(trackInfo.StreamUrl))
            {
                throw new Exception("Không thể lấy đường dẫn stream âm thanh trực tiếp từ SoundCloud.");
            }

            return trackInfo;
        }

        private async void BtnDownloadSearchResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is SoundcloudTrackInfo item)
            {
                btnDownload.IsEnabled = false;
                btnFetch.IsEnabled = false;
                btnLoadMore.IsEnabled = false;
                txtUrl.IsEnabled = false;
                lstSearchResults.IsEnabled = false;
                progressDownload.Value = 0;
                txtStatus.Visibility = Visibility.Visible;
                txtStatus.Text = $"Đang chuẩn bị tải '{item.Title}'...";

                try
                {
                    // Resolve StreamUrl if not populated yet
                    if (string.IsNullOrEmpty(item.StreamUrl) && !string.IsNullOrEmpty(item.TranscodingUrl))
                    {
                        string clientId = await EnsureClientIdAsync();
                        string streamReq = $"{item.TranscodingUrl}?client_id={clientId}";
                        var streamJson = await _httpClient.GetStringAsync(streamReq);
                        using var sDoc = JsonDocument.Parse(streamJson);
                        if (sDoc.RootElement.TryGetProperty("url", out var directUrl))
                        {
                            item.StreamUrl = directUrl.GetString() ?? "";
                        }
                    }

                    if (string.IsNullOrEmpty(item.StreamUrl))
                    {
                        throw new Exception("Không tìm thấy link stream âm thanh.");
                    }

                    _currentTrack = item;
                    await ExecuteDownloadTrackAsync(item);

                    txtStatus.Text = $"Đã tải xong '{item.Title}'! Đã thêm vào danh sách phát.";
                    AnimateProgress(100);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã xảy ra lỗi khi tải: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    progressDownload.Value = 0;
                    txtStatus.Text = "";
                }
                finally
                {
                    btnFetch.IsEnabled = true;
                    btnLoadMore.IsEnabled = true;
                    txtUrl.IsEnabled = true;
                    lstSearchResults.IsEnabled = true;
                }
            }
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null || string.IsNullOrEmpty(_currentTrack.StreamUrl)) return;

            btnDownload.IsEnabled = false;
            btnFetch.IsEnabled = false;
            txtUrl.IsEnabled = false;
            progressDownload.Value = 0;
            txtStatus.Visibility = Visibility.Visible;
            txtStatus.Text = "Đang tải bài hát từ SoundCloud...";

            try
            {
                await ExecuteDownloadTrackAsync(_currentTrack);

                AnimateProgress(100);
                txtStatus.Text = $"Tải xuống hoàn tất '{_currentTrack.Title}'! Đã thêm vào danh sách phát.";
                btnDownload.IsEnabled = true;
                btnFetch.IsEnabled = true;
                txtUrl.IsEnabled = true;
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Lỗi khi tải: {ex.Message}";
                btnDownload.IsEnabled = true;
                btnFetch.IsEnabled = true;
                txtUrl.IsEnabled = true;
            }
        }

        private async Task ExecuteDownloadTrackAsync(SoundcloudTrackInfo track)
        {
            string rawTitle = string.IsNullOrWhiteSpace(track.Title) ? "SoundCloud_Track_" + DateTime.Now.Ticks : track.Title;
            string safeTitle = string.Join("_", rawTitle.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrEmpty(safeTitle)) safeTitle = "SoundCloud_Track_" + DateTime.Now.Ticks;

            string finalFile = Path.Combine(_destinationFolder, safeTitle + ".mp3");
            int counter = 1;
            while (File.Exists(finalFile))
            {
                finalFile = Path.Combine(_destinationFolder, $"{safeTitle}_{counter}.mp3");
                counter++;
            }

            // Download MP3 Stream directly
            AnimateProgress(25);
            txtStatus.Text = "Đang tải luồng âm thanh MP3...";
            using (var stream = await _httpClient.GetStreamAsync(track.StreamUrl))
            using (var fs = new FileStream(finalFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fs);
            }

            // Add ID3 Metadata
            AnimateProgress(85);
            txtStatus.Text = "Đang gắn Metadata...";
            await Task.Run(async () =>
            {
                try
                {
                    var tfile = TagLib.File.Create(finalFile);
                    tfile.Tag.Title = track.Title;
                    if (!string.IsNullOrEmpty(track.Artist))
                    {
                        tfile.Tag.Performers = new[] { track.Artist };
                    }

                    if (!string.IsNullOrEmpty(track.CoverUrl))
                    {
                        try
                        {
                            var imageBytes = await _httpClient.GetByteArrayAsync(track.CoverUrl);
                            var picture = new TagLib.Picture(new TagLib.ByteVector(imageBytes))
                            {
                                Type = TagLib.PictureType.FrontCover,
                                Description = "Cover",
                                MimeType = "image/jpeg"
                            };
                            tfile.Tag.Pictures = new TagLib.IPicture[] { picture };
                        }
                        catch { }
                    }
                    tfile.Save();
                }
                catch { }
            });

            DownloadedFilePath = finalFile;
            DownloadCompleted?.Invoke(this, finalFile);
        }
    }
}

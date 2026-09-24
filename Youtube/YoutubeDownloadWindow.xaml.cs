using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos.Streams;

namespace FresAudio.Youtube
{
    public partial class YoutubeDownloadWindow : Window
    {
        private readonly YoutubeClient _youtube;
        private YoutubeExplode.Videos.Video? _currentVideo;
        private string? _singleVideoId;
        private string? _singleVideoTitle;
        private string? _singleVideoAuthor;
        private string? _singleVideoDurationText;
        private string? _singleVideoThumbnailUrl;
        private List<Thumbnail> _singleVideoThumbnails = new List<Thumbnail>();
        private string _destinationFolder;
        public string DestinationFolder => _destinationFolder;
        private readonly List<string> _availableFoldersList = new List<string>();
        public string? DownloadedFilePath { get; private set; }
        public event EventHandler<string>? DownloadCompleted;

        private bool _isPlaylist = false;
        private IReadOnlyList<YoutubeExplode.Playlists.PlaylistVideo>? _currentPlaylistVideos;
        private CancellationTokenSource? _downloadCts;
        private IAsyncEnumerator<YoutubeExplode.Search.VideoSearchResult>? _youtubeSearchEnumerator;
        private bool _canLoadMoreYoutube = false;
        public ObservableCollection<YoutubeSearchResultItem> SearchResults { get; } = new ObservableCollection<YoutubeSearchResultItem>();
        public ObservableCollection<YoutubePlaylistItem> PlaylistItems { get; } = new ObservableCollection<YoutubePlaylistItem>();

        public class YoutubeSearchResultItem
        {
            public string VideoId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
            public string DurationText { get; set; } = string.Empty;
            public string ThumbnailUrl { get; set; } = string.Empty;
            public IReadOnlyList<Thumbnail> Thumbnails { get; set; } = new List<Thumbnail>();
        }

        public class YoutubePlaylistItem : System.ComponentModel.INotifyPropertyChanged
        {
            public int Index { get; set; }
            public string VideoId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
            public string DurationText { get; set; } = string.Empty;
            public string ThumbnailUrl { get; set; } = string.Empty;
            public IReadOnlyList<Thumbnail> Thumbnails { get; set; } = new List<Thumbnail>();

            private string _status = "Chờ tải";
            public string Status
            {
                get => _status;
                set 
                { 
                    _status = value; 
                    OnPropertyChanged(nameof(Status)); 
                    OnPropertyChanged(nameof(CanDownload));
                    OnPropertyChanged(nameof(DownloadButtonVisibility));
                }
            }

            private bool _isBatchDownloading = false;
            public bool IsBatchDownloading
            {
                get => _isBatchDownloading;
                set
                {
                    if (_isBatchDownloading != value)
                    {
                        _isBatchDownloading = value;
                        OnPropertyChanged(nameof(IsBatchDownloading));
                        OnPropertyChanged(nameof(CanDownload));
                    }
                }
            }

            public bool CanDownload => !_isBatchDownloading && Status != "Đã có sẵn" && Status != "Hoàn tất" && Status != "Đã có sẵn (bỏ qua)" && Status != "Đang tải...";
            public Visibility DownloadButtonVisibility => (Status == "Đã có sẵn" || Status == "Hoàn tất" || Status == "Đã có sẵn (bỏ qua)") ? Visibility.Collapsed : Visibility.Visible;

            private string _statusColor = "#888888";
            public string StatusColor
            {
                get => _statusColor;
                set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
            }

            private string _statusIcon = "ClockOutline";
            public string StatusIcon
            {
                get => _statusIcon;
                set { _statusIcon = value; OnPropertyChanged(nameof(StatusIcon)); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop));
        }

        public YoutubeDownloadWindow(string destinationFolder, IEnumerable<string>? availableFolders = null)
        {
            InitializeComponent();
            _youtube = new YoutubeClient();
            _destinationFolder = destinationFolder;

            InitializeFolderList(destinationFolder, availableFolders);

            lstSearchResults.ItemsSource = SearchResults;
            lstPlaylistItems.ItemsSource = PlaylistItems;
            txtStatus.Text = string.Empty;
            progressContainer.Visibility = Visibility.Collapsed;

            Loaded += (s, e) =>
            {
                txtUrl.Focus();
                CheckClipboardForYoutubeUrl();
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

        private void RefreshPlaylistExistingStatus()
        {
            if (!_isPlaylist || PlaylistItems.Count == 0) return;
            int existingCount = 0;
            foreach (var item in PlaylistItems)
            {
                string safeTitle = string.Join("_", item.Title.Split(Path.GetInvalidFileNameChars()));
                string finalFile = Path.Combine(_destinationFolder, safeTitle + ".mp3");
                bool exists = File.Exists(finalFile) && new FileInfo(finalFile).Length > 1024;
                if (exists)
                {
                    item.Status = "Đã có sẵn";
                    item.StatusColor = "#10b981";
                    item.StatusIcon = "CheckCircle";
                    existingCount++;
                }
                else if (item.Status == "Đã có sẵn" || item.Status == "Hoàn tất")
                {
                    item.Status = "Chờ tải";
                    item.StatusColor = "#888888";
                    item.StatusIcon = "ClockOutline";
                }
            }
            int needCount = PlaylistItems.Count - existingCount;
            txtPlaylistStats.Text = $"{PlaylistItems.Count} bài hát • {needCount} bài mới cần tải, {existingCount} bài đã có sẵn (bỏ qua)";
        }

        private void CboDestinationFolder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboDestinationFolder.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                _destinationFolder = selected;
                RefreshPlaylistExistingStatus();
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
                    RefreshPlaylistExistingStatus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi chọn thư mục: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckClipboardForYoutubeUrl()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    if (text.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
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
                DragMove();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
            {
                _downloadCts.Cancel();
            }
            base.OnClosing(e);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnStopDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
            {
                _downloadCts.Cancel();
                btnStopDownload.IsEnabled = false;
                txtStatus.Text = "Đang dừng tải...";
            }
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
            if (string.IsNullOrEmpty(txtUrl.Text) && infoPanel.Visibility == Visibility.Collapsed && searchPanel.Visibility == Visibility.Collapsed && playlistPanel.Visibility == Visibility.Collapsed)
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

        private void LstPlaylistItems_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
                if (scrollViewer != null)
                {
                    double newOffset = scrollViewer.VerticalOffset - (e.Delta / 3.0);
                    scrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableHeight)));
                    e.Handled = true;
                }
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

        private static string ExtractVideoId(string url)
        {
            var match = Regex.Match(url, @"(?:v=|\/|youtu\.be\/)([0-9A-Za-z_-]{11})");
            return match.Success ? match.Groups[1].Value : url.Trim();
        }

        private async void BtnFetch_Click(object sender, RoutedEventArgs e)
        {
            string input = txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            btnFetch.IsEnabled = false;
            btnDownload.Visibility = Visibility.Collapsed;
            btnDownload.IsEnabled = false;
            btnLoadMore.Visibility = Visibility.Collapsed;
            scrollSearchResults.ScrollToTop();
            welcomePanel.Visibility = Visibility.Collapsed;
            infoPanel.Visibility = Visibility.Collapsed;
            searchPanel.Visibility = Visibility.Collapsed;
            playlistPanel.Visibility = Visibility.Collapsed;
            progressContainer.Visibility = Visibility.Visible;
            progressDownload.Value = 0;
            txtStatus.Visibility = Visibility.Visible;
            txtStatus.Text = "Đang tìm kiếm thông tin...";

            if (_youtubeSearchEnumerator != null)
            {
                try { await _youtubeSearchEnumerator.DisposeAsync(); } catch { }
                _youtubeSearchEnumerator = null;
            }

            _isPlaylist = false;
            _currentPlaylistVideos = null;
            _currentVideo = null;
            _singleVideoId = null;
            _singleVideoTitle = null;
            _singleVideoAuthor = null;
            _singleVideoDurationText = null;
            _singleVideoThumbnailUrl = null;
            _singleVideoThumbnails.Clear();
            SearchResults.Clear();
            PlaylistItems.Clear();

            bool isUrl = input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         input.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                         input.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

            try
            {
                if (isUrl)
                {
                    bool isPlaylist = (input.Contains("playlist?list=") || input.Contains("&list=") || input.Contains("?list=")) && !input.Contains("list=RD");
                    if (isPlaylist)
                    {
                        txtStatus.Text = "Đang lấy danh sách bài hát trong Playlist...";
                        var playlist = await _youtube.Playlists.GetAsync(input);

                        txtPlaylistTitle.Text = playlist.Title;
                        txtPlaylistChannel.Text = playlist.Author?.ChannelTitle ?? "Nhiều tác giả";

                        var playlistThumbUrl = playlist.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url;
                        if (!string.IsNullOrEmpty(playlistThumbUrl))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(playlistThumbUrl, UriKind.Absolute);
                            bitmap.EndInit();
                            imgPlaylistThumbnail.Source = bitmap;
                        }

                        var videosList = new List<YoutubeExplode.Playlists.PlaylistVideo>();
                        int idx = 1;
                        int existingCount = 0;

                        await foreach (var video in _youtube.Playlists.GetVideosAsync(playlist.Id))
                        {
                            videosList.Add(video);

                            var thumbUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "";
                            string safeTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
                            string finalFile = Path.Combine(_destinationFolder, safeTitle + ".mp3");
                            bool exists = File.Exists(finalFile) && new FileInfo(finalFile).Length > 1024;
                            if (exists) existingCount++;

                            PlaylistItems.Add(new YoutubePlaylistItem
                            {
                                Index = idx++,
                                VideoId = video.Id,
                                Title = video.Title,
                                Author = video.Author?.ChannelTitle ?? "YouTube",
                                DurationText = video.Duration?.ToString(@"mm\:ss") ?? "--:--",
                                ThumbnailUrl = thumbUrl,
                                Thumbnails = video.Thumbnails.ToList(),
                                Status = exists ? "Đã có sẵn" : "Chờ tải",
                                StatusColor = exists ? "#10b981" : "#888888",
                                StatusIcon = exists ? "CheckCircle" : "ClockOutline"
                            });
                        }

                        _currentPlaylistVideos = videosList;
                        _isPlaylist = true;

                        int needCount = PlaylistItems.Count - existingCount;
                        txtPlaylistStats.Text = $"{PlaylistItems.Count} bài hát • {needCount} bài mới cần tải, {existingCount} bài đã có sẵn (bỏ qua)";

                        playlistPanel.Visibility = Visibility.Visible;
                        btnDownload.Visibility = Visibility.Visible;
                        btnDownload.IsEnabled = true;
                        txtStatus.Text = needCount > 0 
                            ? $"Tìm thấy {PlaylistItems.Count} bài hát ({needCount} bài mới). Nhấn 'TẢI XUỐNG MP3' bên dưới để tải."
                            : $"Toàn bộ {PlaylistItems.Count} bài hát trong Playlist đã có sẵn trong máy tính!";
                    }
                    else
                    {
                        try
                        {
                            _currentVideo = await _youtube.Videos.GetAsync(input);
                            _singleVideoId = _currentVideo.Id.Value;
                            _singleVideoTitle = _currentVideo.Title;
                            _singleVideoAuthor = _currentVideo.Author.ChannelTitle;
                            _singleVideoDurationText = _currentVideo.Duration?.ToString(@"hh\:mm\:ss") ?? "Không rõ";
                            _singleVideoThumbnails = _currentVideo.Thumbnails.ToList();
                            _singleVideoThumbnailUrl = _currentVideo.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "";
                        }
                        catch
                        {
                            // Smart Fallback via YouTube oEmbed API
                            string vidId = ExtractVideoId(input);
                            string oembedUrl = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={vidId}&format=json";
                            using var http = new HttpClient();
                            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                            var jsonStr = await http.GetStringAsync(oembedUrl);
                            using var oDoc = JsonDocument.Parse(jsonStr);

                            _singleVideoId = vidId;
                            _singleVideoTitle = oDoc.RootElement.GetProperty("title").GetString() ?? "YouTube Video";
                            _singleVideoAuthor = oDoc.RootElement.GetProperty("author_name").GetString() ?? "YouTube";
                            _singleVideoDurationText = "--:--";
                            _singleVideoThumbnailUrl = oDoc.RootElement.TryGetProperty("thumbnail_url", out var th) ? th.GetString() ?? "" : $"https://i.ytimg.com/vi/{vidId}/hqdefault.jpg";
                            _singleVideoThumbnails = new List<Thumbnail>
                            {
                                new Thumbnail(_singleVideoThumbnailUrl, new YoutubeExplode.Common.Resolution(480, 360))
                            };
                        }

                        txtTitle.Text = _singleVideoTitle;
                        txtChannel.Text = _singleVideoAuthor;
                        txtDuration.Text = _singleVideoDurationText;

                        if (!string.IsNullOrEmpty(_singleVideoThumbnailUrl))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(_singleVideoThumbnailUrl, UriKind.Absolute);
                            bitmap.EndInit();
                            imgThumbnail.Source = bitmap;
                        }

                        infoPanel.Visibility = Visibility.Visible;
                        btnDownload.Visibility = Visibility.Visible;
                        btnDownload.IsEnabled = true;
                        txtStatus.Text = "Tìm thấy bài hát! Nhấn nút 'TẢI XUỐNG MP3' bên dưới để tải.";
                    }
                }
                else
                {
                    txtStatus.Text = $"Đang tìm kiếm '{input}' trên YouTube...";
                    _youtubeSearchEnumerator = _youtube.Search.GetVideosAsync(input).GetAsyncEnumerator();
                    await LoadMoreYoutubeResultsAsync(20);

                    if (SearchResults.Count > 0)
                    {
                        searchPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        welcomePanel.Visibility = Visibility.Visible;
                        txtStatus.Text = "Không tìm thấy kết quả phù hợp trên YouTube.";
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Không thể lấy thông tin: " + ex.Message;
                welcomePanel.Visibility = Visibility.Visible;
            }
            finally
            {
                btnFetch.IsEnabled = true;
            }
        }

        private async Task<bool> LoadMoreYoutubeResultsAsync(int countToLoad = 20)
        {
            if (_youtubeSearchEnumerator == null) return false;

            int loaded = 0;
            while (loaded < countToLoad && await _youtubeSearchEnumerator.MoveNextAsync())
            {
                var video = _youtubeSearchEnumerator.Current;
                string durText = video.Duration?.ToString(@"mm\:ss") ?? "";
                if (video.Duration.HasValue && video.Duration.Value.TotalHours >= 1)
                {
                    durText = video.Duration.Value.ToString(@"hh\:mm\:ss");
                }

                var thumbUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "";

                SearchResults.Add(new YoutubeSearchResultItem
                {
                    VideoId = video.Id.Value,
                    Title = video.Title,
                    Author = video.Author.ChannelTitle,
                    DurationText = durText,
                    ThumbnailUrl = thumbUrl,
                    Thumbnails = video.Thumbnails.ToList()
                });

                loaded++;
            }

            if (SearchResults.Count > 0)
            {
                txtResultCount.Text = $"KẾT QUẢ TÌM KIẾM ({SearchResults.Count} BÀI HÁT)";
                txtStatus.Text = $"Tìm thấy {SearchResults.Count} kết quả. Nhấn 'TẢI' trên bài hát để tải về máy.";
            }

            _canLoadMoreYoutube = loaded >= countToLoad;
            btnLoadMore.Visibility = _canLoadMoreYoutube ? Visibility.Visible : Visibility.Collapsed;
            return _canLoadMoreYoutube;
        }

        private async void BtnLoadMore_Click(object sender, RoutedEventArgs e)
        {
            if (_youtubeSearchEnumerator == null || !btnLoadMore.IsEnabled) return;

            try
            {
                btnLoadMore.IsEnabled = false;
                txtLoadMore.Text = "Đang tải...";

                await LoadMoreYoutubeResultsAsync(20);
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Lỗi khi tải thêm kết quả: " + ex.Message;
            }
            finally
            {
                btnLoadMore.IsEnabled = true;
                txtLoadMore.Text = "Xem thêm";
            }
        }

        private async void BtnDownloadSearchResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is YoutubeSearchResultItem item)
            {
                btnDownload.IsEnabled = false;
                btnFetch.IsEnabled = false;
                btnLoadMore.IsEnabled = false;
                txtUrl.IsEnabled = false;
                lstSearchResults.IsEnabled = false;
                progressDownload.Value = 0;
                txtStatus.Visibility = Visibility.Visible;
                txtStatus.Text = $"Đang chuẩn bị tải '{item.Title}'...";

                _downloadCts = new CancellationTokenSource();
                var token = _downloadCts.Token;
                btnStopDownload.Visibility = Visibility.Visible;
                btnStopDownload.IsEnabled = true;

                try
                {
                    await DownloadSingleVideoAsync(item.VideoId, item.Title, item.Thumbnails, token);
                    if (!token.IsCancellationRequested)
                    {
                        txtStatus.Text = $"Đã tải xong '{item.Title}'! Đã thêm vào danh sách phát.";
                        AnimateProgress(100);
                    }
                }
                catch (OperationCanceledException)
                {
                    txtStatus.Text = $"Đã hủy tải bài '{item.Title}'.";
                    progressDownload.Value = 0;
                }
                catch (YoutubeExplode.Exceptions.VideoUnavailableException)
                {
                    MessageBox.Show("YouTube đang tạm thời giới hạn tải từ mạng của bạn (hạn chế bot / bảo mật). Bạn vui lòng đợi một lát rồi thử lại, hoặc thử đổi mạng (4G / Wi-Fi khác) hoặc chọn bài hát khác trong danh sách nhé!", "Tạm thời bị giới hạn tải", MessageBoxButton.OK, MessageBoxImage.Warning);
                    progressDownload.Value = 0;
                    txtStatus.Text = "Tạm thời bị giới hạn tải. Vui lòng thử lại sau.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã xảy ra lỗi khi tải: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    progressDownload.Value = 0;
                    txtStatus.Text = "";
                }
                finally
                {
                    btnStopDownload.Visibility = Visibility.Collapsed;
                    btnStopDownload.IsEnabled = true;
                    btnFetch.IsEnabled = true;
                    btnLoadMore.IsEnabled = true;
                    txtUrl.IsEnabled = true;
                    lstSearchResults.IsEnabled = true;
                }
            }
        }

        private async void BtnDownloadPlaylistItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is YoutubePlaylistItem item)
            {
                if (!item.CanDownload) return;

                btnDownload.IsEnabled = false;
                btnFetch.IsEnabled = false;
                txtUrl.IsEnabled = false;
                lstPlaylistItems.IsEnabled = false;
                progressDownload.Value = 0;
                txtStatus.Visibility = Visibility.Visible;
                txtStatus.Text = $"Đang chuẩn bị tải '{item.Title}'...";

                _downloadCts = new CancellationTokenSource();
                var token = _downloadCts.Token;
                btnStopDownload.Visibility = Visibility.Visible;
                btnStopDownload.IsEnabled = true;

                item.Status = "Đang tải...";
                item.StatusColor = "#3b82f6";
                item.StatusIcon = "ArrowDownCircle";

                try
                {
                    await DownloadSingleVideoAsync(item.VideoId, item.Title, item.Thumbnails, token);
                    if (!token.IsCancellationRequested)
                    {
                        item.Status = "Hoàn tất";
                        item.StatusColor = "#10b981";
                        item.StatusIcon = "CheckCircle";
                        txtStatus.Text = $"Đã tải xong '{item.Title}'! Đã thêm vào danh sách phát.";
                        AnimateProgress(100);
                    }
                }
                catch (OperationCanceledException)
                {
                    item.Status = "Đã hủy";
                    item.StatusColor = "#e11d48";
                    item.StatusIcon = "CloseCircle";
                    txtStatus.Text = $"Đã hủy tải bài '{item.Title}'.";
                    progressDownload.Value = 0;
                }
                catch (YoutubeExplode.Exceptions.VideoUnavailableException)
                {
                    item.Status = "Bị giới hạn";
                    item.StatusColor = "#f59e0b";
                    item.StatusIcon = "AlertCircle";
                    MessageBox.Show("YouTube đang tạm thời giới hạn tải từ mạng của bạn (hạn chế bot / bảo mật). Bạn vui lòng đợi một lát rồi thử lại, hoặc thử đổi mạng (4G / Wi-Fi khác) hoặc chọn bài khác nhé!", "Tạm thời bị giới hạn tải", MessageBoxButton.OK, MessageBoxImage.Warning);
                    progressDownload.Value = 0;
                    txtStatus.Text = "Tạm thời bị giới hạn tải.";
                }
                catch (Exception ex)
                {
                    item.Status = "Bị lỗi";
                    item.StatusColor = "#f59e0b";
                    item.StatusIcon = "AlertCircle";
                    MessageBox.Show("Đã xảy ra lỗi khi tải: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    progressDownload.Value = 0;
                    txtStatus.Text = "";
                }
                finally
                {
                    btnStopDownload.Visibility = Visibility.Collapsed;
                    btnStopDownload.IsEnabled = true;
                    btnFetch.IsEnabled = true;
                    btnDownload.IsEnabled = true;
                    txtUrl.IsEnabled = true;
                    lstPlaylistItems.IsEnabled = true;
                    RefreshPlaylistExistingStatus();
                }
            }
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null && string.IsNullOrEmpty(_singleVideoId) && !_isPlaylist) return;

            btnDownload.IsEnabled = false;
            btnFetch.IsEnabled = false;
            txtUrl.IsEnabled = false;
            progressDownload.Value = 0;
            txtStatus.Visibility = Visibility.Visible;

            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;
            btnStopDownload.Visibility = Visibility.Visible;
            btnStopDownload.IsEnabled = true;

            foreach (var itm in PlaylistItems) itm.IsBatchDownloading = true;

            try
            {
                if (_isPlaylist && PlaylistItems.Count > 0)
                {
                    int total = PlaylistItems.Count;
                    int current = 0;
                    int downloadedCount = 0;
                    int skippedCount = 0;

                    foreach (var item in PlaylistItems)
                    {
                        if (token.IsCancellationRequested) break;
                        current++;

                        string safeTitle = string.Join("_", item.Title.Split(Path.GetInvalidFileNameChars()));
                        string finalFile = Path.Combine(_destinationFolder, safeTitle + ".mp3");

                        // Kiểm tra nếu đã có sẵn thì bỏ qua
                        if (File.Exists(finalFile) && new FileInfo(finalFile).Length > 1024)
                        {
                            item.Status = "Đã có sẵn (bỏ qua)";
                            item.StatusColor = "#10b981";
                            item.StatusIcon = "CheckCircle";
                            skippedCount++;
                            DownloadedFilePath = finalFile;
                            DownloadCompleted?.Invoke(this, finalFile);
                            continue;
                        }

                        txtStatus.Text = $"Đang tải bài {current}/{total}: {item.Title}...";
                        item.Status = "Đang tải...";
                        item.StatusColor = "#3b82f6";
                        item.StatusIcon = "ArrowDownCircle";
                        lstPlaylistItems.ScrollIntoView(item);

                        try
                        {
                            await DownloadSingleVideoAsync(item.VideoId, item.Title, item.Thumbnails, token);
                            item.Status = "Hoàn tất";
                            item.StatusColor = "#10b981";
                            item.StatusIcon = "CheckCircle";
                            downloadedCount++;
                        }
                        catch (OperationCanceledException)
                        {
                            item.Status = "Đã hủy";
                            item.StatusColor = "#e11d48";
                            item.StatusIcon = "CloseCircle";
                            break;
                        }
                        catch (Exception ex)
                        {
                            item.Status = "Bị lỗi";
                            item.StatusColor = "#f59e0b";
                            item.StatusIcon = "AlertCircle";
                            System.Diagnostics.Debug.WriteLine($"Error downloading {item.Title}: {ex.Message}");
                        }
                    }

                    if (token.IsCancellationRequested)
                    {
                        txtStatus.Text = $"Đã dừng tải theo yêu cầu. Đã hoàn tất {downloadedCount} bài mới, bỏ qua {skippedCount} bài đã có sẵn.";
                        progressDownload.Value = 0;
                    }
                    else
                    {
                        txtStatus.Text = $"Đã xử lý xong toàn bộ Playlist! Tải mới {downloadedCount} bài, bỏ qua {skippedCount} bài đã có sẵn.";
                        AnimateProgress(100);
                    }
                }
                else if (!_isPlaylist && (!string.IsNullOrEmpty(_singleVideoId) || _currentVideo != null))
                {
                    string vId = _currentVideo?.Id.Value ?? _singleVideoId!;
                    string vTitle = _currentVideo?.Title ?? _singleVideoTitle!;
                    var vThumbs = _currentVideo != null ? _currentVideo.Thumbnails.ToList() : _singleVideoThumbnails;

                    txtStatus.Text = "Đang tải luồng âm thanh...";
                    await DownloadSingleVideoAsync(vId, vTitle, vThumbs, token);
                    if (!token.IsCancellationRequested)
                    {
                        txtStatus.Text = "Đã tải xong! Đã thêm vào danh sách phát.";
                        AnimateProgress(100);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                txtStatus.Text = "Đã dừng tải theo yêu cầu.";
                progressDownload.Value = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã xảy ra lỗi khi tải: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                progressDownload.Value = 0;
                txtStatus.Text = "";
            }
            finally
            {
                foreach (var itm in PlaylistItems) itm.IsBatchDownloading = false;
                btnStopDownload.Visibility = Visibility.Collapsed;
                btnStopDownload.IsEnabled = true;
                btnDownload.IsEnabled = true;
                btnFetch.IsEnabled = true;
                txtUrl.IsEnabled = true;
                RefreshPlaylistExistingStatus();
            }
        }

        private static async Task<string> EnsureYtDlpAsync()
        {
            string localTools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "yt-dlp.exe");
            if (File.Exists(localTools)) return localTools;

            string localApp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
            if (File.Exists(localApp)) return localApp;

            string appDataBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FresAudio", "bin");
            string appDataYtdlp = Path.Combine(appDataBin, "yt-dlp.exe");
            if (File.Exists(appDataYtdlp)) return appDataYtdlp;

            Directory.CreateDirectory(appDataBin);
            await YoutubeDLSharp.Utils.DownloadYtDlp(appDataBin);
            return appDataYtdlp;
        }

        private static void ConvertToMp3(string inputFile, string outputFile, int bitrate = 320000)
        {
            try
            {
                using var reader = new MediaFoundationReader(inputFile);
                MediaFoundationEncoder.EncodeToMp3(reader, outputFile, bitrate);
            }
            catch
            {
                try
                {
                    File.Copy(inputFile, outputFile, true);
                }
                catch { }
            }
        }

        private async Task DownloadSingleVideoAsync(string videoId, string videoTitle, IReadOnlyList<Thumbnail> thumbnails, CancellationToken token = default)
        {
            string safeTitle = string.Join("_", videoTitle.Split(Path.GetInvalidFileNameChars()));
            string finalFile = Path.Combine(_destinationFolder, safeTitle + ".mp3");
            string downloadedMediaFile = "";

            token.ThrowIfCancellationRequested();

            try
            {
                // Engine 1: yt-dlp (Thử các player clients chuyên dụng của YouTube)
                txtStatus.Text = "Đang kết nối luồng âm thanh YouTube...";
                AnimateProgress(10);

                string ytdlPath = await EnsureYtDlpAsync();
                var ytdl = new YoutubeDLSharp.YoutubeDL
                {
                    YoutubeDLPath = ytdlPath,
                    OutputFolder = Path.GetTempPath(),
                    OutputFileTemplate = $"fres_yt_{videoId}_%(epoch)s.%(ext)s"
                };

                var progressHandler = new Progress<YoutubeDLSharp.DownloadProgress>(p =>
                {
                    AnimateProgress(15 + p.Progress * 55);
                });

                string videoUrl = $"https://www.youtube.com/watch?v={videoId}";
                string[] clients = new[] { "android", "mweb", "web_creator", "tvhtml5_simply_embedded_player" };

                foreach (var cl in clients)
                {
                    token.ThrowIfCancellationRequested();
                    var opt = new YoutubeDLSharp.Options.OptionSet
                    {
                        Format = "bestaudio/best",
                        ExtractorArgs = $"youtube:player_client={cl}"
                    };

                    var res = await ytdl.RunVideoDownload(videoUrl, progress: progressHandler, overrideOptions: opt, ct: token);
                    if (res.Success && !string.IsNullOrEmpty(res.Data) && File.Exists(res.Data))
                    {
                        downloadedMediaFile = res.Data;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }

            token.ThrowIfCancellationRequested();

            // Engine 2: YoutubeExplode fallback (Chỉ lấy trực tiếp từ YouTube)
            if (string.IsNullOrEmpty(downloadedMediaFile) || !File.Exists(downloadedMediaFile))
            {
                try
                {
                    var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, token);
                    var streamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
                    if (streamInfo != null)
                    {
                        downloadedMediaFile = Path.Combine(Path.GetTempPath(), $"fres_yt_{videoId}.{streamInfo.Container.Name}");
                        var progress = new Progress<double>(p =>
                        {
                            AnimateProgress(15 + p * 55);
                        });
                        await _youtube.Videos.Streams.DownloadAsync(streamInfo, downloadedMediaFile, progress, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }

            token.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(downloadedMediaFile) || !File.Exists(downloadedMediaFile))
            {
                throw new Exception("Không thể kết nối đến luồng âm thanh YouTube của video này.");
            }

            token.ThrowIfCancellationRequested();

            // Direct convert to MP3 320 kbps (giữ nguyên âm lượng gốc)
            AnimateProgress(75);
            txtStatus.Text = "Đang xuất file MP3 320 kbps...";
            await Task.Run(() =>
            {
                ConvertToMp3(downloadedMediaFile, finalFile, 320000);
            }, token);

            try { File.Delete(downloadedMediaFile); } catch { }

            token.ThrowIfCancellationRequested();

            // Add ID3 Metadata
            AnimateProgress(90);
            txtStatus.Text = "Đang gắn thẻ thông tin bài hát...";
            await Task.Run(async () =>
            {
                try
                {
                    var tfile = TagLib.File.Create(finalFile);
                    string cleanTitle = videoTitle;
                    string[] patterns = { "(Official Music Video)", "[Official Music Video]", "(Official Video)", "[Official Video]", "(Lyric Video)", "[Lyric Video]", "(Official Audio)", "[Official Audio]", "(Official)", "[Official]" };
                    foreach (var p in patterns)
                    {
                        cleanTitle = cleanTitle.Replace(p, "", StringComparison.OrdinalIgnoreCase);
                    }

                    tfile.Tag.Title = cleanTitle.Trim();
                    tfile.Tag.Performers = new string[0];

                    var thumbnailUrl = thumbnails?.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url;
                    if (!string.IsNullOrEmpty(thumbnailUrl))
                    {
                        using var httpClient = new HttpClient();
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                        var imageBytes = await httpClient.GetByteArrayAsync(thumbnailUrl, token);
                        var picture = new TagLib.Picture(new TagLib.ByteVector(imageBytes))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            Description = "Thumbnail",
                            MimeType = "image/jpeg"
                        };
                        tfile.Tag.Pictures = new TagLib.IPicture[] { picture };
                    }
                    tfile.Save();
                }
                catch { }
            }, token);

            AnimateProgress(100);
            DownloadedFilePath = finalFile;
            DownloadCompleted?.Invoke(this, finalFile);
            progressDownload.Value = 0;
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

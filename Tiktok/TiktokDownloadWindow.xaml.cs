using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FresAudio.Tiktok
{
    public class TiktokTrackInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string AudioUrl { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public int Duration { get; set; }
    }

    public partial class TiktokDownloadWindow : Window
    {
        private static readonly HttpClient _httpClient;
        private string _destinationFolder;
        public string DestinationFolder => _destinationFolder;
        private readonly List<string> _availableFoldersList = new List<string>();
        private TiktokTrackInfo? _currentTrack;

        public string DownloadedFilePath { get; private set; } = string.Empty;
        public event EventHandler<string>? DownloadCompleted;

        static TiktokDownloadWindow()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public TiktokDownloadWindow(string destinationFolder, IEnumerable<string>? availableFolders = null)
        {
            InitializeComponent();
            _destinationFolder = destinationFolder;

            InitializeFolderList(destinationFolder, availableFolders);
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

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void BtnFetch_Click(object sender, RoutedEventArgs e)
        {
            var url = txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                btnFetch.IsEnabled = false;
                txtStatus.Visibility = Visibility.Visible;
                txtStatus.Text = "Đang lấy thông tin từ TikTok...";
                _currentTrack = null;

                var track = await FetchTikTokInfoAsync(url);
                if (track == null || string.IsNullOrEmpty(track.AudioUrl))
                {
                    throw new Exception("Không tìm thấy luồng âm thanh hoặc liên kết không hợp lệ.");
                }

                _currentTrack = track;
                txtTitle.Text = string.IsNullOrWhiteSpace(track.Title) ? "TikTok Audio" : track.Title;
                txtChannel.Text = string.IsNullOrWhiteSpace(track.Author) ? "TikTok Creator" : track.Author;
                txtDuration.Text = track.Duration > 0 ? $"{track.Duration} giây" : "Âm thanh TikTok";

                if (!string.IsNullOrEmpty(track.CoverUrl))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(track.CoverUrl, UriKind.Absolute);
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
                btnDownload.IsEnabled = true;
                txtStatus.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể lấy thông tin TikTok: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Visibility = Visibility.Collapsed;
            }
            finally
            {
                btnFetch.IsEnabled = true;
            }
        }

        private async Task<TiktokTrackInfo?> FetchTikTokInfoAsync(string url)
        {
            var postReq = new HttpRequestMessage(HttpMethod.Post, "https://www.tikwm.com/api/");
            postReq.Headers.Add("Accept", "application/json, text/plain, */*");
            postReq.Headers.Add("Origin", "https://www.tikwm.com");
            postReq.Headers.Add("Referer", "https://www.tikwm.com/");
            postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "url", url },
                { "hd", "1" }
            });

            var response = await _httpClient.SendAsync(postReq);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var codeElem) && codeElem.GetInt32() == 0 &&
                root.TryGetProperty("data", out var data))
            {
                var track = new TiktokTrackInfo();

                // Check music_info first
                if (data.TryGetProperty("music_info", out var musicInfo) && musicInfo.ValueKind == JsonValueKind.Object)
                {
                    if (musicInfo.TryGetProperty("title", out var t)) track.Title = t.GetString() ?? "";
                    if (musicInfo.TryGetProperty("author", out var a)) track.Author = a.GetString() ?? "";
                    if (musicInfo.TryGetProperty("play", out var p)) track.AudioUrl = p.GetString() ?? "";
                    if (musicInfo.TryGetProperty("cover", out var c)) track.CoverUrl = c.GetString() ?? "";
                    if (musicInfo.TryGetProperty("duration", out var d) && d.TryGetInt32(out int dur)) track.Duration = dur;
                }

                // Fallback to top-level data fields
                if (string.IsNullOrEmpty(track.AudioUrl) && data.TryGetProperty("music", out var directMusic))
                {
                    track.AudioUrl = directMusic.GetString() ?? "";
                }

                if (string.IsNullOrEmpty(track.Title) && data.TryGetProperty("title", out var topTitle))
                {
                    track.Title = topTitle.GetString() ?? "";
                }

                if (string.IsNullOrEmpty(track.CoverUrl))
                {
                    if (data.TryGetProperty("cover", out var topCover)) track.CoverUrl = topCover.GetString() ?? "";
                    else if (data.TryGetProperty("origin_cover", out var origCover)) track.CoverUrl = origCover.GetString() ?? "";
                }

                if (string.IsNullOrEmpty(track.Author) && data.TryGetProperty("author", out var authorObj))
                {
                    if (authorObj.TryGetProperty("nickname", out var nick)) track.Author = nick.GetString() ?? "";
                    else if (authorObj.TryGetProperty("unique_id", out var uid)) track.Author = uid.GetString() ?? "";
                }

                if (track.Duration == 0 && data.TryGetProperty("duration", out var topDur) && topDur.TryGetInt32(out int dur2))
                {
                    track.Duration = dur2;
                }

                return track;
            }
            else
            {
                string msg = root.TryGetProperty("msg", out var msgElem) ? msgElem.GetString() ?? "Lỗi không xác định" : "Lỗi phản hồi từ máy chủ";
                throw new Exception(msg);
            }
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null || string.IsNullOrEmpty(_currentTrack.AudioUrl)) return;

            btnDownload.IsEnabled = false;
            btnFetch.IsEnabled = false;
            txtUrl.IsEnabled = false;
            progressDownload.Value = 0;
            txtStatus.Visibility = Visibility.Visible;
            txtStatus.Text = "Đang tải bài hát...";

            try
            {
                string rawTitle = string.IsNullOrWhiteSpace(_currentTrack.Title) ? "TikTok_Audio_" + DateTime.Now.Ticks : _currentTrack.Title;
                string safeTitle = string.Join("_", rawTitle.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrEmpty(safeTitle)) safeTitle = "TikTok_Audio_" + DateTime.Now.Ticks;

                string finalFile = Path.Combine(_destinationFolder, safeTitle + ".mp3");
                int counter = 1;
                while (File.Exists(finalFile))
                {
                    finalFile = Path.Combine(_destinationFolder, $"{safeTitle}_{counter}.mp3");
                    counter++;
                }

                // Download the audio file directly
                AnimateProgress(20);
                txtStatus.Text = "Đang tải luồng âm thanh...";
                byte[] audioBytes;
                using (var stream = await _httpClient.GetStreamAsync(_currentTrack.AudioUrl))
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    audioBytes = ms.ToArray();
                }

                AnimateProgress(70);
                txtStatus.Text = "Đang lưu tệp MP3...";
                await File.WriteAllBytesAsync(finalFile, audioBytes);

                // Add ID3 Metadata
                AnimateProgress(85);
                txtStatus.Text = "Đang gắn Metadata...";
                await Task.Run(async () =>
                {
                    try
                    {
                        var tfile = TagLib.File.Create(finalFile);
                        tfile.Tag.Title = _currentTrack.Title;
                        if (!string.IsNullOrEmpty(_currentTrack.Author))
                        {
                            tfile.Tag.Performers = new[] { _currentTrack.Author };
                        }

                        if (!string.IsNullOrEmpty(_currentTrack.CoverUrl))
                        {
                            try
                            {
                                var imageBytes = await _httpClient.GetByteArrayAsync(_currentTrack.CoverUrl);
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

                AnimateProgress(100);
                txtStatus.Text = $"Đã tải xong '{_currentTrack.Title}'! Đã thêm vào danh sách phát.";

                DownloadedFilePath = finalFile;
                DownloadCompleted?.Invoke(this, finalFile);

                btnDownload.IsEnabled = true;
                btnFetch.IsEnabled = true;
                txtUrl.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã xảy ra lỗi khi tải: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                btnDownload.IsEnabled = true;
                btnFetch.IsEnabled = true;
                txtUrl.IsEnabled = true;
                progressDownload.Value = 0;
                txtStatus.Text = "";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AnimateProgress(double value)
        {
            var anim = new DoubleAnimation(value, TimeSpan.FromMilliseconds(250));
            progressDownload.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
        }
    }
}

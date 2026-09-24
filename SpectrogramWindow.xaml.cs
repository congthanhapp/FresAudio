using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;

namespace FresAudio
{
    public partial class SpectrogramWindow : Window
    {
        private string? _currentFilePath;
        private CancellationTokenSource? _cts;
        private WriteableBitmap? _spectrogramBitmap;
        private float[,]? _dbMatrix;
        private double _totalDurationSeconds;
        private int _sampleRate = 44100;
        private int _maxFrequency = 22050;
        private int _imageWidth = 1200;
        private int _imageHeight = 512;
        private const int FftSize = 2048; // 1024 bins, ~21.5 Hz per bin for 44.1kHz
        private const float MinDb = -120f;
        private const float MaxDb = 0f;

        private static readonly uint[] ColorLut = GenerateColorPalette();

        public SpectrogramWindow(string? initialFilePath = null)
        {
            InitializeComponent();
            DrawDbAxis();

            if (!string.IsNullOrEmpty(initialFilePath) && File.Exists(initialFilePath))
            {
                LoadAndAnalyzeFile(initialFilePath);
            }
        }

        #region Window Control Events

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            UpdateWindowCornerRadius();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
            UpdateWindowCornerRadius();
        }

        private void UpdateWindowCornerRadius()
        {
            if (WindowState == WindowState.Maximized)
            {
                iconMaximize.Text = "\uE923";
                btnMaximize.ToolTip = "Khôi phục";
                RootBorder.CornerRadius = new CornerRadius(0);
                bdrHeader.CornerRadius = new CornerRadius(0);
                bdrFooter.CornerRadius = new CornerRadius(0);
            }
            else
            {
                iconMaximize.Text = "\uE922";
                btnMaximize.ToolTip = "Phóng to";
                RootBorder.CornerRadius = new CornerRadius(15);
                bdrHeader.CornerRadius = new CornerRadius(14, 14, 0, 0);
                bdrFooter.CornerRadius = new CornerRadius(0, 0, 14, 14);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        #endregion

        #region Drag & Drop and File Selection

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]))
                {
                    LoadAndAnalyzeFile(files[0]);
                }
            }
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Chọn tệp âm thanh để soi phổ",
                Filter = "Audio Files (*.flac;*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.wma)|*.flac;*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.wma|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog(this) == true)
            {
                LoadAndAnalyzeFile(ofd.FileName);
            }
        }

        private void BtnOpenCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin && !string.IsNullOrEmpty(mainWin.CurrentSongPath))
            {
                if (File.Exists(mainWin.CurrentSongPath))
                {
                    LoadAndAnalyzeFile(mainWin.CurrentSongPath);
                    return;
                }
            }

            txtStatusMessage.Text = "Không có bài hát nào đang phát trong FresAudio.";
        }

        #endregion

        #region Spectrogram Analysis & FFT Rendering

        public async void LoadAndAnalyzeFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _currentFilePath = filePath;

            // Hiển thị thông tin file ban đầu
            UpdateFileMetadataHeader(filePath);

            gridEmptyPlaceholder.Visibility = Visibility.Collapsed;
            gridLoading.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            txtLoadingStatus.Text = "Đang đọc dữ liệu âm thanh... 0%";
            txtStatusMessage.Text = "Đang phân tích...";

            try
            {
                int renderWidth = Math.Max(800, (int)gridSpectrogramContainer.ActualWidth);
                if (renderWidth <= 0) renderWidth = 1200;
                int renderHeight = _imageHeight;

                _imageWidth = renderWidth;

                var result = await Task.Run(() => ComputeSpectrogramData(filePath, renderWidth, renderHeight, (percent, msg) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = percent;
                        txtLoadingStatus.Text = $"{msg} {percent:F0}%";
                    });
                }, token), token);

                if (token.IsCancellationRequested || result == null) return;

                _dbMatrix = result.DbMatrix;
                _sampleRate = result.SampleRate;
                _maxFrequency = result.SampleRate / 2;
                _totalDurationSeconds = result.DurationSeconds;

                // Tạo WriteableBitmap và nạp pixel mượt mà, an toàn
                uint[] pixelBuffer = new uint[renderWidth * renderHeight];
                for (int y = 0; y < renderHeight; y++)
                {
                    int rowOffset = y * renderWidth;
                    for (int x = 0; x < renderWidth; x++)
                    {
                        float db = result.DbMatrix[x, y];
                        pixelBuffer[rowOffset + x] = DbToColor(db);
                    }
                }

                var bitmap = new WriteableBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Bgr32, null);
                bitmap.WritePixels(new Int32Rect(0, 0, renderWidth, renderHeight), pixelBuffer, renderWidth * sizeof(uint), 0);

                _spectrogramBitmap = bitmap;
                imgSpectrogram.Source = _spectrogramBitmap;

                // Vẽ trục và vạch chỉ dẫn
                RedrawAxesAndGuides();

                gridLoading.Visibility = Visibility.Collapsed;
                txtStatusMessage.Text = $"Phân tích hoàn tất • {System.IO.Path.GetFileName(filePath)}";
            }
            catch (OperationCanceledException)
            {
                // Bị hủy do chuyển bài khác
            }
            catch (Exception ex)
            {
                gridLoading.Visibility = Visibility.Collapsed;
                txtStatusMessage.Text = $"Lỗi phân tích: {ex.Message}";
                MessageBox.Show(this, $"Không thể đọc hoặc phân tích file:\n{ex.Message}", "Lỗi đọc tệp", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private class AnalysisResult
        {
            public float[,] DbMatrix { get; set; } = null!;
            public int SampleRate { get; set; }
            public double DurationSeconds { get; set; }
        }

        private AnalysisResult? ComputeSpectrogramData(string filePath, int width, int height, Action<double, string> reportProgress, CancellationToken token)
        {
            WaveStream? reader = null;
            try
            {
                try
                {
                    reader = new AudioFileReader(filePath);
                }
                catch
                {
                    reader = new MediaFoundationReader(filePath);
                }

                if (reader == null) return null;

                var waveFormat = reader.WaveFormat;
                int sampleRate = waveFormat.SampleRate;
                int channels = waveFormat.Channels;
                var sampleProvider = reader.ToSampleProvider();

                // Đọc toàn bộ audio sang mono float array
                var audioSamples = new System.Collections.Generic.List<float>();
                float[] buffer = new float[4096 * channels];
                int read;
                long totalBytesRead = 0;
                long totalLength = reader.Length;

                while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (token.IsCancellationRequested) return null;

                    for (int i = 0; i < read; i += channels)
                    {
                        float sum = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            sum += buffer[i + ch];
                        }
                        audioSamples.Add(sum / channels);
                    }

                    totalBytesRead += read * sizeof(float);
                    if (totalLength > 0)
                    {
                        double p = Math.Min(30, (double)totalBytesRead / totalLength * 30.0);
                        reportProgress(p, "Đang nạp dữ liệu âm thanh...");
                    }
                }

                float[] samples = audioSamples.ToArray();
                int totalSamples = samples.Length;
                if (totalSamples < FftSize) return null;

                double durationSeconds = (double)totalSamples / sampleRate;
                float[,] dbMatrix = new float[width, height];

                // Precompute Hamming Window
                float[] window = new float[FftSize];
                for (int i = 0; i < FftSize; i++)
                {
                    window[i] = (float)FastFourierTransform.HammingWindow(i, FftSize);
                }

                int m = (int)Math.Log(FftSize, 2.0);
                int fftBins = FftSize / 2; // 1024 bins

                // Tính toán FFT cho từng cột (X: 0 -> width - 1)
                Complex[] fftBuffer = new Complex[FftSize];

                for (int x = 0; x < width; x++)
                {
                    if (token.IsCancellationRequested) return null;

                    // Xác định vị trí mẫu trung tâm
                    long sampleIndex = (long)((double)x / (width - 1) * (totalSamples - FftSize));
                    if (sampleIndex < 0) sampleIndex = 0;
                    if (sampleIndex + FftSize > totalSamples) sampleIndex = totalSamples - FftSize;

                    // Nạp mẫu và nhân Window
                    for (int i = 0; i < FftSize; i++)
                    {
                        fftBuffer[i].X = samples[sampleIndex + i] * window[i];
                        fftBuffer[i].Y = 0f;
                    }

                    FastFourierTransform.FFT(true, m, fftBuffer);

                    // Ánh xạ các tần số vào trục Y (Y=0 là đỉnh maxFreq, Y=height-1 là 0 Hz)
                    for (int y = 0; y < height; y++)
                    {
                        // Bin tương ứng: tần số tuyến tính từ 0 Hz đến Nyquist
                        double freqRatio = (double)(height - 1 - y) / (height - 1);
                        int bin = (int)Math.Clamp(Math.Round(freqRatio * (fftBins - 1)), 0, fftBins - 1);

                        float real = fftBuffer[bin].X;
                        float imag = fftBuffer[bin].Y;
                        float mag = (float)Math.Sqrt(real * real + imag * imag) / (FftSize / 2f);

                        float db = (mag > 1e-6f) ? (float)(20.0 * Math.Log10(mag)) : MinDb;
                        if (db < MinDb) db = MinDb;
                        if (db > MaxDb) db = MaxDb;

                        dbMatrix[x, y] = db;
                    }

                    if (x % 20 == 0)
                    {
                        double p = 30.0 + ((double)x / width * 70.0);
                        reportProgress(p, "Đang phân tích STFT...");
                    }
                }

                return new AnalysisResult
                {
                    DbMatrix = dbMatrix,
                    SampleRate = sampleRate,
                    DurationSeconds = durationSeconds
                };
            }
            finally
            {
                reader?.Dispose();
            }
        }

        #endregion

        #region Color Palette LUT (Spek / Audacity Colormap)

        private static uint[] GenerateColorPalette()
        {
            uint[] lut = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255.0f;
                byte r, g, b;

                if (t < 0.15f)
                {
                    // Đen -> Xanh lam thẫm
                    float f = t / 0.15f;
                    r = 0;
                    g = 0;
                    b = (byte)(f * 120);
                }
                else if (t < 0.35f)
                {
                    // Xanh lam -> Cyan sáng
                    float f = (t - 0.15f) / 0.20f;
                    r = 0;
                    g = (byte)(f * 220);
                    b = (byte)(120 + f * 135);
                }
                else if (t < 0.60f)
                {
                    // Cyan -> Vàng chanh
                    float f = (t - 0.35f) / 0.25f;
                    r = (byte)(f * 255);
                    g = 220;
                    b = (byte)((1.0f - f) * 255);
                }
                else if (t < 0.85f)
                {
                    // Vàng -> Đỏ rực
                    float f = (t - 0.60f) / 0.25f;
                    r = 255;
                    g = (byte)((1.0f - f) * 220);
                    b = 0;
                }
                else
                {
                    // Đỏ -> Trắng chói
                    float f = (t - 0.85f) / 0.15f;
                    r = 255;
                    g = (byte)(f * 255);
                    b = (byte)(f * 255);
                }

                // Format Bgr32: 0xFF000000 | (r << 16) | (g << 8) | b
                lut[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
            }
            return lut;
        }

        private static uint DbToColor(float db)
        {
            float norm = (db - MinDb) / (MaxDb - MinDb);
            norm = Math.Clamp(norm, 0f, 1f);
            int index = (int)(norm * 255);
            return ColorLut[index];
        }

        #endregion

        #region Axes & Guides Drawing

        private void SpectrogramArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_spectrogramBitmap != null)
            {
                RedrawAxesAndGuides();
            }
        }

        private void RedrawAxesAndGuides()
        {
            DrawFrequencyAxis();
            DrawTimeAxis();
            DrawGuideLines();
        }

        private void DrawFrequencyAxis()
        {
            canvasFreqAxis.Children.Clear();
            double h = gridSpectrogramContainer.ActualHeight;
            if (h <= 0 || _maxFrequency <= 0) return;

            // Xác định các mốc tần số cần vẽ (kHz)
            int stepKhz = 5;
            if (_maxFrequency > 48000) stepKhz = 10;
            else if (_maxFrequency <= 24000) stepKhz = 4;

            for (int f = 0; f <= _maxFrequency; f += stepKhz * 1000)
            {
                double y = h - ((double)f / _maxFrequency * h);
                if (y < 0 || y > h) continue;

                // Vạch tick
                var tick = new Line
                {
                    X1 = 55,
                    Y1 = y,
                    X2 = 65,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    StrokeThickness = 1
                };
                canvasFreqAxis.Children.Add(tick);

                // Nhãn chữ
                var txt = new TextBlock
                {
                    Text = f >= 1000 ? $"{f / 1000}k" : $"{f}",
                    Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetRight(txt, 14);
                Canvas.SetTop(txt, Math.Max(0, y - 7));
                canvasFreqAxis.Children.Add(txt);
            }
        }

        private void DrawTimeAxis()
        {
            canvasTimeAxis.Children.Clear();
            double w = gridSpectrogramContainer.ActualWidth;
            if (w <= 0 || _totalDurationSeconds <= 0) return;

            // Chia thành 8 - 10 mốc thời gian
            int numTicks = Math.Max(4, (int)(w / 120));
            double stepSeconds = _totalDurationSeconds / numTicks;

            for (int i = 0; i <= numTicks; i++)
            {
                double sec = i * stepSeconds;
                if (sec > _totalDurationSeconds) sec = _totalDurationSeconds;

                double x = (sec / _totalDurationSeconds) * w;

                // Vạch tick
                var tick = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = 6,
                    Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    StrokeThickness = 1
                };
                canvasTimeAxis.Children.Add(tick);

                // Nhãn thời gian
                var ts = TimeSpan.FromSeconds(sec);
                var txt = new TextBlock
                {
                    Text = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss"),
                    Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                    FontSize = 10.5
                };
                Canvas.SetLeft(txt, Math.Max(0, Math.Min(w - 35, x - 15)));
                Canvas.SetTop(txt, 8);
                canvasTimeAxis.Children.Add(txt);
            }
        }

        private void DrawDbAxis()
        {
            canvasDbAxis.Children.Clear();
            int[] dbs = { 0, -20, -40, -60, -80, -100, -120 };

            for (int i = 0; i < dbs.Length; i++)
            {
                double ratio = (double)i / (dbs.Length - 1);
                var txt = new TextBlock
                {
                    Text = $"{dbs[i]}",
                    Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                    FontSize = 9.5
                };
                Canvas.SetLeft(txt, 2);
                Canvas.SetTop(txt, ratio * 350); // scales inside canvas
                canvasDbAxis.Children.Add(txt);
            }
        }

        private void DrawGuideLines()
        {
            canvasGuideLines.Children.Clear();
            double w = gridSpectrogramContainer.ActualWidth;
            double h = gridSpectrogramContainer.ActualHeight;
            if (w <= 0 || h <= 0 || _maxFrequency <= 0) return;

            // 1. Vạch 16 kHz (MP3 128kbps Cutoff)
            AddGuideLine(16000, "#AAFF5252", "16k (MP3 128k)", w, h);

            // 2. Vạch 20 kHz (MP3 320kbps Cutoff)
            AddGuideLine(20000, "#AAFFB300", "20k (MP3 320k)", w, h);

            // 3. Vạch 22.05 kHz (CD FLAC 44.1k Nyquist)
            if (_maxFrequency >= 22050)
            {
                AddGuideLine(22050, "#AA00E676", "22.05k (CD Lossless)", w, h);
            }
        }

        private void AddGuideLine(int freqHz, string colorHex, string label, double w, double h)
        {
            if (freqHz > _maxFrequency) return;

            double y = h - ((double)freqHz / _maxFrequency * h);
            if (y < 2 || y > h - 2) return;

            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;

            var line = new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = w,
                Y2 = y,
                Stroke = brush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            canvasGuideLines.Children.Add(line);

            var txt = new TextBlock
            {
                Text = label,
                Foreground = brush,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.85
            };
            Canvas.SetRight(txt, 8);
            Canvas.SetTop(txt, y - 14);
            canvasGuideLines.Children.Add(txt);
        }

        #endregion

        #region Hover & Crosshair Measurement

        private void Spectrogram_MouseMove(object sender, MouseEventArgs e)
        {
            if (_spectrogramBitmap == null || _dbMatrix == null || _totalDurationSeconds <= 0)
            {
                lineCursorV.Visibility = Visibility.Collapsed;
                lineCursorH.Visibility = Visibility.Collapsed;
                bdrHoverBadge.Visibility = Visibility.Collapsed;
                return;
            }

            Point p = e.GetPosition(gridSpectrogramContainer);
            double w = gridSpectrogramContainer.ActualWidth;
            double h = gridSpectrogramContainer.ActualHeight;

            if (p.X < 0 || p.X > w || p.Y < 0 || p.Y > h)
            {
                Spectrogram_MouseLeave(sender, e);
                return;
            }

            // Hiển thị đường gióng
            lineCursorV.Visibility = Visibility.Visible;
            lineCursorH.Visibility = Visibility.Visible;
            bdrHoverBadge.Visibility = Visibility.Visible;

            lineCursorV.X1 = p.X;
            lineCursorV.X2 = p.X;
            lineCursorV.Y1 = 0;
            lineCursorV.Y2 = h;

            lineCursorH.X1 = 0;
            lineCursorH.X2 = w;
            lineCursorH.Y1 = p.Y;
            lineCursorH.Y2 = p.Y;

            // Tính toán giá trị đo tại con trỏ
            double timeSeconds = (p.X / w) * _totalDurationSeconds;
            double freqHz = (1.0 - (p.Y / h)) * _maxFrequency;

            int matrixX = (int)Math.Clamp((p.X / w) * _imageWidth, 0, _imageWidth - 1);
            int matrixY = (int)Math.Clamp((p.Y / h) * _imageHeight, 0, _imageHeight - 1);
            float db = _dbMatrix[matrixX, matrixY];

            var ts = TimeSpan.FromSeconds(timeSeconds);
            string timeStr = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss\.f") : ts.ToString(@"mm\:ss\.f");
            string freqStr = freqHz >= 1000 ? $"{freqHz / 1000.0:F2} kHz ({freqHz:N0} Hz)" : $"{freqHz:N0} Hz";

            txtHoverInfo.Text = $"⏱ {timeStr}  |  🔊 {freqStr}  |  📊 {db:F1} dB";

            // Định vị Badge Tooltip gần chuột nhưng không tràn khung
            double badgeLeft = p.X + 15;
            double badgeTop = p.Y - 35;

            if (badgeLeft + 260 > w) badgeLeft = p.X - 265;
            if (badgeTop < 5) badgeTop = p.Y + 15;

            Canvas.SetLeft(bdrHoverBadge, Math.Max(5, badgeLeft));
            Canvas.SetTop(bdrHoverBadge, Math.Max(5, badgeTop));
        }

        private void Spectrogram_MouseLeave(object sender, MouseEventArgs e)
        {
            lineCursorV.Visibility = Visibility.Collapsed;
            lineCursorH.Visibility = Visibility.Collapsed;
            bdrHoverBadge.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Metadata and Export

        private void UpdateFileMetadataHeader(string filePath)
        {
            try
            {
                txtSongTitle.Text = System.IO.Path.GetFileName(filePath);

                var fi = new FileInfo(filePath);
                txtFileSize.Text = $"{fi.Length / (1024.0 * 1024.0):F2} MB";

                string ext = System.IO.Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
                txtFormat.Text = ext;

                // Đọc metadata qua TagLib
                try
                {
                    using var tagFile = TagLib.File.Create(filePath);
                    if (tagFile.Properties != null)
                    {
                        var props = tagFile.Properties;
                        txtSampleRate.Text = $"{props.AudioSampleRate / 1000.0:F1} kHz";
                        txtBitrate.Text = props.BitsPerSample > 0 
                            ? $"{props.AudioBitrate} kbps ({props.BitsPerSample}-bit)" 
                            : $"{props.AudioBitrate} kbps";
                        txtChannels.Text = props.AudioChannels == 1 ? "Mono" : (props.AudioChannels == 2 ? "Stereo" : $"{props.AudioChannels} Kênh");
                        txtDuration.Text = props.Duration.ToString(@"mm\:ss");
                    }
                }
                catch
                {
                    // Fallback
                    txtSampleRate.Text = "--- kHz";
                    txtBitrate.Text = "--- kbps";
                    txtChannels.Text = "Stereo";
                    txtDuration.Text = "--:--";
                }
            }
            catch
            {
                txtSongTitle.Text = System.IO.Path.GetFileName(filePath);
            }
        }

        private void BtnExportImage_Click(object sender, RoutedEventArgs e)
        {
            if (_spectrogramBitmap == null)
            {
                MessageBox.Show(this, "Chưa có ảnh phổ để lưu. Vui lòng mở hoặc kéo thả một tệp âm thanh vào trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Lưu ảnh phổ âm thanh",
                Filter = "PNG Image (*.png)|*.png",
                FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_currentFilePath)}_Spectrogram.png"
            };

            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    // Render toàn bộ vùng vẽ bao gồm cả trục tọa độ
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(_spectrogramBitmap));

                    using (var fs = File.OpenWrite(sfd.FileName))
                    {
                        encoder.Save(fs);
                    }

                    txtStatusMessage.Text = $"Đã lưu ảnh phổ thành công: {System.IO.Path.GetFileName(sfd.FileName)}";
                    MessageBox.Show(this, $"Đã lưu ảnh phổ thành công tại:\n{sfd.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Lỗi lưu ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}

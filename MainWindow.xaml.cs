using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using FresAudio.Services;
using FresAudio.Models;
using FresAudio.Youtube;
using FresAudio.Tiktok;
using FresAudio.Soundcloud;

namespace FresAudio
{
    public partial class MainWindow : Window
    {
        private SettingsService settingsService;
        private PlaylistService playlistService;
        private AudioPlayerService audioPlayerService;

        private int currentSongIndex = -1;
        private int currentPlayRequestId = 0;
        private HashSet<int> _playedSongsInCycle = new HashSet<int>();
        private Random _shuffleRandom = new Random();
        private Stack<int> _playbackHistory = new Stack<int>();
        private bool _isNavigatingBack = false;
        private bool isDraggingTimeline = false;

        private DispatcherTimer visualizerTimer;
        private float[] currentFft = new float[1024];
        
        private const int BAR_COUNT = 64; 
        private Rectangle[] visualizerBars = new Rectangle[BAR_COUNT];
        private const int WM_DEVICECHANGE = 0x0219;
        private int currentThemeMode = 1; // 0: Light, 1: Dark, 2: Transparent
        private ObservableCollection<PlaylistItem> _filteredPlaylist = new ObservableCollection<PlaylistItem>();
        private ObservableCollection<FolderTabItem> _folderTabs = new ObservableCollection<FolderTabItem>();
        private ObservableCollection<FolderTabItem> _playlistTabs = new ObservableCollection<FolderTabItem>();
        private FolderTabItem _selectedTab = null;
        private FolderTabItem _playbackTab = null;
        private bool _isRightClicking = false;

        public MainWindow()
        {
            InitializeComponent();
            lstPlaylist.ItemsSource = _filteredPlaylist;
            itemsFolderTabs.ItemsSource = _folderTabs;
            itemsPlaylistTabs.ItemsSource = _playlistTabs;
            
            settingsService = new SettingsService();
            playlistService = new PlaylistService(settingsService);
            audioPlayerService = new AudioPlayerService();
            
            audioPlayerService.FftCalculated += AudioPlayerService_FftCalculated;
            audioPlayerService.PlaybackStopped += AudioPlayerService_PlaybackStopped;
            audioPlayerService.DefaultDeviceChanged += AudioPlayerService_DefaultDeviceChanged;

            InitVisualizer();
            UpdatePlaylistTabs();
            RefreshPlaylistView();
            LoadInitialData();

            Dispatcher.InvokeAsync(() => LoadAudioDevices(), DispatcherPriority.Background);
            
            // Không khôi phục giao diện cũ, luôn mặc định là Dark Mode (1) khi mở app
            currentThemeMode = 1; 
            ApplyTheme();
            
            iconShuffle.Foreground = settingsService.CurrentSettings.IsShuffle 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3b82f6")) 
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a1a1aa"));
                
            UpdateRepeatIcon();
        }

        private async void LoadInitialData()
        {
            var args = Environment.GetCommandLineArgs();
            string filePath = string.Join(" ", args.Skip(1)).Trim('"', ' ');

            // Luôn nạp lại toàn bộ các thư mục cũ từ phiên trước (không tự ý nhảy tab khi đang nạp hàng loạt)
            var savedFolders = settingsService.CurrentSettings.LastFolders.ToList();
            settingsService.ClearLastFolders(); 
            foreach (var folder in savedFolders)
            {
                if (System.IO.Directory.Exists(folder))
                {
                    await LoadPlaylist(folder, append: true, autoSelectTab: false);
                }
            }

            if (args.Length > 1 && System.IO.File.Exists(filePath))
            {
                string targetFolder = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(targetFolder))
                {
                    // Nếu thư mục chứa bài hát chưa có trong danh sách thì nạp thêm
                    if (!playlistService.LoadedFolders.Any(f => string.Equals(f, targetFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        await LoadPlaylist(targetFolder, append: true, recurse: false, autoSelectTab: false);
                    }
                }

                UpdateFolderTabs();

                // Tự động chuyển tab sang thư mục của bài hát đó (nếu có)
                if (!string.IsNullOrEmpty(targetFolder))
                {
                    var targetTab = _folderTabs.FirstOrDefault(t => !t.IsAllTab && string.Equals(t.FolderPath, targetFolder, StringComparison.OrdinalIgnoreCase));
                    if (targetTab != null)
                    {
                        _playbackTab = targetTab;
                        SelectFolderTab(targetTab);
                    }
                }

                int songIndex = playlistService.GetSongIndex(filePath);
                if (songIndex >= 0)
                {
                    currentSongIndex = songIndex;
                    SyncPlaylistSelection();
                    PlaySong();
                }
            }
            else
            {
                UpdateFolderTabs();
                var firstTab = _folderTabs.FirstOrDefault(t => t.IsAllTab) ?? _folderTabs.FirstOrDefault();
                if (firstTab != null)
                {
                    SelectFolderTab(firstTab);
                }
                else
                {
                    RefreshPlaylistView();
                }
                scrollFolderTabs?.ScrollToHorizontalOffset(0);
            }
        }

        public async void OpenFileFromExternal(string filePath)
        {
            try
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();

                filePath = filePath.Trim('"', ' ');
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

                string targetFolder = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(targetFolder))
                {
                    if (!playlistService.LoadedFolders.Any(f => string.Equals(f, targetFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        await LoadPlaylist(targetFolder, append: true, recurse: false, autoSelectTab: false);
                    }
                }

                UpdateFolderTabs();

                if (!string.IsNullOrEmpty(targetFolder))
                {
                    var targetTab = _folderTabs.FirstOrDefault(t => !t.IsAllTab && string.Equals(t.FolderPath, targetFolder, StringComparison.OrdinalIgnoreCase));
                    if (targetTab != null)
                    {
                        _playbackTab = targetTab;
                        SelectFolderTab(targetTab);
                    }
                }

                int songIndex = playlistService.GetSongIndex(filePath);
                if (songIndex >= 0)
                {
                    currentSongIndex = songIndex;
                    SyncPlaylistSelection();
                    PlaySong();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OpenFileFromExternal error: " + ex.Message);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(new HwndSourceHook(WndProc));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                Dispatcher.InvokeAsync(() => RefreshAudioDevicesList());
            }
            else if (msg == 0x0024) // WM_GETMINMAXINFO
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)(System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(MINMAXINFO)) ?? new MINMAXINFO());
            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);
                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
            }
            
            // Enforce minimum window size (logical 400x620)
            PresentationSource source = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            double dpiY = 1.0;
            if (source != null && source.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            mmi.ptMinTrackSize.x = (int)(380 * dpiX);
            mmi.ptMinTrackSize.y = (int)(150 * dpiY);
            
            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
        }

        internal struct POINT { public int x; public int y; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct MINMAXINFO { public POINT ptReserved; public POINT ptMaxSize; public POINT ptMaxPosition; public POINT ptMinTrackSize; public POINT ptMaxTrackSize; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        internal class MONITORINFO { public int cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO)); public RECT rcMonitor = new RECT(); public RECT rcWork = new RECT(); public int dwFlags = 0; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct RECT { public int left, top, right, bottom; }
        [System.Runtime.InteropServices.DllImport("user32")]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);
        [System.Runtime.InteropServices.DllImport("User32")]
        internal static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        private void RefreshAudioDevicesList()
        {
            string currentSelectedName = cboAudioDevices.SelectedIndex >= 0 ? cboAudioDevices.SelectedItem as string : null;

            cboAudioDevices.SelectionChanged -= CboAudioDevices_SelectionChanged; 
            
            var devices = audioPlayerService.GetAudioDevices();
            cboAudioDevices.ItemsSource = devices;
            
            int newSelectedIndex = 0;
            if (currentSelectedName != null)
            {
                int index = devices.IndexOf(currentSelectedName);
                if (index >= 0) newSelectedIndex = index;
            }

            cboAudioDevices.SelectedIndex = newSelectedIndex;
            audioPlayerService.SelectedDeviceNumber = newSelectedIndex - 1;
            
            cboAudioDevices.SelectionChanged += CboAudioDevices_SelectionChanged; 
            
            if (currentSelectedName != null && newSelectedIndex == 0 && currentSelectedName != "Mặc định hệ thống")
            {
                if (audioPlayerService.IsPlaying)
                {
                    audioPlayerService.ChangeDevice(audioPlayerService.SelectedDeviceNumber);
                }
            }
        }

        private void LoadAudioDevices()
        {
            cboAudioDevices.ItemsSource = audioPlayerService.GetAudioDevices();
            cboAudioDevices.SelectedIndex = 0;
        }

        private void CboAudioDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboAudioDevices.SelectedIndex >= 0)
            {
                int newDeviceNumber = cboAudioDevices.SelectedIndex - 1;
                if (newDeviceNumber != audioPlayerService.SelectedDeviceNumber)
                {
                    audioPlayerService.ChangeDevice(newDeviceNumber);
                }
            }
        }

        private void InitVisualizer()
        {
            for (int i = 0; i < BAR_COUNT; i++)
            {
                var rect = new Rectangle
                {
                    Width = 3,
                    Height = 2,
                    SnapsToDevicePixels = true 
                };
                rect.SetResourceReference(Rectangle.FillProperty, "TextPrimary");
                
                Canvas.SetLeft(rect, i * 4); 
                Canvas.SetTop(rect, 29);

                canvasVisualizer.Children.Add(rect);
                visualizerBars[i] = rect;
            }

            visualizerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            visualizerTimer.Tick += VisualizerTimer_Tick;
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            if (e.OriginalSource is DependencyObject source)
            {
                bool clickedInsideSearch = false;
                bool clickedInsidePlaylist = false;
                DependencyObject current = source;
                while (current != null)
                {
                    if (current == txtSearch) clickedInsideSearch = true;
                    if (current == lstPlaylist) clickedInsidePlaylist = true;
                    
                    if (clickedInsideSearch && clickedInsidePlaylist) break;

                    current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
                }

                if (!clickedInsideSearch && txtSearch.IsFocused)
                {
                    FocusManager.SetFocusedElement(FocusManager.GetFocusScope(txtSearch), null);
                    Keyboard.ClearFocus();
                }

                if (!clickedInsidePlaylist && lstPlaylist.SelectedItem != null)
                {
                    lstPlaylist.SelectedItem = null;
                }
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.GetPosition(this).Y < this.ActualHeight - 100)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            }
        }

        private bool _isKeyboardNavigating = false;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(e.OriginalSource is TextBox))
            {
                if (e.Key == Key.Space)
                {
                    e.Handled = true;
                    BtnPlayPause_Click(btnPlayPause, new RoutedEventArgs());
                    return;
                }

                if (e.Key >= Key.A && e.Key <= Key.Z)
                {
                    string keyStr = e.Key.ToString().ToLowerInvariant();
                    int startIndex = 0;
                    if (lstPlaylist.SelectedItem is PlaylistItem selectedItem)
                    {
                        int currentListIndex = _filteredPlaylist.IndexOf(selectedItem);
                        if (currentListIndex >= 0) startIndex = currentListIndex + 1;
                    }

                    PlaylistItem targetItem = null;
                    for (int i = startIndex; i < _filteredPlaylist.Count; i++)
                    {
                        if (_filteredPlaylist[i].SearchString.StartsWith(keyStr)) { targetItem = _filteredPlaylist[i]; break; }
                    }
                    if (targetItem == null)
                    {
                        for (int i = 0; i < startIndex; i++)
                        {
                            if (_filteredPlaylist[i].SearchString.StartsWith(keyStr)) { targetItem = _filteredPlaylist[i]; break; }
                        }
                    }

                    if (targetItem != null)
                    {
                        _isKeyboardNavigating = true;
                        lstPlaylist.SelectedItem = targetItem;
                        lstPlaylist.ScrollIntoView(targetItem);
                        _isKeyboardNavigating = false;
                    }
                    e.Handled = true;
                }
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.ActualWidth < 700)
            {
                mainContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                mainContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
                
                mainContentGrid.RowDefinitions[0].Height = GridLength.Auto;
                mainContentGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);

                Grid.SetColumnSpan(leftPanel, 2);
                Grid.SetRowSpan(leftPanel, 1);
                Grid.SetColumn(leftPanel, 0);
                Grid.SetRow(leftPanel, 0);

                Grid.SetColumnSpan(rightPanel, 2);
                Grid.SetRowSpan(rightPanel, 1);
                Grid.SetColumn(rightPanel, 0);
                Grid.SetRow(rightPanel, 1);

                // Squeeze sizes and margins to fit 3 songs in the playlist below
                bdrAlbumArt.Width = 80;
                bdrAlbumArt.Height = 80;
                clipAlbumArt.Rect = new Rect(0, 0, 80, 80);
                leftPanel.Padding = new Thickness(10, 5, 10, 0);
                
                // Restore visualizer but squeeze its margin
                canvasVisualizer.Margin = new Thickness(0, 5, 0, 0);
                canvasVisualizer.Visibility = Visibility.Visible;
                
                txtTitle.Margin = new Thickness(0, 5, 0, 0);
                rightPanel.Margin = new Thickness(15, 10, 15, 0);
                lstPlaylist.Margin = new Thickness(0, 10, 0, 0);

                // Responsive Bottom Controls
                Grid.SetRow(viewboxControls, 0);
                Grid.SetColumn(viewboxControls, 0);
                Grid.SetColumnSpan(viewboxControls, 3);

                Grid.SetRow(pnlTime, 1);
                Grid.SetColumn(pnlTime, 0);
                Grid.SetColumnSpan(pnlTime, 1);
                pnlTime.Margin = new Thickness(0, 12, 0, 0);

                Grid.SetRow(pnlVolume, 1);
                Grid.SetColumn(pnlVolume, 2);
                Grid.SetColumnSpan(pnlVolume, 1);
                pnlVolume.Margin = new Thickness(0, 12, 0, 0);

                if (sliderVolume != null) sliderVolume.Width = 65;
            }
            else
            {
                mainContentGrid.ColumnDefinitions[0].Width = new GridLength(350);
                mainContentGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                
                mainContentGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                mainContentGrid.RowDefinitions[1].Height = new GridLength(0);

                Grid.SetColumnSpan(leftPanel, 1);
                Grid.SetRowSpan(leftPanel, 2);
                Grid.SetColumn(leftPanel, 0);
                Grid.SetRow(leftPanel, 0);

                Grid.SetColumnSpan(rightPanel, 1);
                Grid.SetRowSpan(rightPanel, 2);
                Grid.SetColumn(rightPanel, 1);
                Grid.SetRow(rightPanel, 0);

                // Restore Original Sizes and Margins
                bdrAlbumArt.Width = 240;
                bdrAlbumArt.Height = 240;
                clipAlbumArt.Rect = new Rect(0, 0, 240, 240);
                leftPanel.Padding = new Thickness(20, 20, 20, 0);
                
                canvasVisualizer.Margin = new Thickness(0, 20, 0, 0);
                canvasVisualizer.Visibility = Visibility.Visible;
                
                txtTitle.Margin = new Thickness(0, 10, 0, 0);
                rightPanel.Margin = new Thickness(20);
                lstPlaylist.Margin = new Thickness(0, 15, 0, 0);

                // Standard Bottom Controls
                Grid.SetRow(viewboxControls, 0);
                Grid.SetColumn(viewboxControls, 1);
                Grid.SetColumnSpan(viewboxControls, 1);

                Grid.SetRow(pnlTime, 0);
                Grid.SetColumn(pnlTime, 0);
                Grid.SetColumnSpan(pnlTime, 1);
                pnlTime.Margin = new Thickness(0);

                Grid.SetRow(pnlVolume, 0);
                Grid.SetColumn(pnlVolume, 2);
                Grid.SetColumnSpan(pnlVolume, 1);
                pnlVolume.Margin = new Thickness(0);

                if (sliderVolume != null) sliderVolume.Width = 100;
            }
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            currentThemeMode = currentThemeMode switch
            {
                1 => 0, // Dark -> Light
                0 => 2, // Light -> Transparent Dark
                2 => 3, // Transparent Dark -> Transparent Light
                3 => 1, // Transparent Light -> Dark
                _ => 1
            };
            
            settingsService.CurrentSettings.ThemeMode = currentThemeMode;
            settingsService.CurrentSettings.IsDarkMode = currentThemeMode == 1 || currentThemeMode == 2;
            settingsService.SaveSettings();
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            var appResources = Application.Current.Resources.MergedDictionaries;
            var oldThemeDict = appResources.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Themes/"));
            if (oldThemeDict != null)
            {
                appResources.Remove(oldThemeDict);
            }

            string newThemeStr = currentThemeMode switch
            {
                0 => "Themes/Light.xaml",
                1 => "Themes/Dark.xaml",
                2 => "Themes/Transparent.xaml",
                3 => "Themes/TransparentLight.xaml",
                _ => "Themes/Dark.xaml"
            };

            appResources.Insert(0, new ResourceDictionary { Source = new Uri(newThemeStr, UriKind.Relative) });

            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme((currentThemeMode == 0 || currentThemeMode == 3) ? BaseTheme.Light : BaseTheme.Dark);
            paletteHelper.SetTheme(theme);

            iconTheme.Kind = currentThemeMode switch
            {
                0 => PackIconKind.WeatherSunny,
                1 => PackIconKind.WeatherNight,
                2 => PackIconKind.WaterOutline,
                3 => PackIconKind.Water,
                _ => PackIconKind.WeatherNight
            };
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            
            if (this.WindowState == WindowState.Maximized)
            {
                RootBorder.Margin = new Thickness(0);
                TitleBarRow.Height = new GridLength(40);
                iconMaximize.Text = "\uE923"; 
                RootBorder.CornerRadius = new CornerRadius(0);
                RootBorder.BorderThickness = new Thickness(0);
            }
            else
            {
                RootBorder.Margin = new Thickness(0);
                TitleBarRow.Height = new GridLength(32);
                iconMaximize.Text = "\uE922"; 
                RootBorder.CornerRadius = new CornerRadius(15);
                RootBorder.BorderThickness = new Thickness(1);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            StopSong();
            this.Close();
        }

        private void BtnFolder_Click(object sender, RoutedEventArgs e)
        {
            try 
            {
                var dialog = new FolderBrowserWindow
                {
                    Owner = this,
                    FolderSelected = (folderPath, files) =>
                    {
                        if (!string.IsNullOrEmpty(folderPath))
                        {
                            _ = LoadPlaylist(folderPath, files, append: true);
                        }
                    }
                };
                dialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("CRASH: " + ex.Message);
            }
        }

        private YoutubeDownloadWindow _activeYoutubeWindow = null;
        private FresAudio.Spotify.SpotifyDownloadWindow _activeSpotifyWindow = null;
        private TiktokDownloadWindow _activeTiktokWindow = null;
        private SoundcloudDownloadWindow _activeSoundcloudWindow = null;

        private void BtnYoutube_Click(object sender, RoutedEventArgs e)
        {
            if (_activeYoutubeWindow != null && _activeYoutubeWindow.IsLoaded)
            {
                if (_activeYoutubeWindow.WindowState == WindowState.Minimized)
                    _activeYoutubeWindow.WindowState = WindowState.Normal;
                _activeYoutubeWindow.Activate();
                return;
            }

            string currentFolder = (_selectedTab != null && !_selectedTab.IsAllTab && !string.IsNullOrEmpty(_selectedTab.FolderPath))
                ? _selectedTab.FolderPath
                : (playlistService.LoadedFolders.Count > 0 ? playlistService.LoadedFolders[0] : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

            _activeYoutubeWindow = new YoutubeDownloadWindow(currentFolder, playlistService.LoadedFolders) { Owner = this };
            
            _activeYoutubeWindow.DownloadCompleted += (s, downloadedPath) =>
            {
                if (!string.IsNullOrEmpty(downloadedPath))
                {
                    string targetFolder = _activeYoutubeWindow?.DestinationFolder ?? currentFolder;
                    _ = LoadPlaylist(targetFolder, new System.Collections.Generic.List<string> { downloadedPath }, append: true, recurse: false, insertAtTop: true);
                }
            };
            
            _activeYoutubeWindow.Closed += (s, args) => _activeYoutubeWindow = null;
            _activeYoutubeWindow.Show();
        }

        private void BtnSpotify_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSpotifyWindow != null && _activeSpotifyWindow.IsLoaded)
            {
                if (_activeSpotifyWindow.WindowState == WindowState.Minimized)
                    _activeSpotifyWindow.WindowState = WindowState.Normal;
                _activeSpotifyWindow.Activate();
                return;
            }

            string currentFolder = (_selectedTab != null && !_selectedTab.IsAllTab && !string.IsNullOrEmpty(_selectedTab.FolderPath))
                ? _selectedTab.FolderPath
                : (playlistService.LoadedFolders.Count > 0 ? playlistService.LoadedFolders[0] : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

            _activeSpotifyWindow = new FresAudio.Spotify.SpotifyDownloadWindow(currentFolder, playlistService.LoadedFolders) { Owner = this };

            _activeSpotifyWindow.DownloadCompleted += (s, downloadedPath) =>
            {
                if (!string.IsNullOrEmpty(downloadedPath))
                {
                    string targetFolder = _activeSpotifyWindow?.DestinationFolder ?? currentFolder;
                    _ = LoadPlaylist(targetFolder, new System.Collections.Generic.List<string> { downloadedPath }, append: true, recurse: false, insertAtTop: true);
                }
            };

            _activeSpotifyWindow.Closed += (s, args) => _activeSpotifyWindow = null;
            _activeSpotifyWindow.Show();
        }

        private void BtnTiktok_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTiktokWindow != null && _activeTiktokWindow.IsLoaded)
            {
                if (_activeTiktokWindow.WindowState == WindowState.Minimized)
                    _activeTiktokWindow.WindowState = WindowState.Normal;
                _activeTiktokWindow.Activate();
                return;
            }

            string currentFolder = (_selectedTab != null && !_selectedTab.IsAllTab && !string.IsNullOrEmpty(_selectedTab.FolderPath))
                ? _selectedTab.FolderPath
                : (playlistService.LoadedFolders.Count > 0 ? playlistService.LoadedFolders[0] : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

            _activeTiktokWindow = new TiktokDownloadWindow(currentFolder, playlistService.LoadedFolders) { Owner = this };

            _activeTiktokWindow.DownloadCompleted += (s, downloadedPath) =>
            {
                if (!string.IsNullOrEmpty(downloadedPath))
                {
                    string targetFolder = _activeTiktokWindow?.DestinationFolder ?? currentFolder;
                    _ = LoadPlaylist(targetFolder, new System.Collections.Generic.List<string> { downloadedPath }, append: true, recurse: false, insertAtTop: true);
                }
            };

            _activeTiktokWindow.Closed += (s, args) => _activeTiktokWindow = null;
            _activeTiktokWindow.Show();
        }

        private void BtnSoundcloud_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSoundcloudWindow != null && _activeSoundcloudWindow.IsLoaded)
            {
                if (_activeSoundcloudWindow.WindowState == WindowState.Minimized)
                    _activeSoundcloudWindow.WindowState = WindowState.Normal;
                _activeSoundcloudWindow.Activate();
                return;
            }

            string currentFolder = (_selectedTab != null && !_selectedTab.IsAllTab && !string.IsNullOrEmpty(_selectedTab.FolderPath))
                ? _selectedTab.FolderPath
                : (playlistService.LoadedFolders.Count > 0 ? playlistService.LoadedFolders[0] : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

            _activeSoundcloudWindow = new SoundcloudDownloadWindow(currentFolder, playlistService.LoadedFolders) { Owner = this };

            _activeSoundcloudWindow.DownloadCompleted += (s, downloadedPath) =>
            {
                if (!string.IsNullOrEmpty(downloadedPath))
                {
                    string targetFolder = _activeSoundcloudWindow?.DestinationFolder ?? currentFolder;
                    _ = LoadPlaylist(targetFolder, new System.Collections.Generic.List<string> { downloadedPath }, append: true, recurse: false, insertAtTop: true);
                }
            };

            _activeSoundcloudWindow.Closed += (s, args) => _activeSoundcloudWindow = null;
            _activeSoundcloudWindow.Show();
        }

        private void BtnRemoveSong_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            DependencyObject item = VisualTreeHelper.GetParent(button);
            while (item != null && !(item is ListBoxItem))
            {
                item = VisualTreeHelper.GetParent(item);
            }

            if (item is ListBoxItem listBoxItem && listBoxItem.DataContext is FresAudio.Models.PlaylistItem playItem)
            {
                e.Handled = true;

                // NẾU ĐANG Ở TRONG CUSTOM PLAYLIST: Chỉ loại bỏ bài hát khỏi Playlist này, không mở dialog xác nhận xóa file
                if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
                {
                    string targetPath = playItem.FilePath;
                    if (string.IsNullOrEmpty(targetPath) && playItem.OriginalIndex >= 0 && playItem.OriginalIndex < playlistService.PlaylistPaths.Count)
                    {
                        targetPath = playlistService.PlaylistPaths[playItem.OriginalIndex];
                    }

                    if (!string.IsNullOrEmpty(targetPath))
                    {
                        if (_selectedTab.IsAllTab)
                        {
                            var customPlaylists = settingsService.CurrentSettings.CustomPlaylists ?? new List<CustomPlaylist>();
                            foreach (var pl in customPlaylists)
                            {
                                pl.SongPaths.RemoveAll(p => string.Equals(p, targetPath, StringComparison.OrdinalIgnoreCase));
                            }
                        }
                        else
                        {
                            var targetPl = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == _selectedTab.PlaylistId);
                            targetPl?.SongPaths.RemoveAll(p => string.Equals(p, targetPath, StringComparison.OrdinalIgnoreCase));
                        }
                        settingsService.SaveSettings();
                        UpdatePlaylistTabs();
                        RefreshPlaylistView();
                    }
                    return;
                }

                int originalIndex = playItem.OriginalIndex;
                if (originalIndex >= 0 && originalIndex < playlistService.PlaylistPaths.Count)
                {
                    string removedFilePath = playlistService.PlaylistPaths[originalIndex];

                    var dialog = new DeleteSongDialog(playItem.DisplayName)
                    {
                        Owner = this,
                        OptionSelected = (deleteOption) =>
                        {
                            if (deleteOption == DeleteSongOption.Cancel) return;

                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                int songIdx = playlistService.PlaylistPaths.IndexOf(removedFilePath);
                                if (songIdx < 0) return;

                                bool isRemovingPlayingSong = (currentSongIndex == songIdx);

                                if (isRemovingPlayingSong)
                                {
                                    StopSong();
                                }
                                else if (currentSongIndex > songIdx)
                                {
                                    currentSongIndex--;
                                }

                                if (deleteOption == DeleteSongOption.DeleteFromDisk)
                                {
                                    try
                                    {
                                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                            removedFilePath,
                                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                                    }
                                    catch
                                    {
                                        try
                                        {
                                            if (System.IO.File.Exists(removedFilePath))
                                            {
                                                System.IO.File.Delete(removedFilePath);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show($"Không thể xóa file trên ổ cứng: {ex.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        }
                                    }
                                }
                                else
                                {
                                    settingsService.CurrentSettings.RemovedSongs.Add(removedFilePath);
                                    settingsService.SaveSettings();
                                }

                                playlistService.RemoveSong(songIdx);
                                RefreshPlaylistView();

                                if (isRemovingPlayingSong)
                                {
                                    if (playlistService.PlaylistPaths.Count > 0)
                                    {
                                        currentSongIndex = currentSongIndex >= playlistService.PlaylistPaths.Count ? 0 : currentSongIndex;
                                        SyncPlaylistSelection();
                                        PlaySong();
                                    }
                                    else
                                    {
                                        currentSongIndex = -1;
                                        ResetUI();
                                    }
                                }

                                UpdateFolderTabs();
                            });
                        }
                    };
                    dialog.Show();
                }
            }
        }

        private void ResetUI()
        {
            txtTitle.Text = "Chưa chọn bài hát";
            txtArtist.Text = "Unknown Artist";
            imgAlbumArt.Source = null;
            txtCurrentTime.Text = "00:00";
            txtTotalTime.Text = "00:00";
            sliderTimeline.Value = 0;
            if (txtFormat != null) txtFormat.Text = "---";
            if (txtBitrate != null) txtBitrate.Text = "--- kbps";
            if (txtSampleRate != null) txtSampleRate.Text = "--- kHz";
            if (txtFileSize != null) txtFileSize.Text = "--- MB";
        }

        private async System.Threading.Tasks.Task LoadPlaylist(string folder, List<string> explicitFiles = null, bool append = false, bool recurse = true, bool insertAtTop = false, bool autoSelectTab = true)
        {
            try
            {
                int previousCount = playlistService.PlaylistPaths.Count;
                var (newPaths, newDisplayNames) = await playlistService.LoadPlaylistAsync(folder, explicitFiles, append, recurse, insertAtTop);

                if (previousCount == 0 && playlistService.PlaylistPaths.Count > 0)
                {
                    currentSongIndex = -1;
                }
                else if (insertAtTop && newPaths.Count > 0)
                {
                    if (currentSongIndex >= 0)
                    {
                        currentSongIndex += newPaths.Count;
                    }
                }

                UpdateFolderTabs();

                // Nếu vừa mở một thư mục cụ thể và được phép tự chọn tab thì tự động chuyển đến tab của thư mục đó
                if (autoSelectTab && !string.IsNullOrEmpty(folder))
                {
                    var newlyAddedTab = _folderTabs.FirstOrDefault(t => !t.IsAllTab && string.Equals(t.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
                    if (newlyAddedTab != null)
                    {
                        SelectFolderTab(newlyAddedTab);
                        return;
                    }
                }

                RefreshPlaylistView();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi đọc thư mục: " + ex.Message);
            }
        }

        private void TabsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                double step = 80;
                if (e.Delta < 0)
                    scrollViewer.ScrollToHorizontalOffset(Math.Min(scrollViewer.ScrollableWidth, scrollViewer.HorizontalOffset + step));
                else
                    scrollViewer.ScrollToHorizontalOffset(Math.Max(0, scrollViewer.HorizontalOffset - step));
                e.Handled = true;
            }
        }

        private void ScrollFolderTabs_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateTabScrollButtons();
        }

        private void BtnScrollTabsLeft_Click(object sender, RoutedEventArgs e)
        {
            if (scrollFolderTabs == null) return;
            double target = Math.Max(0, scrollFolderTabs.HorizontalOffset - 120);
            scrollFolderTabs.ScrollToHorizontalOffset(target);
        }

        private void BtnScrollTabsRight_Click(object sender, RoutedEventArgs e)
        {
            if (scrollFolderTabs == null) return;
            double target = Math.Min(scrollFolderTabs.ScrollableWidth, scrollFolderTabs.HorizontalOffset + 120);
            scrollFolderTabs.ScrollToHorizontalOffset(target);
        }

        private void UpdateTabScrollButtons()
        {
            if (scrollFolderTabs == null || btnScrollTabsLeft == null || btnScrollTabsRight == null) return;
            
            bool canScrollLeft = scrollFolderTabs.HorizontalOffset > 0.5;
            bool canScrollRight = scrollFolderTabs.HorizontalOffset < (scrollFolderTabs.ScrollableWidth - 0.5);

            btnScrollTabsLeft.Visibility = canScrollLeft ? Visibility.Visible : Visibility.Collapsed;
            btnScrollTabsRight.Visibility = canScrollRight ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnScrollPlaylistsLeft_Click(object sender, RoutedEventArgs e)
        {
            if (scrollPlaylistTabs == null) return;
            double target = Math.Max(0, scrollPlaylistTabs.HorizontalOffset - 120);
            scrollPlaylistTabs.ScrollToHorizontalOffset(target);
        }

        private void BtnScrollPlaylistsRight_Click(object sender, RoutedEventArgs e)
        {
            if (scrollPlaylistTabs == null) return;
            double target = Math.Min(scrollPlaylistTabs.ScrollableWidth, scrollPlaylistTabs.HorizontalOffset + 120);
            scrollPlaylistTabs.ScrollToHorizontalOffset(target);
        }

        private void PlaylistTabsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (scrollPlaylistTabs == null) return;
            if (e.Delta < 0)
            {
                scrollPlaylistTabs.ScrollToHorizontalOffset(scrollPlaylistTabs.HorizontalOffset + 40);
            }
            else
            {
                scrollPlaylistTabs.ScrollToHorizontalOffset(Math.Max(0, scrollPlaylistTabs.HorizontalOffset - 40));
            }
            e.Handled = true;
        }

        private void ScrollPlaylistTabs_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdatePlaylistTabScrollButtons();
        }

        private void UpdatePlaylistTabScrollButtons()
        {
            if (scrollPlaylistTabs == null || btnScrollPlaylistsLeft == null || btnScrollPlaylistsRight == null) return;

            bool canScrollLeft = scrollPlaylistTabs.HorizontalOffset > 0.5;
            bool canScrollRight = scrollPlaylistTabs.HorizontalOffset < (scrollPlaylistTabs.ScrollableWidth - 0.5);

            btnScrollPlaylistsLeft.Visibility = canScrollLeft ? Visibility.Visible : Visibility.Collapsed;
            btnScrollPlaylistsRight.Visibility = canScrollRight ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ScrollPlaylistTabIntoView(FolderTabItem tab)
        {
            if (tab == null || scrollPlaylistTabs == null || itemsPlaylistTabs == null) return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var container = itemsPlaylistTabs.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;
                    if (container != null)
                    {
                        container.BringIntoView();
                    }
                    else
                    {
                        int index = _playlistTabs.IndexOf(tab);
                        if (index >= 0)
                        {
                            double estimatedOffset = index * 110.0;
                            scrollPlaylistTabs.ScrollToHorizontalOffset(estimatedOffset);
                        }
                    }
                }
                catch { }
            }, DispatcherPriority.Loaded);
        }

        private void FolderTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is FolderTabItem tab)
            {
                SelectFolderTab(tab);
            }
        }

        private void PlaylistTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is FolderTabItem tab)
            {
                if (tab.IsAllTab) return; // Tab "Tổng Playlist" chỉ hiển thị thông tin, không cho chọn / bấm vào
                SelectPlaylistTab(tab);
            }
        }

        private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is FolderTabItem tab)
            {
                e.Handled = true;
                CloseFolderTab(tab);
            }
        }

        private void BtnClosePlaylistTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is FolderTabItem tab)
            {
                e.Handled = true;
                DeletePlaylist(tab);
            }
        }

        private void SelectFolderTab(FolderTabItem tab)
        {
            if (tab == null) return;
            if (!string.IsNullOrEmpty(txtSearch?.Text))
            {
                txtSearch.Text = string.Empty;
            }
            _selectedTab = tab;
            foreach (var t in _folderTabs)
            {
                t.IsSelected = (t == tab);
            }
            foreach (var t in _playlistTabs)
            {
                t.IsSelected = false;
            }
            RefreshPlaylistView();
            ScrollTabIntoView(tab);
        }

        private void SelectPlaylistTab(FolderTabItem tab)
        {
            if (tab == null || tab.IsAllTab) return;
            if (!string.IsNullOrEmpty(txtSearch?.Text))
            {
                txtSearch.Text = string.Empty;
            }
            _selectedTab = tab;
            foreach (var t in _playlistTabs)
            {
                t.IsSelected = (!t.IsAllTab && t == tab);
            }
            foreach (var t in _folderTabs)
            {
                t.IsSelected = false;
            }
            RefreshPlaylistView();
            ScrollPlaylistTabIntoView(tab);
        }

        private void ScrollTabIntoView(FolderTabItem tab)
        {
            if (tab == null || scrollFolderTabs == null || itemsFolderTabs == null) return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var container = itemsFolderTabs.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;
                    if (container != null)
                    {
                        container.BringIntoView();
                    }
                    else
                    {
                        int index = _folderTabs.IndexOf(tab);
                        if (index >= 0)
                        {
                            double estimatedOffset = index * 110.0;
                            scrollFolderTabs.ScrollToHorizontalOffset(estimatedOffset);
                        }
                    }
                }
                catch { }
            }, DispatcherPriority.Loaded);
        }

        private void CloseFolderTab(FolderTabItem tab)
        {
            if (tab == null || tab.IsAllTab) return;

            bool isPlaybackTab = (_playbackTab != null && !_playbackTab.IsAllTab && !string.IsNullOrEmpty(_playbackTab.FolderPath) && string.Equals(_playbackTab.FolderPath, tab.FolderPath, StringComparison.OrdinalIgnoreCase));

            string playingFolder = (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistFolderOrigins.Count)
                ? playlistService.PlaylistFolderOrigins[currentSongIndex]
                : null;

            bool isCurrentPlayingFolder = !string.IsNullOrEmpty(playingFolder) &&
                !string.IsNullOrEmpty(tab.FolderPath) &&
                string.Equals(
                    System.IO.Path.GetFullPath(playingFolder).TrimEnd('\\', '/'),
                    System.IO.Path.GetFullPath(tab.FolderPath).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);

            playlistService.RemoveFolder(tab.FolderPath);

            // Nếu bài đang phát thuộc thư mục bị đóng HOẶC tab đang phát bị đóng HOẶC đã đóng toàn bộ thư mục -> Dừng phát ngay lập tức và reset UI
            if (isPlaybackTab || isCurrentPlayingFolder || playlistService.LoadedFolders.Count == 0 || playlistService.PlaylistPaths.Count == 0)
            {
                StopSong();
                currentSongIndex = -1;
                _playbackTab = null;
                ResetUI();
            }

            if (_selectedTab == tab)
            {
                _selectedTab = _folderTabs.FirstOrDefault(t => t.IsAllTab);
            }

            UpdateFolderTabs();
            RefreshPlaylistView();
        }

        private void DeletePlaylist(FolderTabItem tab)
        {
            if (tab == null) return;
            var playlist = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == tab.PlaylistId);
            if (playlist != null)
            {
                var result = MessageBox.Show($"Bạn có chắc muốn xóa playlist \"{playlist.Name}\" không?", "Xác nhận xóa Playlist", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    if (_playbackTab == tab || (_playbackTab != null && string.Equals(_playbackTab.PlaylistId, tab.PlaylistId, StringComparison.OrdinalIgnoreCase)))
                    {
                        _playbackTab = null;
                    }

                    settingsService.CurrentSettings.CustomPlaylists.Remove(playlist);
                    settingsService.SaveSettings();

                    UpdatePlaylistTabs();

                    if (_selectedTab == tab)
                    {
                        var allTab = _folderTabs.FirstOrDefault(t => t.IsAllTab) ?? _folderTabs.FirstOrDefault();
                        if (allTab != null) SelectFolderTab(allTab);
                        else RefreshPlaylistView();
                    }
                }
            }
        }

        private void UpdateFolderTabs()
        {
            if (playlistService.LoadedFolders.Count == 0)
            {
                _folderTabs.Clear();
                _selectedTab = null;
                UpdateTabScrollButtons();
                UpdatePlaylistTabs();
                return;
            }

            // 1. All tab
            var allTab = _folderTabs.FirstOrDefault(t => t.IsAllTab);
            if (allTab == null)
            {
                allTab = new FolderTabItem
                {
                    IsAllTab = true,
                    Name = "Tất cả",
                    FolderPath = null,
                    SongCount = playlistService.PlaylistPaths.Count,
                    IsSelected = (_selectedTab == null || _selectedTab.IsAllTab)
                };
                _folderTabs.Insert(0, allTab);
                if (_selectedTab == null) _selectedTab = allTab;
            }
            else
            {
                allTab.SongCount = playlistService.PlaylistPaths.Count;
            }

            // 2. Remove folder tabs no longer in LoadedFolders
            var existingFolderTabs = _folderTabs.Where(t => !t.IsAllTab).ToList();
            foreach (var tab in existingFolderTabs)
            {
                if (!playlistService.LoadedFolders.Any(f => string.Equals(f, tab.FolderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _folderTabs.Remove(tab);
                }
            }

            // 3. Add or update folder tabs
            foreach (var folder in playlistService.LoadedFolders)
            {
                int count = playlistService.PlaylistFolderOrigins.Count(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                string folderName = System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(folderName)) folderName = folder;

                var existingTab = _folderTabs.FirstOrDefault(t => !t.IsAllTab && string.Equals(t.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
                if (existingTab == null)
                {
                    _folderTabs.Add(new FolderTabItem
                    {
                        IsAllTab = false,
                        IsCustomPlaylist = false,
                        Name = folderName,
                        FolderPath = folder,
                        SongCount = count,
                        IsSelected = (_selectedTab != null && !_selectedTab.IsCustomPlaylist && string.Equals(_selectedTab.FolderPath, folder, StringComparison.OrdinalIgnoreCase))
                    });
                }
                else
                {
                    existingTab.Name = folderName;
                    existingTab.SongCount = count;
                }
            }

            UpdatePlaylistTabs();

            // 4. Ensure active selection
            if (_selectedTab == null)
            {
                _selectedTab = _folderTabs.FirstOrDefault(t => t.IsAllTab) ?? _folderTabs.FirstOrDefault();
            }

            UpdateFolderTabsIsPlaying();

            Dispatcher.InvokeAsync(() => UpdateTabScrollButtons(), DispatcherPriority.Loaded);
        }

        private void UpdatePlaylistTabs()
        {
            var customPlaylists = settingsService.CurrentSettings.CustomPlaylists ?? new List<CustomPlaylist>();

            // 1. Tab Cố Định: "Tổng Playlist" ở đầu hàng (index 0) - Chỉ hiển thị số lượng, không cho chọn
            var allPlaylistsTab = _playlistTabs.FirstOrDefault(t => t.IsCustomPlaylist && t.IsAllTab);
            if (allPlaylistsTab == null)
            {
                allPlaylistsTab = new FolderTabItem
                {
                    IsAllTab = true,
                    IsCustomPlaylist = true,
                    PlaylistId = "TOTAL_ALL_PLAYLISTS",
                    Name = "Tổng Playlist",
                    FolderPath = null,
                    SongCount = customPlaylists.Count,
                    IsSelected = false
                };
                _playlistTabs.Insert(0, allPlaylistsTab);
            }
            else
            {
                allPlaylistsTab.SongCount = customPlaylists.Count;
                allPlaylistsTab.IsSelected = false;
            }

            // Nếu tab đang chọn là Tổng Playlist (không hợp lệ), chuyển về tab Tất cả của thư mục
            if (_selectedTab != null && _selectedTab.IsCustomPlaylist && _selectedTab.IsAllTab)
            {
                _selectedTab = _folderTabs.FirstOrDefault(t => t.IsAllTab) ?? _folderTabs.FirstOrDefault();
            }

            // 2. Xóa các tab playlist không còn tồn tại (giữ nguyên tab Tổng Playlist)
            var existingPlaylistTabs = _playlistTabs.Where(t => !t.IsAllTab).ToList();
            foreach (var tab in existingPlaylistTabs)
            {
                if (!customPlaylists.Any(p => p.Id == tab.PlaylistId))
                {
                    _playlistTabs.Remove(tab);
                }
            }

            // 3. Thêm hoặc cập nhật các tab playlist cụ thể
            foreach (var pl in customPlaylists)
            {
                var existingTab = _playlistTabs.FirstOrDefault(t => !t.IsAllTab && t.PlaylistId == pl.Id);
                if (existingTab == null)
                {
                    _playlistTabs.Add(new FolderTabItem
                    {
                        IsAllTab = false,
                        IsCustomPlaylist = true,
                        PlaylistId = pl.Id,
                        Name = pl.Name,
                        FolderPath = null,
                        SongCount = pl.SongPaths.Count,
                        IsSelected = (_selectedTab != null && _selectedTab.IsCustomPlaylist && !_selectedTab.IsAllTab && _selectedTab.PlaylistId == pl.Id)
                    });
                }
                else
                {
                    existingTab.Name = pl.Name;
                    existingTab.SongCount = pl.SongPaths.Count;
                }
            }

            UpdatePlaylistTabScrollButtons();
            UpdateFolderTabsIsPlaying();
        }

        private void UpdateFolderTabsIsPlaying(bool? forcedPlaying = null)
        {
            bool isPlaying = forcedPlaying ?? audioPlayerService.IsPlaying;

            foreach (var tab in _folderTabs)
            {
                if (!isPlaying || _playbackTab == null || _playbackTab.IsCustomPlaylist)
                {
                    tab.IsPlaying = false;
                }
                else
                {
                    if (_playbackTab.IsAllTab)
                    {
                        tab.IsPlaying = tab.IsAllTab;
                    }
                    else
                    {
                        tab.IsPlaying = !tab.IsAllTab && string.Equals(tab.FolderPath, _playbackTab.FolderPath, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            foreach (var tab in _playlistTabs)
            {
                if (!isPlaying || _playbackTab == null || tab.IsAllTab)
                {
                    tab.IsPlaying = false;
                }
                else
                {
                    tab.IsPlaying = _playbackTab.IsCustomPlaylist && !tab.IsAllTab && string.Equals(tab.PlaylistId, _playbackTab.PlaylistId, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void LstPlaylist_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
            {
                e.Handled = true;
                return;
            }

            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListBoxItem))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }
            if (dep is ListBoxItem item && item.DataContext is PlaylistItem pItem)
            {
                _isRightClicking = true;
                try
                {
                    lstPlaylist.SelectedItem = pItem;
                }
                finally
                {
                    _isRightClicking = false;
                }
            }
        }

        private void LstPlaylist_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is ListBoxItem))
            {
                if (source is Button) return; // Ignore clicks on buttons
                source = VisualTreeHelper.GetParent(source);
            }

            var item = ItemsControl.ContainerFromElement(lstPlaylist, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null && item.DataContext is PlaylistItem playlistItem)
            {
                _playbackTab = _selectedTab ?? _folderTabs.FirstOrDefault(t => t.IsAllTab);

                if (playlistItem.OriginalIndex < 0 && !string.IsNullOrEmpty(playlistItem.FilePath) && System.IO.File.Exists(playlistItem.FilePath))
                {
                    int existingIdx = playlistService.GetSongIndex(playlistItem.FilePath);
                    if (existingIdx >= 0)
                    {
                        playlistItem.OriginalIndex = existingIdx;
                    }
                    else
                    {
                        playlistService.PlaylistPaths.Add(playlistItem.FilePath);
                        playlistService.DisplayPlaylist.Add(playlistItem.DisplayName);
                        playlistService.PlaylistFolderOrigins.Add(System.IO.Path.GetDirectoryName(playlistItem.FilePath) ?? "");
                        playlistItem.OriginalIndex = playlistService.PlaylistPaths.Count - 1;
                    }
                }

                if (playlistItem.OriginalIndex != currentSongIndex)
                {
                    if (currentSongIndex >= 0 && !_isNavigatingBack)
                    {
                        _playbackHistory.Push(currentSongIndex);
                    }
                    currentSongIndex = playlistItem.OriginalIndex;
                    if (settingsService.CurrentSettings.IsShuffle)
                    {
                        ResetShufflePool(currentSongIndex);
                    }
                    PlaySong();
                }
            }
        }

        private void LstPlaylist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRightClicking) return; // Không phát nhạc khi chuột phải!

            if (lstPlaylist.SelectedItem is PlaylistItem item)
            {
                lstPlaylist.ScrollIntoView(item);

                if (item.OriginalIndex < 0 && !string.IsNullOrEmpty(item.FilePath) && System.IO.File.Exists(item.FilePath))
                {
                    int existingIdx = playlistService.GetSongIndex(item.FilePath);
                    if (existingIdx >= 0)
                    {
                        item.OriginalIndex = existingIdx;
                    }
                    else
                    {
                        playlistService.PlaylistPaths.Add(item.FilePath);
                        playlistService.DisplayPlaylist.Add(item.DisplayName);
                        playlistService.PlaylistFolderOrigins.Add(System.IO.Path.GetDirectoryName(item.FilePath) ?? "");
                        item.OriginalIndex = playlistService.PlaylistPaths.Count - 1;
                    }
                }

                if (item.OriginalIndex != currentSongIndex && !_isKeyboardNavigating)
                {
                    _playbackTab = _selectedTab ?? _folderTabs.FirstOrDefault(t => t.IsAllTab);
                    if (currentSongIndex >= 0 && !_isNavigatingBack)
                    {
                        _playbackHistory.Push(currentSongIndex);
                    }
                    currentSongIndex = item.OriginalIndex;
                    if (settingsService.CurrentSettings.IsShuffle)
                    {
                        ResetShufflePool(currentSongIndex);
                    }
                    PlaySong();
                }
            }
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (!audioPlayerService.HasAudio && playlistService.PlaylistPaths.Count > 0)
            {
                if (currentSongIndex < 0)
                {
                    var indices = GetCurrentTabSongIndices();
                    currentSongIndex = indices.Count > 0 ? indices[0] : 0;
                }
                _playbackTab = _selectedTab ?? _folderTabs.FirstOrDefault(t => t.IsAllTab);
                PlaySong();
            }
            else if (audioPlayerService.HasAudio)
            {
                if (audioPlayerService.IsPlaying)
                {
                    audioPlayerService.Pause();
                    visualizerTimer.Stop();
                    iconPlayPause.Kind = PackIconKind.Play;
                    UpdateFolderTabsIsPlaying();
                }
                else
                {
                    if (_playbackTab == null)
                    {
                        _playbackTab = _selectedTab ?? _folderTabs.FirstOrDefault(t => t.IsAllTab);
                    }
                    audioPlayerService.Play();
                    visualizerTimer.Start();
                    iconPlayPause.Kind = PackIconKind.Pause;
                    UpdateFolderTabsIsPlaying();
                }
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            PlayNextSong(false); 
        }

        private List<int> GetCurrentTabSongIndices()
        {
            var targetTab = _playbackTab ?? _selectedTab;
            if (targetTab != null)
            {
                if (targetTab.IsCustomPlaylist)
                {
                    if (targetTab.IsAllTab)
                    {
                        var customPlaylists = settingsService.CurrentSettings.CustomPlaylists ?? new List<CustomPlaylist>();
                        var list = new List<int>();
                        foreach (var pl in customPlaylists)
                        {
                            foreach (var path in pl.SongPaths)
                            {
                                int idx = playlistService.PlaylistPaths.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                                if (idx >= 0 && !list.Contains(idx)) list.Add(idx);
                            }
                        }
                        if (list.Count > 0) return list;
                    }
                    else
                    {
                        var customPl = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == targetTab.PlaylistId);
                        if (customPl != null)
                        {
                            var list = new List<int>();
                            foreach (var path in customPl.SongPaths)
                            {
                                int idx = playlistService.PlaylistPaths.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                                if (idx >= 0) list.Add(idx);
                            }
                            if (list.Count > 0) return list;
                        }
                    }
                }
                else if (!targetTab.IsAllTab && !string.IsNullOrEmpty(targetTab.FolderPath))
                {
                    var list = new List<int>();
                    for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
                    {
                        if (i < playlistService.PlaylistFolderOrigins.Count && 
                            string.Equals(playlistService.PlaylistFolderOrigins[i], targetTab.FolderPath, StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add(i);
                        }
                    }
                    if (list.Count > 0) return list;
                }
            }
            
            return Enumerable.Range(0, playlistService.PlaylistPaths.Count).ToList();
        }

        private void ResetShufflePool(int currentSongIdx = -1)
        {
            _playedSongsInCycle.Clear();
            if (currentSongIdx >= 0)
            {
                _playedSongsInCycle.Add(currentSongIdx);
            }
        }

        private void PlayNextSong(bool isAutoSkip)
        {
            var validIndices = GetCurrentTabSongIndices();
            if (validIndices.Count == 0) return;

            // Nếu chỉ có 1 bài hát và bấm nút Next thủ công -> giữ nguyên cho bài hát tiếp tục phát, không phát lại từ đầu
            if (!isAutoSkip && validIndices.Count <= 1)
            {
                return;
            }

            var settings = settingsService.CurrentSettings;

            if (isAutoSkip && settings.RepeatMode == 2)
            {
                PlaySong();
                return;
            }

            int nextSongIndex = -1;

            if (settings.IsShuffle)
            {
                // Loại bỏ các index không còn hợp lệ khỏi pool nếu danh sách bài thay đổi
                _playedSongsInCycle.RemoveWhere(idx => !validIndices.Contains(idx));

                // Đảm bảo bài hiện tại đã được đánh dấu vào chu kỳ
                if (currentSongIndex >= 0 && validIndices.Contains(currentSongIndex))
                {
                    _playedSongsInCycle.Add(currentSongIndex);
                }

                // Lấy danh sách các bài chưa được phát trong chu kỳ hiện tại
                var unplayed = validIndices.Where(idx => !_playedSongsInCycle.Contains(idx)).ToList();

                // Nếu đã phát hết toàn bộ các bài trong tab/playlist -> Đã hoàn thành 1 chu kỳ!
                if (unplayed.Count == 0)
                {
                    // Nếu RepeatMode == 0 (không lặp lại danh sách) và tự động hết bài -> Dừng phát
                    if (isAutoSkip && settings.RepeatMode == 0)
                    {
                        StopSong();
                        _playedSongsInCycle.Clear();
                        return;
                    }

                    // Reset pool cho chu kỳ mới
                    _playedSongsInCycle.Clear();

                    // Ứng viên cho bài mở đầu chu kỳ mới: tránh trùng bài vừa kết thúc nếu danh sách > 1 bài
                    var candidates = validIndices.Count > 1
                        ? validIndices.Where(idx => idx != currentSongIndex).ToList()
                        : validIndices;

                    unplayed = candidates;
                }

                // THUẬT TOÁN SMART DITHERING (CHỐNG DÍNH CHÙM / PHÂN TÁN ĐỀU):
                // Nếu còn từ 3 bài chưa nghe trở lên trong bể, ưu tiên chọn các bài không nằm sát cạnh bài vừa phát (cách xa >= 2 vị trí trong tab)
                int currentPosInTab = validIndices.IndexOf(currentSongIndex);
                List<int> pickCandidates = unplayed;

                if (unplayed.Count >= 3 && currentPosInTab >= 0)
                {
                    var nonAdjacent = unplayed.Where(idx =>
                    {
                        int pos = validIndices.IndexOf(idx);
                        return Math.Abs(pos - currentPosInTab) >= 2;
                    }).ToList();

                    if (nonAdjacent.Count > 0)
                    {
                        pickCandidates = nonAdjacent;
                    }
                }

                // Bốc thăm ngẫu nhiên động 1 bài từ các bài ứng viên tối ưu
                int pickedIndex = pickCandidates[_shuffleRandom.Next(pickCandidates.Count)];
                _playedSongsInCycle.Add(pickedIndex);
                nextSongIndex = pickedIndex;
            }
            else
            {
                int currentPos = validIndices.IndexOf(currentSongIndex);
                int nextPos = (currentPos >= 0) ? (currentPos + 1) % validIndices.Count : 0;
                
                if (isAutoSkip && settings.RepeatMode == 0 && nextPos == 0 && currentPos == validIndices.Count - 1)
                {
                    StopSong();
                    return;
                }
                nextSongIndex = validIndices[nextPos];
            }

            if (currentSongIndex >= 0 && !_isNavigatingBack && currentSongIndex != nextSongIndex)
            {
                _playbackHistory.Push(currentSongIndex);
            }
            currentSongIndex = nextSongIndex;
            
            SyncPlaylistSelection();
            PlaySong();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            var validIndices = GetCurrentTabSongIndices();
            if (validIndices.Count <= 1) return; // Nếu chỉ có 1 bài hát thì giữ nguyên không làm gì

            // 1. Ưu tiên lấy lại bài hát trước đó từ Lịch sử phát (Playback History)
            while (_playbackHistory.Count > 0)
            {
                int prevHistoryIndex = _playbackHistory.Pop();
                if (prevHistoryIndex >= 0 && prevHistoryIndex < playlistService.PlaylistPaths.Count && prevHistoryIndex != currentSongIndex && validIndices.Contains(prevHistoryIndex))
                {
                    _isNavigatingBack = true;
                    try
                    {
                        currentSongIndex = prevHistoryIndex;
                        SyncPlaylistSelection();
                        PlaySong();
                        return;
                    }
                    finally
                    {
                        _isNavigatingBack = false;
                    }
                }
            }

            // 2. Nếu lịch sử trống (vừa mở app) -> Lùi về bài liền kề theo thứ tự hoặc bốc bài khác
            var settings = settingsService.CurrentSettings;

            if (settings.IsShuffle)
            {
                var others = validIndices.Where(idx => idx != currentSongIndex).ToList();
                if (others.Count > 0)
                {
                    currentSongIndex = others[_shuffleRandom.Next(others.Count)];
                }
                else
                {
                    currentSongIndex = validIndices[0];
                }
            }
            else
            {
                int currentPos = validIndices.IndexOf(currentSongIndex);
                int prevPos = (currentPos > 0) ? (currentPos - 1) : (validIndices.Count - 1);
                currentSongIndex = validIndices[prevPos];
            }

            SyncPlaylistSelection();
            PlaySong();
        }

        private void BtnRewind_Click(object sender, RoutedEventArgs e)
        {
            if (audioPlayerService.HasAudio)
            {
                var newTime = audioPlayerService.CurrentTime - TimeSpan.FromSeconds(10);
                if (newTime < TimeSpan.Zero) newTime = TimeSpan.Zero;
                audioPlayerService.CurrentTime = newTime;
            }
        }

        private void BtnFastForward_Click(object sender, RoutedEventArgs e)
        {
            if (audioPlayerService.HasAudio)
            {
                var newTime = audioPlayerService.CurrentTime + TimeSpan.FromSeconds(10);
                if (newTime > audioPlayerService.TotalTime) newTime = audioPlayerService.TotalTime;
                audioPlayerService.CurrentTime = newTime;
            }
        }

        private void BtnShuffle_Click(object sender, RoutedEventArgs e)
        {
            settingsService.CurrentSettings.IsShuffle = !settingsService.CurrentSettings.IsShuffle;
            settingsService.SaveSettings();
            iconShuffle.Foreground = settingsService.CurrentSettings.IsShuffle 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3b82f6")) 
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a1a1aa"));
                
            if (settingsService.CurrentSettings.IsShuffle)
            {
                ResetShufflePool(currentSongIndex);
            }
        }

        private void BtnRepeat_Click(object sender, RoutedEventArgs e)
        {
            settingsService.CurrentSettings.RepeatMode = (settingsService.CurrentSettings.RepeatMode + 1) % 3;
            settingsService.SaveSettings();
            UpdateRepeatIcon();
        }
        
        private void UpdateRepeatIcon()
        {
            var mode = settingsService.CurrentSettings.RepeatMode;
            if (mode == 0)
            {
                iconRepeat.Kind = PackIconKind.Repeat;
                iconRepeat.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a1a1aa"));
            }
            else if (mode == 1)
            {
                iconRepeat.Kind = PackIconKind.Repeat;
                iconRepeat.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3b82f6"));
            }
            else
            {
                iconRepeat.Kind = PackIconKind.RepeatOnce;
                iconRepeat.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3b82f6"));
            }
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (audioPlayerService != null)
            {
                audioPlayerService.Volume = (float)(e.NewValue / 100.0);
            }
            if (txtVolumePercent != null)
            {
                txtVolumePercent.Text = $"{(int)e.NewValue}%";
            }
        }

        private void SliderTimeline_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            isDraggingTimeline = true;
        }

        private void SliderTimeline_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            isDraggingTimeline = false;
            audioPlayerService.CurrentTime = TimeSpan.FromSeconds(sliderTimeline.Value);
        }

        private void SliderTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isDraggingTimeline)
            {
                txtCurrentTime.Text = TimeSpan.FromSeconds(sliderTimeline.Value).ToString(@"mm\:ss");
            }
            else
            {
                audioPlayerService.CurrentTime = TimeSpan.FromSeconds(sliderTimeline.Value);
            }
        }

        private void PlaySong()
        {
            if (currentSongIndex < 0 || currentSongIndex >= playlistService.PlaylistPaths.Count) return;

            // Kiểm tra tính hợp lệ của _playbackTab hiện tại
            if (_playbackTab != null)
            {
                if (_playbackTab.IsCustomPlaylist && !_playbackTab.IsAllTab)
                {
                    bool exists = settingsService.CurrentSettings.CustomPlaylists != null &&
                                  settingsService.CurrentSettings.CustomPlaylists.Any(p => string.Equals(p.Id, _playbackTab.PlaylistId, StringComparison.OrdinalIgnoreCase));
                    if (!exists) _playbackTab = null;
                }
                else if (!_playbackTab.IsCustomPlaylist && !_playbackTab.IsAllTab)
                {
                    bool exists = playlistService.LoadedFolders.Any(f => string.Equals(f, _playbackTab.FolderPath, StringComparison.OrdinalIgnoreCase));
                    if (!exists) _playbackTab = null;
                }
            }

            // Đồng bộ _playbackTab nếu chưa có (null)
            if (_playbackTab == null)
            {
                if (_selectedTab != null)
                {
                    _playbackTab = _selectedTab;
                }
                else if (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistFolderOrigins.Count)
                {
                    string playingFolder = playlistService.PlaylistFolderOrigins[currentSongIndex];
                    _playbackTab = _folderTabs.FirstOrDefault(t => !t.IsAllTab && string.Equals(t.FolderPath, playingFolder, StringComparison.OrdinalIgnoreCase))
                                   ?? _folderTabs.FirstOrDefault(t => t.IsAllTab);
                }
                else
                {
                    _playbackTab = _folderTabs.FirstOrDefault(t => t.IsAllTab);
                }
            }

            foreach (var item in _filteredPlaylist)
            {
                item.IsPlaying = (item.OriginalIndex == currentSongIndex);
            }

            // Giữ icon Loa luôn ổn định trên Tab của bài hát hiện tại, tránh co giãn giật tab khi chuyển bài
            UpdateFolderTabsIsPlaying(forcedPlaying: true);

            currentPlayRequestId++; 
            visualizerTimer?.Stop();
            iconPlayPause.Kind = PackIconKind.Play;
            foreach (var rect in visualizerBars)
            {
                if (rect != null) rect.Height = 0;
            }
            audioPlayerService.Stop();

            string currentFile = playlistService.PlaylistPaths[currentSongIndex];
            string currentFolder = (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistFolderOrigins.Count)
                ? playlistService.PlaylistFolderOrigins[currentSongIndex]
                : (_playbackTab?.FolderPath ?? "");
            string currentPlaylistId = (_playbackTab != null && _playbackTab.IsCustomPlaylist) ? _playbackTab.PlaylistId : "";

            var effectiveEq = settingsService.GetEffectiveEqualizer(currentFile, currentFolder, currentPlaylistId, out _);
            audioPlayerService.ApplyEqualizerSettings(effectiveEq);

            if (settingsService.CurrentSettings.IsShuffle)
            {
                var validIndices = GetCurrentTabSongIndices();
                if (validIndices.Contains(currentSongIndex))
                {
                    _playedSongsInCycle.Add(currentSongIndex);
                }
            }

            UpdateSongInfo(currentFile);
            
            // Cập nhật cửa sổ Equalizer theo real-time nếu đang mở
            if (_activeEqualizerWindow != null && _activeEqualizerWindow.IsLoaded)
            {
                string curSongTitle = (currentSongIndex >= 0 && currentSongIndex < playlistService.DisplayPlaylist.Count) 
                    ? playlistService.DisplayPlaylist[currentSongIndex] 
                    : System.IO.Path.GetFileNameWithoutExtension(currentFile);
                _activeEqualizerWindow.UpdatePlayingSong(currentFile, curSongTitle, currentFolder, currentPlaylistId);
            }
            
            float vol = (float)(sliderVolume.Value / 100.0);

            int requestId = ++currentPlayRequestId;

            System.Threading.Tasks.Task.Run(() => 
            {
                try
                {
                    audioPlayerService.Play(currentFile, vol);
                    
                    if (requestId != currentPlayRequestId)
                    {
                        audioPlayerService.Stop();
                        return;
                    }

                    Dispatcher.Invoke(() => {
                        iconPlayPause.Kind = PackIconKind.Pause;
                        
                        sliderTimeline.ValueChanged -= SliderTimeline_ValueChanged;
                        sliderTimeline.Value = 0;
                        sliderTimeline.Maximum = audioPlayerService.TotalTime.TotalSeconds;
                        sliderTimeline.ValueChanged += SliderTimeline_ValueChanged;
                        
                        txtTotalTime.Text = audioPlayerService.TotalTime.ToString(@"mm\:ss");
                        visualizerTimer.Start();
                        UpdateFolderTabsIsPlaying();
                    });
                }
                catch (Exception ex)
                {
                    if (requestId == currentPlayRequestId)
                    {
                        Dispatcher.Invoke(() => {
                            MessageBox.Show("Rất tiếc, lỗi phát nhạc: " + ex.Message, "Lỗi Phát Nhạc", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                }
            });
        }

        private void UpdateSongInfo(string file)
        {
            txtTitle.Text = System.IO.Path.GetFileNameWithoutExtension(file);
            txtArtist.Text = "Unknown Artist";
            imgAlbumArt.Source = null;
            if (txtFormat != null) txtFormat.Text = "---";
            if (txtBitrate != null) txtBitrate.Text = "--- kbps";
            if (txtSampleRate != null) txtSampleRate.Text = "--- kHz";
            if (txtFileSize != null) txtFileSize.Text = "--- MB";

            try
            {
                using (var tfile = TagLib.File.Create(file))
                {
                    if (!string.IsNullOrEmpty(tfile.Tag.Title))
                        txtTitle.Text = tfile.Tag.Title;
                    
                    if (tfile.Tag.Performers.Length > 0)
                        txtArtist.Text = string.Join(", ", tfile.Tag.Performers);

                    if (tfile.Properties != null)
                    {
                        string ext = System.IO.Path.GetExtension(file).Replace(".", "").ToUpper();
                        if (txtFormat != null) txtFormat.Text = ext;
                        
                        int bitrate = tfile.Properties.AudioBitrate;
                        if (txtBitrate != null) txtBitrate.Text = bitrate > 0 ? $"{bitrate} kbps" : "--- kbps";
                        
                        int sampleRate = tfile.Properties.AudioSampleRate;
                        if (txtSampleRate != null) txtSampleRate.Text = sampleRate > 0 ? $"{sampleRate / 1000.0:0.#} kHz" : "--- kHz";
                    }

                    var fileInfo = new System.IO.FileInfo(file);
                    double mb = fileInfo.Length / 1048576.0;
                    if (txtFileSize != null) txtFileSize.Text = $"{mb:0.##} MB";

                    if (tfile.Tag.Pictures.Length > 0)
                    {
                        var pic = tfile.Tag.Pictures[0];
                        using (MemoryStream ms = new MemoryStream(pic.Data.Data))
                        {
                            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            bitmap.Freeze(); 
                            imgAlbumArt.Source = bitmap;
                        }
                    }
                }
            }
            catch { }
        }

        private void StopSong()
        {
            currentPlayRequestId++; 
            visualizerTimer?.Stop();
            iconPlayPause.Kind = PackIconKind.Play;
            
            foreach (var rect in visualizerBars)
            {
                if (rect != null) rect.Height = 0;
            }
            
            audioPlayerService.Stop();
            _playbackTab = null;
            UpdateFolderTabsIsPlaying();
        }

        private void AudioPlayerService_DefaultDeviceChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    if (audioPlayerService.SelectedDeviceNumber == -1)
                    {
                        bool wasPlaying = audioPlayerService.IsPlaying;
                        audioPlayerService.ChangeDevice(-1);
                        if (wasPlaying)
                        {
                            audioPlayerService.Play();
                        }
                    }
                }
                catch { }
            }));
        }

        private void AudioPlayerService_PlaybackStopped(object sender, NAudio.Wave.StoppedEventArgs e)
        {
            if (e.Exception == null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    double totalSec = audioPlayerService.TotalTime.TotalSeconds;
                    double currentSec = audioPlayerService.CurrentTime.TotalSeconds;
                    double remainingSec = totalSec - currentSec;
                    double progressPercent = totalSec > 0 ? (currentSec / totalSec) : 0;

                    // Chỉ tự động chuyển bài khi bài hát đã phát gần hết (còn <= 3.5s hoặc đã đạt >= 98%)
                    if (remainingSec <= 3.5 || progressPercent >= 0.98)
                    {
                        PlayNextSong(true);
                    }
                    else if (totalSec > 0 && currentSec > 0)
                    {
                        // File bị ngắt giữa chừng do lỗi dữ liệu âm thanh
                        string songTitle = txtTitle.Text;
                        string errorTime = audioPlayerService.CurrentTime.ToString(@"mm\:ss");
                        
                        MessageBox.Show(
                            $"Bài hát \"{songTitle}\" bị lỗi dữ liệu âm thanh tại vị trí {errorTime}.\nỨng dụng sẽ tự động chuyển sang bài tiếp theo.", 
                            "Cảnh Báo File Nhạc Lỗi", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Warning);

                        PlayNextSong(true);
                    }
                }));
            }
            else
            {
                // Fallback to system default if an error occurs (like Bluetooth speaker disconnects)
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(500);
                        if (cboAudioDevices.SelectedIndex != 0)
                        {
                            cboAudioDevices.SelectedIndex = 0; // Triggers ChangeDevice(-1) via SelectionChanged
                        }
                        else
                        {
                            audioPlayerService.ChangeDevice(-1); // Re-initialize the player on default device
                        }
                        
                        audioPlayerService.Play();
                    }
                    catch { }
                }));
            }
        }

        private void AudioPlayerService_FftCalculated(object sender, FftEventArgs e)
        {
            if (currentFft == null || currentFft.Length != e.Result.Length)
            {
                currentFft = new float[e.Result.Length];
            }

            for (int i = 0; i < e.Result.Length; i++)
            {
                currentFft[i] = (float)Math.Sqrt(e.Result[i].X * e.Result[i].X + e.Result[i].Y * e.Result[i].Y);
            }
        }

        private void VisualizerTimer_Tick(object sender, EventArgs e)
        {
            if (!audioPlayerService.IsPlaying || currentFft == null) return;

            double maxHeight = canvasVisualizer.Height;
            double multiplier = maxHeight * 5; 

            for (int i = 0; i < BAR_COUNT; i++)
            {
                double power = i * (8.5 / BAR_COUNT); 
                int startFftIndex = (int)Math.Pow(2, power);
                int endFftIndex = (int)Math.Pow(2, power + (8.5 / BAR_COUNT));
                
                if (startFftIndex >= currentFft.Length / 2) startFftIndex = currentFft.Length / 2 - 1;
                if (endFftIndex >= currentFft.Length / 2) endFftIndex = currentFft.Length / 2 - 1;
                if (endFftIndex < startFftIndex) endFftIndex = startFftIndex;

                double maxVal = 0;
                for (int j = startFftIndex; j <= endFftIndex; j++)
                {
                    if (currentFft[j] > maxVal) maxVal = currentFft[j];
                }

                double eqCurve = Math.Sin(Math.PI * i / (BAR_COUNT - 1));
                double freqBoost = 1.0 + (i * 0.1); 
                double targetHeight = maxVal * multiplier * freqBoost * eqCurve;
                
                if (targetHeight > maxHeight) targetHeight = maxHeight;
                if (targetHeight < 2) targetHeight = 2; 

                double currentHeight = visualizerBars[i].Height;
                double newHeight = targetHeight > currentHeight 
                    ? currentHeight + (targetHeight - currentHeight) * 0.6 
                    : currentHeight + (targetHeight - currentHeight) * 0.2;

                visualizerBars[i].Height = newHeight;
                Canvas.SetTop(visualizerBars[i], (maxHeight - newHeight) / 2);
            }

            if (!isDraggingTimeline && audioPlayerService.HasAudio)
            {
                try
                {
                    var currentTime = audioPlayerService.CurrentTime;
                    
                    sliderTimeline.ValueChanged -= SliderTimeline_ValueChanged;
                    sliderTimeline.Value = currentTime.TotalSeconds;
                    sliderTimeline.ValueChanged += SliderTimeline_ValueChanged;
                    
                    txtCurrentTime.Text = currentTime.ToString(@"mm\:ss");
                }
                catch { }
            }
        }

        // --- HẸN GIỜ TẮT NHẠC ---
        private DispatcherTimer sleepTimer;
        private TimeSpan sleepTimerRemaining;

        private void BtnSleepTimerToggle_Click(object sender, RoutedEventArgs e)
        {
            popupSleepTimer.IsOpen = !popupSleepTimer.IsOpen;
        }

        private void StartSleepTimer(int minutes)
        {
            if (sleepTimer == null)
            {
                sleepTimer = new DispatcherTimer();
                sleepTimer.Interval = TimeSpan.FromSeconds(1);
                sleepTimer.Tick += SleepTimer_Tick;
            }

            sleepTimerRemaining = TimeSpan.FromMinutes(minutes);
            UpdateTimerUI();
            sleepTimer.Start();
            
            pnlTimerSetup.Visibility = Visibility.Collapsed;
            pnlTimerActive.Visibility = Visibility.Visible;
            
            popupSleepTimer.IsOpen = false; // Tự động đóng popup sau khi bấm
            iconSleepTimer.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3b82f6"));
        }

        private void StopSleepTimer()
        {
            if (sleepTimer != null)
            {
                sleepTimer.Stop();
            }
            
            pnlTimerSetup.Visibility = Visibility.Visible;
            pnlTimerActive.Visibility = Visibility.Collapsed;
            
            popupSleepTimer.IsOpen = false; // Tự động đóng popup
            iconSleepTimer.Foreground = (Brush)FindResource("TextSecondary");
        }

        private void SleepTimer_Tick(object sender, EventArgs e)
        {
            if (sleepTimerRemaining.TotalSeconds > 0)
            {
                sleepTimerRemaining = sleepTimerRemaining.Subtract(TimeSpan.FromSeconds(1));
                UpdateTimerUI();
            }
            
            if (sleepTimerRemaining.TotalSeconds <= 0)
            {
                StopSleepTimer();
                audioPlayerService.Pause();
                iconPlayPause.Kind = PackIconKind.Play;
            }
        }

        private void UpdateTimerUI()
        {
            txtTimerCountdown.Text = sleepTimerRemaining.ToString(@"hh\:mm\:ss");
        }

        private void BtnTimer15_Click(object sender, RoutedEventArgs e) => StartSleepTimer(15);
        private void BtnTimer30_Click(object sender, RoutedEventArgs e) => StartSleepTimer(30);
        private void BtnTimer60_Click(object sender, RoutedEventArgs e) => StartSleepTimer(60);
        
        private void BtnSetCustomTimer_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtCustomTimer.Text, out int minutes) && minutes > 0)
            {
                StartSleepTimer(minutes);
                txtCustomTimer.Text = "";
            }
            else
            {
                MessageBox.Show("Vui lòng nhập số phút hợp lệ.", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancelTimer_Click(object sender, RoutedEventArgs e) => StopSleepTimer();

        private void TxtCustomTimer_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        // --- TỐC ĐỘ PHÁT NHẠC (PLAYBACK SPEED) ---
        private void BtnPlaybackSpeed_Click(object sender, RoutedEventArgs e)
        {
            popupPlaybackSpeed.IsOpen = !popupPlaybackSpeed.IsOpen;
        }

        private void BtnResetSpeed_Click(object sender, RoutedEventArgs e)
        {
            SetPlaybackSpeed(1.0f);
        }

        private void BtnSpeedPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                if (float.TryParse(btn.Tag.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
                {
                    SetPlaybackSpeed(speed);
                    popupPlaybackSpeed.IsOpen = false;
                }
            }
        }

        private void SliderCustomSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtCustomSpeedVal == null) return;
            float speed = (float)Math.Round(e.NewValue, 2);
            txtCustomSpeedVal.Text = $"{speed:0.00}x";
            SetPlaybackSpeed(speed, fromSlider: true);
        }

        private void SetPlaybackSpeed(float speed, bool fromSlider = false)
        {
            speed = Math.Clamp(speed, 0.25f, 2.5f);
            if (audioPlayerService != null)
            {
                audioPlayerService.PlaybackSpeed = speed;
            }

            if (txtPlaybackSpeed != null)
            {
                txtPlaybackSpeed.Text = $"{speed:0.##}x";
                if (Math.Abs(speed - 1.0f) > 0.01f)
                {
                    txtPlaybackSpeed.Foreground = (Brush)FindResource("AccentPrimary");
                }
                else
                {
                    txtPlaybackSpeed.Foreground = (Brush)FindResource("TextSecondary");
                }
            }

            if (txtCustomSpeedVal != null)
            {
                txtCustomSpeedVal.Text = $"{speed:0.00}x";
            }

            if (!fromSlider && sliderCustomSpeed != null)
            {
                sliderCustomSpeed.ValueChanged -= SliderCustomSpeed_ValueChanged;
                sliderCustomSpeed.Value = speed;
                sliderCustomSpeed.ValueChanged += SliderCustomSpeed_ValueChanged;
            }
        }

        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC).Replace("đ", "d").Replace("Đ", "D");
        }

        private void RefreshPlaylistView()
        {
            string query = txtSearch?.Text?.Trim() ?? "";
            query = RemoveDiacritics(query).ToLowerInvariant();

            _filteredPlaylist.Clear();

            if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
            {
                List<string> songPathsToShow = new List<string>();
                if (_selectedTab.IsAllTab)
                {
                    var customPlaylists = settingsService.CurrentSettings.CustomPlaylists ?? new List<CustomPlaylist>();
                    foreach (var pl in customPlaylists)
                    {
                        foreach (var path in pl.SongPaths)
                        {
                            if (!songPathsToShow.Contains(path, StringComparer.OrdinalIgnoreCase))
                            {
                                songPathsToShow.Add(path);
                            }
                        }
                    }
                }
                else
                {
                    var customPl = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == _selectedTab.PlaylistId);
                    if (customPl != null)
                    {
                        songPathsToShow = customPl.SongPaths;
                    }
                }

                for (int pIdx = 0; pIdx < songPathsToShow.Count; pIdx++)
                {
                    string filePath = songPathsToShow[pIdx];
                    int origIdx = playlistService.PlaylistPaths.FindIndex(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));

                    string displayName;
                    string songFolder = null;
                    if (origIdx >= 0)
                    {
                        displayName = playlistService.DisplayPlaylist[origIdx];
                        songFolder = origIdx < playlistService.PlaylistFolderOrigins.Count ? playlistService.PlaylistFolderOrigins[origIdx] : null;
                    }
                    else
                    {
                        displayName = System.IO.Path.GetFileName(filePath);
                        try
                        {
                            if (System.IO.File.Exists(filePath))
                            {
                                using (var tfile = TagLib.File.Create(filePath))
                                {
                                    string title = tfile.Tag.Title;
                                    string artist = tfile.Tag.FirstPerformer;
                                    if (!string.IsNullOrWhiteSpace(title))
                                    {
                                        displayName = title;
                                        if (!string.IsNullOrWhiteSpace(artist)) displayName += " - " + artist;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    string searchStr = RemoveDiacritics(displayName).ToLowerInvariant();
                    if (string.IsNullOrEmpty(query) || searchStr.Contains(query))
                    {
                        _filteredPlaylist.Add(new PlaylistItem
                        {
                            OriginalIndex = origIdx,
                            DisplayName = displayName,
                            SearchString = searchStr,
                            FilePath = filePath,
                            FolderPath = songFolder,
                            IsPlaying = (origIdx >= 0 && origIdx == currentSongIndex)
                        });
                    }
                }
            }
            else
            {
                string selectedFolderFilter = (_selectedTab != null && !_selectedTab.IsAllTab) ? _selectedTab.FolderPath : null;

                for (int i = 0; i < playlistService.DisplayPlaylist.Count; i++)
                {
                    string songFolder = i < playlistService.PlaylistFolderOrigins.Count ? playlistService.PlaylistFolderOrigins[i] : null;

                    if (selectedFolderFilter != null && !string.Equals(songFolder, selectedFolderFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string displayName = playlistService.DisplayPlaylist[i];
                    string searchStr = RemoveDiacritics(displayName).ToLowerInvariant();

                    if (string.IsNullOrEmpty(query) || searchStr.Contains(query))
                    {
                        _filteredPlaylist.Add(new PlaylistItem 
                        { 
                            OriginalIndex = i, 
                            DisplayName = displayName,
                            SearchString = searchStr,
                            FilePath = i < playlistService.PlaylistPaths.Count ? playlistService.PlaylistPaths[i] : "",
                            FolderPath = songFolder,
                            IsPlaying = (i == currentSongIndex)
                        });
                    }
                }
            }

            bool hasFolders = playlistService.LoadedFolders.Count > 0 || (settingsService.CurrentSettings.CustomPlaylists != null && settingsService.CurrentSettings.CustomPlaylists.Count > 0);
            bool hasSongs = _filteredPlaylist.Count > 0;

            if (gridFolderTabs != null)
            {
                gridFolderTabs.Visibility = hasFolders ? Visibility.Visible : Visibility.Collapsed;
            }
            if (borderEmptyState != null)
            {
                borderEmptyState.Visibility = !hasSongs ? Visibility.Visible : Visibility.Collapsed;

                if (!string.IsNullOrEmpty(query))
                {
                    if (txtEmptyTitle != null) txtEmptyTitle.Text = "Không Tìm Thấy Bài Hát";
                    if (txtEmptyDesc != null) txtEmptyDesc.Text = $"Không có bài hát nào khớp với từ khóa \"{txtSearch?.Text}\" trong danh sách này.";
                    if (txtEmptyAction != null) txtEmptyAction.Text = "Xóa Tìm Kiếm";
                    if (iconEmptyAction != null) iconEmptyAction.Kind = PackIconKind.Close;
                }
                else if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
                {
                    if (txtEmptyTitle != null) txtEmptyTitle.Text = "Playlist Chưa Có Bài Hát";
                    if (txtEmptyDesc != null) txtEmptyDesc.Text = "Bấm nút bên dưới để chọn các bài hát muốn thêm vào Playlist này.";
                    if (txtEmptyAction != null) txtEmptyAction.Text = "Thêm Bài Hát";
                    if (iconEmptyAction != null) iconEmptyAction.Kind = PackIconKind.PlaylistPlus;
                }
                else if (!hasFolders)
                {
                    if (txtEmptyTitle != null) txtEmptyTitle.Text = "Chưa Có Thư Mục Nhạc Nào";
                    if (txtEmptyDesc != null) txtEmptyDesc.Text = "Mở các thư mục chứa bài hát yêu thích của bạn để bắt đầu thưởng thức âm nhạc";
                    if (txtEmptyAction != null) txtEmptyAction.Text = "Mở Thư Mục Nhạc";
                    if (iconEmptyAction != null) iconEmptyAction.Kind = PackIconKind.FolderOpenOutline;
                }
                else
                {
                    if (txtEmptyTitle != null) txtEmptyTitle.Text = "Chưa Có Bài Hát Nào";
                    if (txtEmptyDesc != null) txtEmptyDesc.Text = "Thư mục này hiện không có bài hát nào. Bạn có thể tải nhạc từ YouTube/TikTok/SoundCloud hoặc mở thư mục khác.";
                    if (txtEmptyAction != null) txtEmptyAction.Text = "Mở Thêm Thư Mục";
                    if (iconEmptyAction != null) iconEmptyAction.Kind = PackIconKind.FolderPlusOutline;
                }
            }
            if (lstPlaylist != null)
            {
                lstPlaylist.Visibility = hasSongs ? Visibility.Visible : Visibility.Collapsed;
            }

            SyncPlaylistSelection();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (btnSearchClear != null)
            {
                btnSearchClear.Visibility = !string.IsNullOrEmpty(txtSearch?.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshPlaylistView();
        }

        private void BtnSearchClear_Click(object sender, RoutedEventArgs e)
        {
            if (txtSearch != null)
            {
                txtSearch.Text = string.Empty;
                txtSearch.Focus();
            }
        }

        private void BtnEmptyStateAction_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(txtSearch?.Text))
            {
                txtSearch.Text = string.Empty;
                if (txtSearch != null) txtSearch.Focus();
            }
            else if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
            {
                var pl = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == _selectedTab.PlaylistId);
                if (pl != null)
                {
                    var selectableSongs = new List<SelectableSongItem>();
                    for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
                    {
                        string path = playlistService.PlaylistPaths[i];
                        string displayName = i < playlistService.DisplayPlaylist.Count ? playlistService.DisplayPlaylist[i] : System.IO.Path.GetFileName(path);
                        string folderPath = i < playlistService.PlaylistFolderOrigins.Count ? playlistService.PlaylistFolderOrigins[i] : "";
                        string folderName = !string.IsNullOrEmpty(folderPath) ? System.IO.Path.GetFileName(folderPath.TrimEnd('\\', '/')) : "";
                        if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                        bool isAlreadyInPlaylist = pl.SongPaths != null && pl.SongPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

                        selectableSongs.Add(new SelectableSongItem
                        {
                            FilePath = path,
                            DisplayName = displayName,
                            FolderName = folderName,
                            IsChecked = isAlreadyInPlaylist
                        });
                    }

                    var dialog = new CreatePlaylistDialog(selectableSongs, defaultName: pl.Name, isEdit: true, existingPlaylistNames: settingsService.CurrentSettings.CustomPlaylists?.Select(p => p.Name).ToList())
                    {
                        Owner = this,
                        OnConfirmed = (d) =>
                        {
                            if (!string.IsNullOrWhiteSpace(d.PlaylistName))
                            {
                                pl.Name = d.PlaylistName;
                            }
                            pl.SongPaths = d.SelectedSongPaths;
                            settingsService.SaveSettings();
                            UpdatePlaylistTabs();
                            RefreshPlaylistView();
                        }
                    };
                    dialog.Show();
                }
            }
            else
            {
                BtnFolder_Click(sender, e);
            }
        }

        private void SyncPlaylistSelection()
        {
            if (currentSongIndex >= 0)
            {
                var currentItem = _filteredPlaylist.FirstOrDefault(p => p.OriginalIndex == currentSongIndex);
                if (currentItem != null)
                {
                    lstPlaylist.SelectedItem = currentItem;
                }
                else
                {
                    lstPlaylist.SelectedItem = null;
                }
            }
            else
            {
                lstPlaylist.SelectedItem = null;
            }
        }

        // ==================== TÍNH NĂNG CUSTOM PLAYLIST ====================

        private void BtnCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var selectableSongs = new List<SelectableSongItem>();
            for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
            {
                string path = playlistService.PlaylistPaths[i];
                string displayName = i < playlistService.DisplayPlaylist.Count ? playlistService.DisplayPlaylist[i] : System.IO.Path.GetFileName(path);
                string folderPath = i < playlistService.PlaylistFolderOrigins.Count ? playlistService.PlaylistFolderOrigins[i] : "";
                string folderName = !string.IsNullOrEmpty(folderPath) ? System.IO.Path.GetFileName(folderPath.TrimEnd('\\', '/')) : "";
                if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                selectableSongs.Add(new SelectableSongItem
                {
                    FilePath = path,
                    DisplayName = displayName,
                    FolderName = folderName,
                    IsChecked = false
                });
            }

            var dialog = new CreatePlaylistDialog(selectableSongs, existingPlaylistNames: settingsService.CurrentSettings.CustomPlaylists?.Select(p => p.Name).ToList())
            {
                Owner = this,
                OnConfirmed = (d) =>
                {
                    if (!string.IsNullOrWhiteSpace(d.PlaylistName))
                    {
                        var newPl = new CustomPlaylist
                        {
                            Name = d.PlaylistName,
                            SongPaths = d.SelectedSongPaths
                        };
                        if (settingsService.CurrentSettings.CustomPlaylists == null)
                        {
                            settingsService.CurrentSettings.CustomPlaylists = new List<CustomPlaylist>();
                        }
                        settingsService.CurrentSettings.CustomPlaylists.Add(newPl);
                        settingsService.SaveSettings();

                        UpdatePlaylistTabs();

                        var newTab = _playlistTabs.FirstOrDefault(t => !t.IsAllTab && t.PlaylistId == newPl.Id);
                        if (newTab != null)
                        {
                            SelectPlaylistTab(newTab);
                        }
                    }
                }
            };
            dialog.Show();
        }

        private void PlaylistTab_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            FolderTabItem tab = null;
            if (sender is ContextMenu menu)
            {
                tab = (menu.PlacementTarget as FrameworkElement)?.DataContext as FolderTabItem ?? menu.DataContext as FolderTabItem;
            }
            else if (sender is FrameworkElement elem)
            {
                tab = elem.DataContext as FolderTabItem;
            }

            if (tab == null || tab.IsAllTab)
            {
                e.Handled = true;
            }
        }

        private void FolderTab_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            FolderTabItem tab = null;
            if (sender is ContextMenu menu)
            {
                tab = (menu.PlacementTarget as FrameworkElement)?.DataContext as FolderTabItem ?? menu.DataContext as FolderTabItem;
            }
            else if (sender is FrameworkElement elem)
            {
                tab = elem.DataContext as FolderTabItem;
            }

            if (tab == null || tab.IsAllTab)
            {
                e.Handled = true;
            }
        }

        private void MenuTabAddFolderToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is FolderTabItem tab && !tab.IsAllTab)
            {
                OpenAddToPlaylistDialogForFolder(tab);
            }
        }

        private void OpenAddToPlaylistDialogForFolder(FolderTabItem tab)
        {
            if (tab == null || string.IsNullOrEmpty(tab.FolderPath)) return;

            var folderSongPaths = new List<string>();
            for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
            {
                if (i < playlistService.PlaylistFolderOrigins.Count &&
                    string.Equals(playlistService.PlaylistFolderOrigins[i], tab.FolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    string filePath = playlistService.PlaylistPaths[i];
                    folderSongPaths.Add(filePath);
                }
            }

            if (folderSongPaths.Count == 0)
            {
                MessageBox.Show("Thư mục này không có bài hát nào để thêm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AddToPlaylistDialog(folderSongPaths, $"{tab.Name} ({folderSongPaths.Count} bài)", true, settingsService)
            {
                Owner = this,
                OnPlaylistsSaved = () =>
                {
                    UpdatePlaylistTabs();
                    if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
                    {
                        RefreshPlaylistView();
                    }
                }
            };
            dialog.Show();
        }

        private void AddFolderToPlaylist(CustomPlaylist targetPl, string folderPath)
        {
            if (targetPl == null || string.IsNullOrEmpty(folderPath)) return;

            int addedCount = 0;
            for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
            {
                if (i < playlistService.PlaylistFolderOrigins.Count &&
                    string.Equals(playlistService.PlaylistFolderOrigins[i], folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    string filePath = playlistService.PlaylistPaths[i];
                    if (!targetPl.SongPaths.Contains(filePath, StringComparer.OrdinalIgnoreCase))
                    {
                        targetPl.SongPaths.Add(filePath);
                        addedCount++;
                    }
                }
            }

            if (addedCount > 0)
            {
                settingsService.SaveSettings();
                UpdatePlaylistTabs();
                if (_selectedTab != null && _selectedTab.IsCustomPlaylist && _selectedTab.PlaylistId == targetPl.Id)
                {
                    RefreshPlaylistView();
                }
                MessageBox.Show($"Đã thêm {addedCount} bài hát vào playlist \"{targetPl.Name}\"!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Tất cả bài hát trong thư mục này đã có sẵn trong playlist \"{targetPl.Name}\".", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuTabAddSongs_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is FolderTabItem tab && tab.IsCustomPlaylist)
            {
                var pl = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == tab.PlaylistId);
                if (pl != null)
                {
                    var selectableSongs = new List<SelectableSongItem>();
                    for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
                    {
                        string path = playlistService.PlaylistPaths[i];
                        string displayName = i < playlistService.DisplayPlaylist.Count ? playlistService.DisplayPlaylist[i] : System.IO.Path.GetFileName(path);
                        string folderPath = i < playlistService.PlaylistFolderOrigins.Count ? playlistService.PlaylistFolderOrigins[i] : "";
                        string folderName = !string.IsNullOrEmpty(folderPath) ? System.IO.Path.GetFileName(folderPath.TrimEnd('\\', '/')) : "";
                        if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                        bool isAlreadyInPlaylist = pl.SongPaths != null && pl.SongPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

                        selectableSongs.Add(new SelectableSongItem
                        {
                            FilePath = path,
                            DisplayName = displayName,
                            FolderName = folderName,
                            IsChecked = isAlreadyInPlaylist
                        });
                    }

                    var dialog = new CreatePlaylistDialog(selectableSongs, defaultName: pl.Name, isEdit: true, existingPlaylistNames: settingsService.CurrentSettings.CustomPlaylists?.Select(p => p.Name).ToList())
                    {
                        Owner = this,
                        OnConfirmed = (d) =>
                        {
                            if (!string.IsNullOrWhiteSpace(d.PlaylistName))
                            {
                                pl.Name = d.PlaylistName;
                            }
                            pl.SongPaths = d.SelectedSongPaths;
                            settingsService.SaveSettings();
                            UpdatePlaylistTabs();
                            if (_selectedTab == tab)
                            {
                                RefreshPlaylistView();
                            }
                        }
                    };
                    dialog.Show();
                }
            }
        }

        private void MenuTabRenamePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is FolderTabItem tab && tab.IsCustomPlaylist)
            {
                var pl = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == tab.PlaylistId);
                if (pl != null)
                {
                    var dialog = new CreatePlaylistDialog(defaultName: pl.Name, isRename: true, existingPlaylistNames: settingsService.CurrentSettings.CustomPlaylists?.Select(p => p.Name).ToList())
                    {
                        Owner = this,
                        OnConfirmed = (d) =>
                        {
                            if (!string.IsNullOrWhiteSpace(d.PlaylistName))
                            {
                                pl.Name = d.PlaylistName;
                                settingsService.SaveSettings();
                                UpdatePlaylistTabs();
                                if (_selectedTab == tab)
                                {
                                RefreshPlaylistView();
                                }
                            }
                        }
                    };
                    dialog.Show();
                }
            }
        }

        private void MenuTabDeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is FolderTabItem tab && tab.IsCustomPlaylist)
            {
                DeletePlaylist(tab);
            }
        }

        private void MenuTabCloseFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is FolderTabItem tab && !tab.IsAllTab)
            {
                CloseFolderTab(tab);
            }
        }

        private void MenuTabRenameFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is FolderTabItem tab && !tab.IsAllTab && !string.IsNullOrEmpty(tab.FolderPath))
            {
                string oldPath = tab.FolderPath;
                if (!Directory.Exists(oldPath))
                {
                    MessageBox.Show("Thư mục này không còn tồn tại trên máy tính.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string currentFolderName = System.IO.Path.GetFileName(oldPath);
                string parentDir = System.IO.Path.GetDirectoryName(oldPath);
                if (string.IsNullOrEmpty(parentDir))
                {
                    MessageBox.Show("Không thể đổi tên thư mục gốc của ổ đĩa.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new RenameFolderDialog(currentFolderName, oldPath)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewFolderName))
                {
                    string newFolderName = dialog.NewFolderName.Trim();
                    if (string.Equals(currentFolderName, newFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        return; // Không có gì thay đổi
                    }

                    string newPath = System.IO.Path.Combine(parentDir, newFolderName);
                    if (Directory.Exists(newPath))
                    {
                        MessageBox.Show("Thư mục với tên này đã tồn tại tại cùng vị trí.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 1. Kiểm tra bài hát đang phát có thuộc thư mục này không
                    bool wasPlayingInThisFolder = false;
                    TimeSpan savedTime = TimeSpan.Zero;
                    if (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistPaths.Count)
                    {
                        string curSongPath = playlistService.PlaylistPaths[currentSongIndex];
                        if (curSongPath.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                        {
                            wasPlayingInThisFolder = audioPlayerService.IsPlaying;
                            savedTime = audioPlayerService.CurrentTime;
                            audioPlayerService.Stop();
                        }
                    }

                    try
                    {
                        // 2. Đổi tên thư mục vật lý trên ổ đĩa
                        Directory.Move(oldPath, newPath);

                        // 3. Cập nhật Tab
                        tab.FolderPath = newPath;
                        tab.Name = newFolderName;

                        // 4. Cập nhật PlaylistPaths
                        for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
                        {
                            if (playlistService.PlaylistPaths[i].StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                            {
                                string rel = playlistService.PlaylistPaths[i].Substring(oldPath.Length);
                                playlistService.PlaylistPaths[i] = newPath + rel;
                            }
                        }

                        // 5. Cập nhật PlaylistFolderOrigins
                        for (int i = 0; i < playlistService.PlaylistFolderOrigins.Count; i++)
                        {
                            if (playlistService.PlaylistFolderOrigins[i].StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                            {
                                string rel = playlistService.PlaylistFolderOrigins[i].Substring(oldPath.Length);
                                playlistService.PlaylistFolderOrigins[i] = newPath + rel;
                            }
                        }

                        // 6. Cập nhật LoadedFolders
                        for (int i = 0; i < playlistService.LoadedFolders.Count; i++)
                        {
                            if (string.Equals(playlistService.LoadedFolders[i], oldPath, StringComparison.OrdinalIgnoreCase))
                            {
                                playlistService.LoadedFolders[i] = newPath;
                            }
                        }

                        // 7. Cập nhật LastFolders trong Settings
                        for (int i = 0; i < settingsService.CurrentSettings.LastFolders.Count; i++)
                        {
                            if (string.Equals(settingsService.CurrentSettings.LastFolders[i], oldPath, StringComparison.OrdinalIgnoreCase))
                            {
                                settingsService.CurrentSettings.LastFolders[i] = newPath;
                            }
                        }

                        // 8. Cập nhật trong CustomPlaylists nếu có bài hát thuộc folder này
                        if (settingsService.CurrentSettings.CustomPlaylists != null)
                        {
                            foreach (var pl in settingsService.CurrentSettings.CustomPlaylists)
                            {
                                if (pl.SongPaths != null)
                                {
                                    for (int i = 0; i < pl.SongPaths.Count; i++)
                                    {
                                        if (pl.SongPaths[i].StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            string rel = pl.SongPaths[i].Substring(oldPath.Length);
                                            pl.SongPaths[i] = newPath + rel;
                                        }
                                    }
                                }
                            }
                        }

                        settingsService.SaveSettings();

                        // 9. Cập nhật UI
                        UpdateFolderTabs();
                        RefreshPlaylistView();

                        // 10. Khôi phục phát bài hát nếu trước đó đang phát trong folder này
                        if (wasPlayingInThisFolder && currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistPaths.Count)
                        {
                            string newSongPath = playlistService.PlaylistPaths[currentSongIndex];
                            UpdateSongInfo(newSongPath);
                            PlaySong();
                            if (savedTime > TimeSpan.Zero)
                            {
                                audioPlayerService.CurrentTime = savedTime;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Không thể đổi tên thư mục: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void LstPlaylist_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Bỏ menu chuột phải khi đang ở trong danh sách bài hát của Custom Playlist
            if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
            {
                e.Handled = true;
                return;
            }

            var selectedItem = lstPlaylist.SelectedItem as PlaylistItem;
            if (selectedItem == null && e.OriginalSource is DependencyObject dep)
            {
                var listBoxItem = ItemsControl.ContainerFromElement(lstPlaylist, dep) as ListBoxItem;
                if (listBoxItem?.DataContext is PlaylistItem pItem)
                {
                    selectedItem = pItem;
                    _isRightClicking = true;
                    try
                    {
                        lstPlaylist.SelectedItem = pItem;
                    }
                    finally
                    {
                        _isRightClicking = false;
                    }
                }
            }

            if (menuRemoveFromCurrentPlaylist != null)
            {
                menuRemoveFromCurrentPlaylist.Visibility = (_selectedTab != null && _selectedTab.IsCustomPlaylist) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MenuAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = lstPlaylist.SelectedItem as PlaylistItem;
            if (selectedItem == null || string.IsNullOrEmpty(selectedItem.FilePath)) return;

            var dialog = new AddToPlaylistDialog(selectedItem.FilePath, selectedItem.DisplayName, settingsService)
            {
                Owner = this,
                OnPlaylistsSaved = () =>
                {
                    UpdatePlaylistTabs();
                    if (_selectedTab != null && _selectedTab.IsCustomPlaylist)
                    {
                        RefreshPlaylistView();
                    }
                }
            };
            dialog.Show();
        }

        private void PromptCreatePlaylistForSong(PlaylistItem song)
        {
            var result = MessageBox.Show(
                "Bạn chưa tạo Playlist nào.\n\nBạn có muốn tạo Playlist mới ngay bây giờ không?",
                "Chưa Có Playlist",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var selectableSongs = new List<SelectableSongItem>();
                for (int i = 0; i < playlistService.PlaylistPaths.Count; i++)
                {
                    string path = playlistService.PlaylistPaths[i];
                    string displayName = i < playlistService.DisplayPlaylist.Count ? playlistService.DisplayPlaylist[i] : System.IO.Path.GetFileName(path);
                    string folderPath = i < playlistService.PlaylistFolderOrigins.Count ? playlistService.PlaylistFolderOrigins[i] : "";
                    string folderName = !string.IsNullOrEmpty(folderPath) ? System.IO.Path.GetFileName(folderPath.TrimEnd('\\', '/')) : "";
                    if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                    selectableSongs.Add(new SelectableSongItem
                    {
                        FilePath = path,
                        DisplayName = displayName,
                        FolderName = folderName,
                        IsChecked = (song != null && string.Equals(path, song.FilePath, StringComparison.OrdinalIgnoreCase))
                    });
                }

                var dialog = new CreatePlaylistDialog(selectableSongs, initialSelectedSongPath: song?.FilePath, existingPlaylistNames: settingsService.CurrentSettings.CustomPlaylists?.Select(p => p.Name).ToList())
                {
                    Owner = this,
                    OnConfirmed = (d) =>
                    {
                        if (!string.IsNullOrWhiteSpace(d.PlaylistName))
                        {
                            var selectedPaths = d.SelectedSongPaths;
                            if (song != null && !string.IsNullOrEmpty(song.FilePath) && !selectedPaths.Contains(song.FilePath, StringComparer.OrdinalIgnoreCase))
                            {
                                selectedPaths.Add(song.FilePath);
                            }

                            var newPl = new CustomPlaylist
                            {
                                Name = d.PlaylistName,
                                SongPaths = selectedPaths
                            };
                            if (settingsService.CurrentSettings.CustomPlaylists == null) settingsService.CurrentSettings.CustomPlaylists = new List<CustomPlaylist>();
                            settingsService.CurrentSettings.CustomPlaylists.Add(newPl);
                            settingsService.SaveSettings();

                            UpdatePlaylistTabs();

                            var newTab = _playlistTabs.FirstOrDefault(t => !t.IsAllTab && t.PlaylistId == newPl.Id);
                            if (newTab != null)
                            {
                                SelectPlaylistTab(newTab);
                            }
                        }
                    }
                };
                dialog.Show();
            }
        }

        private void AddSongToPlaylist(CustomPlaylist targetPl, PlaylistItem song)
        {
            if (targetPl == null || song == null || string.IsNullOrEmpty(song.FilePath)) return;

            if (!targetPl.SongPaths.Contains(song.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                targetPl.SongPaths.Add(song.FilePath);
                settingsService.SaveSettings();
                UpdatePlaylistTabs();
                if (_selectedTab != null && _selectedTab.IsCustomPlaylist && _selectedTab.PlaylistId == targetPl.Id)
                {
                    RefreshPlaylistView();
                }
                MessageBox.Show($"Đã thêm bài hát \"{song.DisplayName}\" vào playlist \"{targetPl.Name}\"!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Bài hát \"{song.DisplayName}\" đã có sẵn trong playlist \"{targetPl.Name}\".", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuRemoveFromCurrentPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTab == null || !_selectedTab.IsCustomPlaylist) return;
            var currentItem = lstPlaylist.SelectedItem as PlaylistItem;
            if (currentItem == null || string.IsNullOrEmpty(currentItem.FilePath)) return;

            var pl = settingsService.CurrentSettings.CustomPlaylists?.FirstOrDefault(p => p.Id == _selectedTab.PlaylistId);
            if (pl != null)
            {
                pl.SongPaths.RemoveAll(p => string.Equals(p, currentItem.FilePath, StringComparison.OrdinalIgnoreCase));
                settingsService.SaveSettings();
                UpdatePlaylistTabs();
                RefreshPlaylistView();
            }
        }

        private void MenuOpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            var currentItem = lstPlaylist.SelectedItem as PlaylistItem;
            if (currentItem != null && !string.IsNullOrEmpty(currentItem.FilePath) && System.IO.File.Exists(currentItem.FilePath))
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{currentItem.FilePath}\"");
                }
                catch { }
            }
        }

        private void MenuDeleteSong_Click(object sender, RoutedEventArgs e)
        {
            var currentItem = lstPlaylist.SelectedItem as PlaylistItem;
            if (currentItem == null) return;

            int originalIndex = currentItem.OriginalIndex;
            if (originalIndex >= 0 && originalIndex < playlistService.PlaylistPaths.Count)
            {
                string removedFilePath = playlistService.PlaylistPaths[originalIndex];
                var dialog = new DeleteSongDialog(currentItem.DisplayName)
                {
                    Owner = this,
                    OptionSelected = (deleteOption) =>
                    {
                        if (deleteOption == DeleteSongOption.Cancel) return;

                        int songIdx = playlistService.PlaylistPaths.IndexOf(removedFilePath);
                        if (songIdx < 0) return;

                        bool isRemovingPlayingSong = (currentSongIndex == songIdx);
                        if (isRemovingPlayingSong) StopSong();
                        else if (currentSongIndex > songIdx) currentSongIndex--;

                        if (deleteOption == DeleteSongOption.DeleteFromDisk)
                        {
                            try
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                    removedFilePath,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }
                            catch
                            {
                                try { if (System.IO.File.Exists(removedFilePath)) System.IO.File.Delete(removedFilePath); } catch { }
                            }
                        }
                        else
                        {
                            settingsService.CurrentSettings.RemovedSongs.Add(removedFilePath);
                            settingsService.SaveSettings();
                        }

                        playlistService.RemoveSong(songIdx);
                        RefreshPlaylistView();
                        UpdateFolderTabs();
                    }
                };
                dialog.Show();
            }
        }

        // ==================== EQUALIZER HANDLERS ====================

        private EqualizerWindow _activeEqualizerWindow = null;

        private void OpenEqualizerWindow(string targetPath, string targetTitle, string targetFolder, string targetPlaylistId, string targetPlaylistName, EqualizerScope scope)
        {
            try
            {
                if (_activeEqualizerWindow != null && _activeEqualizerWindow.IsLoaded)
                {
                    if (_activeEqualizerWindow.WindowState == WindowState.Minimized)
                        _activeEqualizerWindow.WindowState = WindowState.Normal;
                    _activeEqualizerWindow.Activate();
                    return;
                }

                _activeEqualizerWindow = new EqualizerWindow(
                    settingsService,
                    audioPlayerService,
                    playlistService,
                    targetPath,
                    targetTitle,
                    targetFolder,
                    targetPlaylistId,
                    targetPlaylistName,
                    scope)
                {
                    Owner = this
                };

                _activeEqualizerWindow.Closed += (s, e) =>
                {
                    _activeEqualizerWindow = null;
                };

                _activeEqualizerWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở Equalizer: " + ex.Message, "Lỗi Equalizer", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnEqualizer_Click(object sender, RoutedEventArgs e)
        {
            string curSongPath = (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistPaths.Count)
                ? playlistService.PlaylistPaths[currentSongIndex]
                : "";
            string curSongTitle = (currentSongIndex >= 0 && currentSongIndex < playlistService.DisplayPlaylist.Count)
                ? playlistService.DisplayPlaylist[currentSongIndex]
                : "";
            string curFolder = (_selectedTab != null && !_selectedTab.IsAllTab && !string.IsNullOrEmpty(_selectedTab.FolderPath))
                ? _selectedTab.FolderPath
                : ((currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistFolderOrigins.Count) ? playlistService.PlaylistFolderOrigins[currentSongIndex] : "");
            string curPlaylistId = (_selectedTab != null && _selectedTab.IsCustomPlaylist && !_selectedTab.IsAllTab)
                ? _selectedTab.PlaylistId
                : ((_playbackTab != null && _playbackTab.IsCustomPlaylist && !_playbackTab.IsAllTab) ? _playbackTab.PlaylistId : "");
            string curPlaylistName = (_selectedTab != null && _selectedTab.IsCustomPlaylist && !_selectedTab.IsAllTab)
                ? _selectedTab.Name
                : ((_playbackTab != null && _playbackTab.IsCustomPlaylist && !_playbackTab.IsAllTab) ? _playbackTab.Name : "");

            OpenEqualizerWindow(curSongPath, curSongTitle, curFolder, curPlaylistId, curPlaylistName, EqualizerScope.Global);
        }

        private void MenuEqualizerForSong_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = lstPlaylist.SelectedItem as PlaylistItem;
            string targetPath = selectedItem?.FilePath;
            string targetTitle = selectedItem?.DisplayName;
            if (string.IsNullOrEmpty(targetPath) && currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistPaths.Count)
            {
                targetPath = playlistService.PlaylistPaths[currentSongIndex];
                targetTitle = playlistService.DisplayPlaylist[currentSongIndex];
            }

            if (string.IsNullOrEmpty(targetPath)) return;

            string targetFolder = System.IO.Path.GetDirectoryName(targetPath) ?? "";
            string curPlaylistId = (_selectedTab != null && _selectedTab.IsCustomPlaylist && !_selectedTab.IsAllTab) ? _selectedTab.PlaylistId : "";
            string curPlaylistName = (_selectedTab != null && _selectedTab.IsCustomPlaylist && !_selectedTab.IsAllTab) ? _selectedTab.Name : "";

            OpenEqualizerWindow(targetPath, targetTitle, targetFolder, curPlaylistId, curPlaylistName, EqualizerScope.Song);
        }

        private void MenuTabFolderEqualizer_Click(object sender, RoutedEventArgs e)
        {
            FolderTabItem tab = null;
            if (sender is FrameworkElement elem)
            {
                tab = elem.DataContext as FolderTabItem;
            }
            if (tab == null) tab = _selectedTab;

            if (tab != null && !tab.IsAllTab && !string.IsNullOrEmpty(tab.FolderPath))
            {
                string curSongPath = (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistPaths.Count) ? playlistService.PlaylistPaths[currentSongIndex] : "";
                string curSongTitle = (currentSongIndex >= 0 && currentSongIndex < playlistService.DisplayPlaylist.Count) ? playlistService.DisplayPlaylist[currentSongIndex] : "";

                OpenEqualizerWindow(curSongPath, curSongTitle, tab.FolderPath, "", "", EqualizerScope.Folder);
            }
        }

        private void MenuTabPlaylistEqualizer_Click(object sender, RoutedEventArgs e)
        {
            FolderTabItem tab = null;
            if (sender is FrameworkElement elem)
            {
                tab = elem.DataContext as FolderTabItem;
            }
            if (tab == null) tab = _selectedTab;

            if (tab != null && tab.IsCustomPlaylist && !tab.IsAllTab && !string.IsNullOrEmpty(tab.PlaylistId))
            {
                string curSongPath = (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistPaths.Count) ? playlistService.PlaylistPaths[currentSongIndex] : "";
                string curSongTitle = (currentSongIndex >= 0 && currentSongIndex < playlistService.DisplayPlaylist.Count) ? playlistService.DisplayPlaylist[currentSongIndex] : "";

                OpenEqualizerWindow(curSongPath, curSongTitle, "", tab.PlaylistId, tab.Name, EqualizerScope.Playlist);
            }
        }

        // ==================== SPECTROGRAM HANDLERS ====================

        public string CurrentSongPath => (currentSongIndex >= 0 && currentSongIndex < playlistService.PlaylistPaths.Count) 
            ? playlistService.PlaylistPaths[currentSongIndex] 
            : "";

        private SpectrogramWindow _activeSpectrogramWindow = null;

        public void OpenSpectrogramWindow(string initialFilePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(initialFilePath))
                {
                    initialFilePath = CurrentSongPath;
                }

                if (_activeSpectrogramWindow != null && _activeSpectrogramWindow.IsLoaded)
                {
                    if (_activeSpectrogramWindow.WindowState == WindowState.Minimized)
                        _activeSpectrogramWindow.WindowState = WindowState.Normal;
                    _activeSpectrogramWindow.Activate();
                    if (!string.IsNullOrEmpty(initialFilePath))
                    {
                        _activeSpectrogramWindow.LoadAndAnalyzeFile(initialFilePath);
                    }
                    return;
                }

                _activeSpectrogramWindow = new SpectrogramWindow(initialFilePath)
                {
                    Owner = this
                };

                _activeSpectrogramWindow.Closed += (s, e) =>
                {
                    _activeSpectrogramWindow = null;
                };

                _activeSpectrogramWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở cửa sổ Phổ âm thanh: " + ex.Message, "Lỗi Spectrogram", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSpectrogram_Click(object sender, RoutedEventArgs e)
        {
            OpenSpectrogramWindow();
        }

        private void CanvasVisualizer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            OpenSpectrogramWindow();
        }

        private void MenuSpectrogramForSong_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = lstPlaylist.SelectedItem as PlaylistItem;
            string targetPath = selectedItem?.FilePath;
            if (string.IsNullOrEmpty(targetPath))
            {
                targetPath = CurrentSongPath;
            }

            if (!string.IsNullOrEmpty(targetPath))
            {
                OpenSpectrogramWindow(targetPath);
            }
            else
            {
                OpenSpectrogramWindow();
            }
        }
    }
}
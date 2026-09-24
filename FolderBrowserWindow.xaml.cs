using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace FresAudio
{
    public partial class FolderBrowserWindow : Window
    {
        public string SelectedFolderPath { get; private set; }
        public List<string> SelectedFiles { get; private set; } = new List<string>();
        public Action<string, List<string>>? FolderSelected { get; set; }

        private readonly string[] _audioExtensions = { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma" };

        public FolderBrowserWindow()
        {
            InitializeComponent();
            this.Loaded += FolderBrowserWindow_Loaded;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void FolderBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDrives();
        }

        private void LoadDrives()
        {
            try
            {
                var rootNodes = new ObservableCollection<FolderNode>();
                var thisPcNode = new FolderNode("This PC", "", false, false, PackIconKind.Monitor);
                
                void AddSpecialFolder(string name, string path, PackIconKind icon)
                {
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        thisPcNode.Children.Add(new FolderNode(name, path, false, false, icon));
                    }
                }

                AddSpecialFolder("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), PackIconKind.Monitor);
                AddSpecialFolder("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), PackIconKind.FileDocumentOutline);
                
                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                AddSpecialFolder("Downloads", downloadsPath, PackIconKind.Download);
                
                AddSpecialFolder("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), PackIconKind.MusicNote);
                AddSpecialFolder("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), PackIconKind.VideoOutline);
                AddSpecialFolder("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), PackIconKind.ImageOutline);

                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.IsReady)
                        {
                            string driveLabel = drive.Name;
                            if (!string.IsNullOrWhiteSpace(drive.VolumeLabel))
                            {
                                driveLabel = $"{drive.VolumeLabel} ({drive.Name.Substring(0, 2)})";
                            }
                            else
                            {
                                string typeName = drive.DriveType == DriveType.Removable ? "USB Drive" : "Local Disk";
                                driveLabel = $"{typeName} ({drive.Name.Substring(0, 2)})";
                            }
                            var node = new FolderNode(driveLabel, drive.Name, true, false, PackIconKind.Harddisk);
                            thisPcNode.Children.Add(node);
                        }
                    }
                    catch { }
                }

                var dummyToRemove = thisPcNode.Children.FirstOrDefault(c => c.Name == "Loading...");
                if (dummyToRemove != null)
                {
                    thisPcNode.Children.Remove(dummyToRemove);
                }

                thisPcNode.IsExpanded = true;
                rootNodes.Add(thisPcNode);
                treeFolders.ItemsSource = rootNodes;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải danh sách ổ đĩa: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TreeFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FolderNode node)
            {
                if (node.IsDrive || !string.IsNullOrEmpty(node.FullPath))
                {
                    txtSelectedPath.Text = node.FullPath;
                    LoadFiles(node.FullPath);
                }
                else
                {
                    txtSelectedPath.Text = string.Empty;
                    lstFiles.ItemsSource = null;
                    txtFileCount.Text = "0 bài hát";
                }
            }
        }

        private async void LoadFiles(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    lstFiles.ItemsSource = null;
                    txtFileCount.Text = "0 bài hát";
                    return;
                }

                var files = await System.Threading.Tasks.Task.Run(() =>
                {
                    var result = new List<PreviewSongItem>();
                    try
                    {
                        var dirFiles = Directory.GetFiles(folderPath);
                        foreach (var file in dirFiles)
                        {
                            string ext = Path.GetExtension(file).ToLower();
                            if (_audioExtensions.Contains(ext))
                            {
                                result.Add(new PreviewSongItem { FileName = Path.GetFileName(file), FilePath = file });
                            }
                        }
                    }
                    catch { }
                    
                    return result.OrderBy(x => x.FileName).ToList();
                });

                lstFiles.ItemsSource = files;
                txtFileCount.Text = $"{files.Count} bài hát";
            }
            catch
            {
                lstFiles.ItemsSource = null;
                txtFileCount.Text = "Không có quyền truy cập";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try { this.DialogResult = false; } catch { }
            this.Close();
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (treeFolders.SelectedItem is FolderNode selectedNode)
            {
                SelectedFolderPath = selectedNode.FullPath;
                var items = lstFiles.ItemsSource as IEnumerable<PreviewSongItem>;
                if (items != null)
                {
                    SelectedFiles = items.Where(x => x.IsSelected).Select(x => x.FilePath).ToList();
                }
                try { this.DialogResult = true; } catch { }
                FolderSelected?.Invoke(SelectedFolderPath, SelectedFiles);
                this.Close();
            }
            else
            {
                MessageBox.Show("Vui lòng chọn một thư mục bên trái.", "Chưa chọn thư mục", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnNewFolder_Click(object sender, RoutedEventArgs e)
        {
            ShowNewFolderModal();
        }

        private void MenuItemNewFolder_Click(object sender, RoutedEventArgs e)
        {
            ShowNewFolderModal();
        }

        private void ShowNewFolderModal()
        {
            if (treeFolders.SelectedItem is not FolderNode selectedNode || string.IsNullOrWhiteSpace(selectedNode.FullPath) || !Directory.Exists(selectedNode.FullPath))
            {
                MessageBox.Show("Vui lòng chọn một ổ đĩa hoặc thư mục cha bên trái trước khi tạo thư mục mới.", "Chưa chọn thư mục cha", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            txtParentFolderInfo.Text = $"Tạo trong: {selectedNode.FullPath}";

            // Gợi ý tên thư mục mới chưa trùng
            string baseName = "Thư mục mới";
            string candidateName = baseName;
            int counter = 2;
            while (Directory.Exists(Path.Combine(selectedNode.FullPath, candidateName)))
            {
                candidateName = $"{baseName} ({counter++})";
            }

            txtNewFolderName.Text = candidateName;
            modalNewFolder.Visibility = Visibility.Visible;
            txtNewFolderName.Focus();
            txtNewFolderName.SelectAll();
        }

        private void BtnCancelNewFolder_Click(object sender, RoutedEventArgs e)
        {
            modalNewFolder.Visibility = Visibility.Collapsed;
        }

        private void TxtNewFolderName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewFolder_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewFolder_Click(sender, e);
            }
        }

        private async void BtnConfirmNewFolder_Click(object sender, RoutedEventArgs e)
        {
            if (treeFolders.SelectedItem is not FolderNode selectedNode || string.IsNullOrWhiteSpace(selectedNode.FullPath))
            {
                modalNewFolder.Visibility = Visibility.Collapsed;
                return;
            }

            string folderName = txtNewFolderName.Text.Trim();
            if (string.IsNullOrEmpty(folderName))
            {
                MessageBox.Show("Tên thư mục không được để trống.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNewFolderName.Focus();
                return;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (folderName.IndexOfAny(invalidChars) >= 0)
            {
                MessageBox.Show("Tên thư mục chứa ký tự không hợp lệ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNewFolderName.Focus();
                return;
            }

            string newFullPath = Path.Combine(selectedNode.FullPath, folderName);
            try
            {
                if (Directory.Exists(newFullPath))
                {
                    MessageBox.Show("Thư mục này đã tồn tại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Directory.CreateDirectory(newFullPath);
                modalNewFolder.Visibility = Visibility.Collapsed;

                // Mở rộng thư mục cha và tải lại danh sách thư mục con
                selectedNode.IsExpanded = true;
                await selectedNode.ReloadChildrenAsync();

                // Chọn ngay thư mục mới vừa tạo
                var createdNode = selectedNode.Children.FirstOrDefault(c => string.Equals(c.FullPath, newFullPath, StringComparison.OrdinalIgnoreCase));
                if (createdNode != null)
                {
                    createdNode.IsSelected = true;
                }

                txtSelectedPath.Text = newFullPath;
                LoadFiles(newFullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tạo thư mục: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class FolderNode : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isLoaded;

        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDrive { get; set; }
        public PackIconKind IconKind { get; set; }
        
        public ObservableCollection<FolderNode> Children { get; set; }

        public FolderNode(string name, string fullPath, bool isDrive, bool isDummy = false, PackIconKind? iconKind = null)
        {
            Name = name;
            FullPath = fullPath;
            IsDrive = isDrive;
            IconKind = iconKind ?? (isDrive ? PackIconKind.Harddisk : PackIconKind.Folder);
            Children = new ObservableCollection<FolderNode>();

            if (!isDummy)
            {
                // Add dummy child to show expander arrow
                Children.Add(new FolderNode("Loading...", "", false, true));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));

                    if (_isExpanded && !_isLoaded)
                    {
                        LoadChildren();
                    }
                }
            }
        }

        public async System.Threading.Tasks.Task ReloadChildrenAsync()
        {
            if (string.IsNullOrEmpty(FullPath)) return;
            _isLoaded = true;
            try
            {
                var dirs = await System.Threading.Tasks.Task.Run(() =>
                {
                    var result = new System.Collections.Generic.List<DirectoryInfo>();
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(FullPath))
                        {
                            try
                            {
                                DirectoryInfo di = new DirectoryInfo(dir);
                                if (!di.Attributes.HasFlag(FileAttributes.Hidden) && !di.Attributes.HasFlag(FileAttributes.System))
                                {
                                    result.Add(di);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    return result;
                });

                Children.Clear();
                foreach (var di in dirs)
                {
                    Children.Add(new FolderNode(di.Name, di.FullName, false, false));
                }
            }
            catch
            {
                Children.Clear();
            }
        }

        private async void LoadChildren()
        {
            // Các node ảo (như This PC) không có đường dẫn thực nên không load children từ ổ cứng
            if (string.IsNullOrEmpty(FullPath)) 
            {
                _isLoaded = true;
                return;
            }

            _isLoaded = true;

            try
            {
                // Lấy danh sách thư mục ở thread nền để không đơ UI
                var dirs = await System.Threading.Tasks.Task.Run(() =>
                {
                    var result = new System.Collections.Generic.List<DirectoryInfo>();
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(FullPath))
                        {
                            try
                            {
                                DirectoryInfo di = new DirectoryInfo(dir);
                                if (!di.Attributes.HasFlag(FileAttributes.Hidden) && !di.Attributes.HasFlag(FileAttributes.System))
                                {
                                    result.Add(di);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    return result;
                });

                // Xóa dummy node "Loading..." 
                Children.Clear();

                // Thêm các thư mục con vào
                foreach (var di in dirs)
                {
                    Children.Add(new FolderNode(di.Name, di.FullName, false, false));
                }
            }
            catch 
            {
                Children.Clear();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PreviewSongItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        
        public bool IsSelected 
        { 
            get => _isSelected; 
            set 
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
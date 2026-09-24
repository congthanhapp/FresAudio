using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace FresAudio
{
    public partial class RenameFolderDialog : Window
    {
        public string NewFolderName { get; private set; } = string.Empty;
        private readonly string _currentName;
        private readonly string _fullPath;

        public RenameFolderDialog(string currentName, string fullPath)
        {
            InitializeComponent();
            _currentName = currentName ?? string.Empty;
            _fullPath = fullPath ?? string.Empty;

            txtFolderPath.Text = $"Vị trí: {_fullPath}";
            txtFolderName.Text = _currentName;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtFolderName.Focus();
            txtFolderName.SelectAll();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TxtFolderName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmRename();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            ConfirmRename();
        }

        private void ConfirmRename()
        {
            string name = txtFolderName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Tên thư mục không được để trống.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtFolderName.Focus();
                return;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (name.IndexOfAny(invalidChars) >= 0)
            {
                MessageBox.Show("Tên thư mục chứa ký tự không hợp lệ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtFolderName.Focus();
                return;
            }

            NewFolderName = name;
            DialogResult = true;
            Close();
        }
    }
}

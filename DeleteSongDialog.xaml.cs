using System;
using System.Windows;
using System.Windows.Input;

namespace FresAudio
{
    public enum DeleteSongOption
    {
        Cancel,
        RemoveFromPlaylist,
        DeleteFromDisk
    }

    public partial class DeleteSongDialog : Window
    {
        public DeleteSongOption SelectedOption { get; private set; } = DeleteSongOption.Cancel;
        public Action<DeleteSongOption>? OptionSelected { get; set; }

        public DeleteSongDialog(string songDisplayName)
        {
            InitializeComponent();
            txtSongName.Text = songDisplayName ?? "Bài hát này";
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnRemoveFromList_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = DeleteSongOption.RemoveFromPlaylist;
            try { DialogResult = true; } catch { }
            OptionSelected?.Invoke(DeleteSongOption.RemoveFromPlaylist);
            Close();
        }

        private void BtnDeleteFromDisk_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = DeleteSongOption.DeleteFromDisk;
            try { DialogResult = true; } catch { }
            OptionSelected?.Invoke(DeleteSongOption.DeleteFromDisk);
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = DeleteSongOption.Cancel;
            try { DialogResult = false; } catch { }
            OptionSelected?.Invoke(DeleteSongOption.Cancel);
            Close();
        }
    }
}

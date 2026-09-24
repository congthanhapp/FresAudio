using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FresAudio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string MutexName = "FresAudio_SingleInstance_Mutex_58a1";
    private const string PipeName = "FresAudio_SingleInstance_Pipe_58a1";
    private static Mutex? _mutex;
    private static CancellationTokenSource? _pipeCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // Gửi tham số mở file (nếu có) tới instance đang chạy
            string filePath = string.Join(" ", e.Args).Trim('"', ' ');
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1000);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(filePath);
            }
            catch { }

            // Đóng ngay tiến trình thứ 2 để không mở thêm cửa sổ
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Không cho phép click chuột phải để chọn các tùy chọn trong ContextMenu / MenuItem (chỉ cho phép chuột trái)
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.MenuItem), System.Windows.Controls.MenuItem.PreviewMouseRightButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((s, args) => args.Handled = true));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.MenuItem), System.Windows.Controls.MenuItem.PreviewMouseRightButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler((s, args) => args.Handled = true));

        StartNamedPipeServer();
    }

    private void StartNamedPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    string? receivedPath = await reader.ReadLineAsync();

                    if (!string.IsNullOrWhiteSpace(receivedPath))
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (MainWindow is MainWindow mainWin)
                            {
                                mainWin.OpenFileFromExternal(receivedPath);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _pipeCts?.Cancel();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }

        try
        {
            // Tự động dừng tất cả tiến trình tải ngầm nếu người dùng tắt app
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("yt-dlp"))
            {
                try { proc.Kill(); } catch { }
            }
        }
        catch { }

        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Exception ex = e.Exception;
        string logPath = "crash.log";
        string msg = "";
        while (ex != null) { msg += ex.Message + "\n" + ex.StackTrace + "\n\n"; ex = ex.InnerException; }
        System.IO.File.WriteAllText(logPath, msg);
        e.Handled = true;
        Environment.Exit(1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show("GLOBAL DOMAIN CRASH:\n" + ex.Message + "\n" + ex.StackTrace, "Lỗi Nghiêm Trọng", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

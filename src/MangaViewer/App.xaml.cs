using System.Runtime.InteropServices;
using System.Windows;

namespace MangaViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("MangaViewer.Desktop"));
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0)
            _ = window.OpenBookAsync(e.Args[0]);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

using Windows.Graphics;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace CodexUsageTray;

public sealed partial class MainWindow : Window
{
    private bool _isClosingForExit;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        WindowHandle = WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1180, 780));
        AppWindow.Closing += OnAppWindowClosing;

        MainPage = new MainPage();
        RootFrame.Content = MainPage;
    }

    public MainPage MainPage { get; }

    public IntPtr WindowHandle { get; }

    public void ShowWindow()
    {
        AppWindow.Show();
        Activate();
    }

    public void CloseForExit()
    {
        _isClosingForExit = true;
        Close();
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_isClosingForExit)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }
}

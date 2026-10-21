using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Meetily.App;

public partial class MainWindow : Window
{
    private readonly WebBridge _bridge;

    public MainWindow(WebBridge bridge)
    {
        InitializeComponent();
        _bridge = bridge;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meetily", "WebView2");
        Directory.CreateDirectory(localAppData);

        var environment = await CoreWebView2Environment.CreateAsync(null, localAppData);
        await WebViewControl.EnsureCoreWebView2Async(environment);

        _bridge.Attach(WebViewControl);

        // Virtual host mapping to serve local wwwroot securely
        var wwwrootDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwrootDir))
        {
            WebViewControl.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.meetily.local",
                wwwrootDir,
                CoreWebView2HostResourceAccessKind.Allow
            );
            WebViewControl.Source = new Uri("https://app.meetily.local/index.html");
        }
        else
        {
            // Fallback for dev mode
            WebViewControl.NavigateToString("<html><body style='background:#0F172A;color:white;font-family:sans-serif;padding:40px;'><h2>Meetily Starting...</h2><p>wwwroot folder not found.</p></body></html>");
        }
    }
}

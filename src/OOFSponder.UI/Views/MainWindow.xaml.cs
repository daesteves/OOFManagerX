using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using OOFManagerX.Core;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Models;
using OOFManagerX.Core.Services;
using OOFSponder.UI.Services;
using OOFSponder.UI.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace OOFSponder.UI.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private static readonly string LogsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OOFSponder", "logs");

    // Window size ratios based on 1270x1495 on 4K (3840x2160)
    private const double WidthRatio = 0.33;   // 33% of screen width
    private const double HeightRatio = 0.76;  // 76% of screen height
    private const int MinWidth = 800;
    private const int MinHeight = 700;
    
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "OOFSponder";
    private const string IconResourceName = "OOFSponder.UI.Assets.oofsponder.png";

    private TrayIconService? _trayIcon;
    private AppWindow? _appWindow;
    private bool _isExiting;
    private IBackgroundOOFService? _backgroundService;
    private string? _tempIconPath;
    private TemplatesService? _templatesService;

    public MainWindow()
    {
        ViewModel = App.GetService<MainViewModel>();
        this.InitializeComponent();
        
        // Set window title
        Title = "OOFSponder - Out of Office Manager";

        // Store window handle for MSAL auth
        var hwnd = WindowNative.GetWindowHandle(this);
        App.SetWindowHandle(hwnd);
        
        // Get AppWindow for minimize/close handling
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        
        // Set responsive window size based on screen
        SetResponsiveWindowSize();
        
        // Setup system tray icon
        SetupTrayIcon();
        
        // Handle window closing - minimize to tray instead
        _appWindow.Closing += AppWindow_Closing;
        
        // Initialize async operations
        _ = InitializeAsync();
    }

    private void SetupTrayIcon()
    {
        // Extract icon from embedded resource to temp file
        var iconPath = ExtractIconToTemp();
        
        _trayIcon = new TrayIconService("OOFSponder - Out of Office Manager", iconPath);
        _trayIcon.OnClick += () => this.DispatcherQueue.TryEnqueue(ShowWindow);
        _trayIcon.OnExit += () => this.DispatcherQueue.TryEnqueue(ExitApplication);
        
        // Set taskbar icon to match tray icon
        SetTaskbarIcon(iconPath);
    }

    private string ExtractIconToTemp()
    {
        // Create temp path for icon
        var tempPath = Path.Combine(Path.GetTempPath(), "OOFSponder", "oofsponder.png");
        var tempDir = Path.GetDirectoryName(tempPath)!;
        
        if (!Directory.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }
        
        // Extract embedded resource if not already extracted
        if (!File.Exists(tempPath))
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(IconResourceName);
            if (stream != null)
            {
                using var fileStream = File.Create(tempPath);
                stream.CopyTo(fileStream);
            }
        }
        
        _tempIconPath = tempPath;
        return tempPath;
    }

    private void SetTaskbarIcon(string pngPath)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            
            if (File.Exists(pngPath))
            {
                // Load PNG and convert to icon using System.Drawing
                using var bitmap = new Bitmap(pngPath);
                var hIcon = bitmap.GetHicon();
                
                // Set both small and large icons using Win32 API
                SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon);
                SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon);
            }
        }
        catch
        {
            // Ignore icon setting errors
        }
    }

    // Win32 constants and imports for setting window icon
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, IntPtr lParam);

    private void SetResponsiveWindowSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        
        // Get display area
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        
        // Calculate size based on ratios
        var width = Math.Max((int)(workArea.Width * WidthRatio), MinWidth);
        var height = Math.Max((int)(workArea.Height * HeightRatio), MinHeight);
        
        appWindow.Resize(new SizeInt32(width, height));
        
        // Center on screen
        var x = (workArea.Width - width) / 2 + workArea.X;
        var y = (workArea.Height - height) / 2 + workArea.Y;
        appWindow.Move(new PointInt32(x, y));
    }

    private async Task InitializeAsync()
    {
        // Check if should start minimized
        var settingsService = App.GetService<ISettingsService>();
        var userSettings = await settingsService.LoadUserSettingsAsync();
        
        // Update the toggle states
        StartMinimizedToggle.IsChecked = userSettings.StartMinimized;
        StartWithWindowsToggle.IsChecked = userSettings.StartWithWindows;
        ViewModel.IsMonitoringEnabled = userSettings.MonitoringEnabled;
        
        // Templates loaded lazily on first access
        _templatesService = new TemplatesService();
        
        if (userSettings.StartMinimized)
        {
            // Hide window after a brief delay to allow initialization
            await Task.Delay(100);
            HideWindow();
        }
        
        await ViewModel.InitializeAsync();
        
        // Start background OOF monitoring service (if enabled)
        _backgroundService = App.GetService<IBackgroundOOFService>();
        _backgroundService.SyncStatusChanged += OnSyncStatusChanged;
        
        if (userSettings.MonitoringEnabled)
        {
            await _backgroundService.StartAsync();
        }
        else
        {
            ViewModel.StatusMessage = LocalizedStrings.MonitoringPaused;
        }
    }

    private void OnSyncStatusChanged(object? sender, OOFSyncEventArgs e)
    {
        // Update status in UI thread
        this.DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.StatusMessage = e.Message;
        });
    }

    private async void StartMinimized_Click(object sender, RoutedEventArgs e)
    {
        var settingsService = App.GetService<ISettingsService>();
        var userSettings = await settingsService.LoadUserSettingsAsync();
        
        var newSettings = userSettings with { StartMinimized = StartMinimizedToggle.IsChecked };
        await settingsService.SaveUserSettingsAsync(newSettings);
    }

    private async void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        var isEnabled = StartWithWindowsToggle.IsChecked;
        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
            if (key != null)
            {
                if (isEnabled)
                {
                    // Get the path to the current executable
                    var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
            
            // Persist the setting
            var settingsService = App.GetService<ISettingsService>();
            var userSettings = await settingsService.LoadUserSettingsAsync();
            var newSettings = userSettings with { StartWithWindows = isEnabled };
            await settingsService.SaveUserSettingsAsync(newSettings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update startup registry: {ex.Message}");
            // Revert the toggle if we failed
            StartWithWindowsToggle.IsChecked = !isEnabled;
        }
    }

    private async void MonitoringToggle_Click(object sender, RoutedEventArgs e)
    {
        var isEnabled = ViewModel.IsMonitoringEnabled;
        
        // Persist the setting
        var settingsService = App.GetService<ISettingsService>();
        var userSettings = await settingsService.LoadUserSettingsAsync();
        var newSettings = userSettings with { MonitoringEnabled = isEnabled };
        await settingsService.SaveUserSettingsAsync(newSettings);
        
        // Start or stop the background service
        if (isEnabled)
        {
            await _backgroundService!.StartAsync();
            ViewModel.StatusMessage = LocalizedStrings.MonitoringStarted;
        }
        else
        {
            await _backgroundService!.StopAsync();
            ViewModel.StatusMessage = LocalizedStrings.MonitoringPaused;
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isExiting)
        {
            // Minimize to tray instead of closing
            args.Cancel = true;
            HideWindow();
        }
    }

    private void ShowWindow()
    {
        this.Activate();
        _appWindow?.Show();
    }

    private void HideWindow()
    {
        _appWindow?.Hide();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _backgroundService?.Dispose();
        _trayIcon?.Dispose();
        this.Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private async void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        var logFile = GetLatestLogFile();
        if (logFile == null)
        {
            await ShowMessageAsync("No Logs", "No log files found yet.");
            return;
        }

        try
        {
            var logContent = await File.ReadAllTextAsync(logFile);
            // Show last 100 lines max
            var lines = logContent.Split('\n');
            var recentLines = lines.Length > 100 
                ? string.Join('\n', lines.Skip(lines.Length - 100)) 
                : logContent;

            var dialog = new ContentDialog
            {
                Title = "Recent Logs",
                Content = new ScrollViewer
                {
                    Content = new TextBlock 
                    { 
                        Text = recentLines,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 11,
                        IsTextSelectionEnabled = true,
                        TextWrapping = TextWrapping.Wrap
                    },
                    MaxHeight = 400
                },
                CloseButtonText = "Close",
                SecondaryButtonText = "Open File",
                XamlRoot = this.Content.XamlRoot
            };
            
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                Process.Start(new ProcessStartInfo(logFile) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Error", $"Failed to read logs: {ex.Message}");
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LogsFolder);
        Process.Start(new ProcessStartInfo(LogsFolder) { UseShellExecute = true });
    }

    private void TemplatesFlyout_Opening(object sender, object e)
    {
        RefreshTemplatesMenu();
    }

    private async void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        var inputBox = new TextBox
        {
            PlaceholderText = "Enter template name",
            Width = 300
        };

        var dialog = new ContentDialog
        {
            Title = "Save as Template",
            Content = inputBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
        {
            var template = Template.Create(
                inputBox.Text.Trim(),
                ViewModel.InternalMessage ?? "",
                ViewModel.ExternalMessage ?? ""
            );
            
            await _templatesService!.SaveTemplateAsync(template);
            RefreshTemplatesMenu();
            ViewModel.StatusMessage = $"Template '{template.Name}' saved";
        }
    }

    private async void RefreshTemplatesMenu()
    {
        // Load templates lazily on first access
        if (_templatesService != null && _templatesService.Templates.Count == 0)
        {
            await _templatesService.LoadAsync();
        }
        
        // Remove old template items (keep first 2: Save as Template + Separator)
        while (TemplatesFlyout.Items.Count > 2)
        {
            TemplatesFlyout.Items.RemoveAt(2);
        }

        // Add templates
        if (_templatesService?.Templates.Count > 0)
        {
            foreach (var template in _templatesService.Templates.OrderBy(t => t.Name))
            {
                var subItem = new MenuFlyoutSubItem
                {
                    Text = template.Name,
                    Tag = template.Id
                };
                
                var loadItem = new MenuFlyoutItem
                {
                    Text = "Load",
                    Tag = template.Id,
                    Icon = new FontIcon { Glyph = "\uE8E5" }
                };
                loadItem.Click += LoadTemplate_Click;
                
                var deleteItem = new MenuFlyoutItem
                {
                    Text = "Delete",
                    Tag = template.Id,
                    Icon = new FontIcon { Glyph = "\uE74D" }
                };
                deleteItem.Click += DeleteTemplate_Click;
                
                subItem.Items.Add(loadItem);
                subItem.Items.Add(deleteItem);
                TemplatesFlyout.Items.Add(subItem);
            }
        }
        else
        {
            var noTemplates = new MenuFlyoutItem
            {
                Text = "(No saved templates)",
                IsEnabled = false
            };
            TemplatesFlyout.Items.Add(noTemplates);
        }
    }

    private void LoadTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string templateId)
        {
            var template = _templatesService?.GetTemplate(templateId);
            if (template != null)
            {
                ViewModel.InternalMessage = template.InternalMessage;
                ViewModel.ExternalMessage = template.ExternalMessage;
                ViewModel.StatusMessage = $"Template '{template.Name}' loaded";
            }
        }
    }

    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string templateId)
        {
            var template = _templatesService?.GetTemplate(templateId);
            if (template == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Template",
                Content = $"Are you sure you want to delete '{template.Name}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _templatesService!.DeleteTemplateAsync(templateId);
                ViewModel.StatusMessage = $"Template '{template.Name}' deleted";
            }
        }
    }

    private async void PreviewInternal_Click(object sender, RoutedEventArgs e)
    {
        await ShowMessagePreviewAsync("Internal Message Preview", ViewModel.InternalMessage);
    }

    private async void PreviewExternal_Click(object sender, RoutedEventArgs e)
    {
        await ShowMessagePreviewAsync("External Message Preview", ViewModel.ExternalMessage);
    }

    private async Task ShowMessagePreviewAsync(string title, string? markdownMessage)
    {
        var htmlContent = MarkdownService.ConvertToHtml(markdownMessage);
        
        if (string.IsNullOrWhiteSpace(htmlContent))
        {
            htmlContent = "<div style=\"color:#888;font-style:italic;\">No message content to preview.</div>";
        }

        // Create a WebView2 for rendering HTML
        var webView = new WebView2
        {
            MinHeight = 300,
            MinWidth = 500
        };

        // Wait for WebView2 to initialize
        await webView.EnsureCoreWebView2Async();
        
        // Create full HTML document with styling
        var fullHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{
            font-family: 'Segoe UI', Calibri, Arial, sans-serif;
            font-size: 11pt;
            color: #333;
            padding: 16px;
            margin: 0;
            background-color: #fff;
        }}
    </style>
</head>
<body>
{htmlContent}
</body>
</html>";

        webView.NavigateToString(fullHtml);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = webView,
            CloseButtonText = "Close",
            XamlRoot = this.Content.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.2.0";
        
        var aboutContent = new StackPanel { Spacing = 12 };
        
        aboutContent.Children.Add(new TextBlock 
        { 
            Text = "OOFSponder",
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"]
        });
        
        aboutContent.Children.Add(new TextBlock 
        { 
            Text = "Modern Out of Office Manager for Microsoft 365",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        aboutContent.Children.Add(new TextBlock { Text = "" }); // Spacer

        aboutContent.Children.Add(new TextBlock 
        { 
            Text = $"Version {versionString}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        aboutContent.Children.Add(new TextBlock { Text = "" }); // Spacer

        aboutContent.Children.Add(new TextBlock 
        { 
            Text = "Modernized by:",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        aboutContent.Children.Add(new TextBlock { Text = "Diogo Esteves" });

        aboutContent.Children.Add(new TextBlock { Text = "" }); // Spacer

        aboutContent.Children.Add(new TextBlock 
        { 
            Text = "Original OOFSponder created by:",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        aboutContent.Children.Add(new TextBlock { Text = "Evan Basalik and Cameron Battagler" });
        
        var linkButton = new HyperlinkButton 
        { 
            Content = "github.com/evanbasalik/oofsponder",
            NavigateUri = new Uri("https://github.com/evanbasalik/oofsponder"),
            Padding = new Thickness(0)
        };
        aboutContent.Children.Add(linkButton);

        aboutContent.Children.Add(new TextBlock { Text = "" }); // Spacer

        aboutContent.Children.Add(new TextBlock 
        { 
            Text = "© 2026 - MIT License",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 11
        });

        var dialog = new ContentDialog
        {
            Title = "About OOFSponder",
            Content = aboutContent,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot
        };
        
        await dialog.ShowAsync();
    }

    private static string? GetLatestLogFile()
    {
        if (!Directory.Exists(LogsFolder))
            return null;
            
        return Directory.GetFiles(LogsFolder, "*.log")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}

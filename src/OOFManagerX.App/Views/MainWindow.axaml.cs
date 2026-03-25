using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using OOFManagerX.App.ViewModels;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Services;

namespace OOFManagerX.App.Views;

public partial class MainWindow : Window
{
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "OOFManagerX";
    private int _currentIntervalMs = 5 * 60 * 1000;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateStartAtBootIndicator();
        UpdateIntervalIndicators();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
        App.TrimMemory();
    }

    public void OnSetInterval(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tagStr) return;
        if (!int.TryParse(tagStr, out var intervalMs)) return;

        _currentIntervalMs = intervalMs;
        var bgService = App.Services.GetRequiredService<IBackgroundOOFService>();
        bgService.SetPollingInterval(intervalMs);

        UpdateIntervalIndicators();

        var label = GetIntervalLabel(intervalMs);
        if (DataContext is MainViewModel vm)
            vm.StatusMessage = $"Sync interval set to {label}";
    }

    public void OnToggleStartAtBoot(object? sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = IsStartWithWindowsEnabled();
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (enabled)
            {
                key?.DeleteValue(AppName, false);
            }
            else
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null) key?.SetValue(AppName, $"\"{exePath}\" --minimized");
            }

            UpdateStartAtBootIndicator();
        }
        catch { /* ignore registry errors */ }
    }

    private void UpdateIntervalIndicators()
    {
        var intervals = new (string Name, int Ms, string Label)[]
        {
            ("Interval1m", 60000, "1 minute"),
            ("Interval5m", 300000, "5 minutes"),
            ("Interval15m", 900000, "15 minutes"),
            ("Interval30m", 1800000, "30 minutes"),
            ("Interval1h", 3600000, "1 hour"),
        };

        foreach (var (name, ms, label) in intervals)
        {
            if (this.FindControl<MenuItem>(name) is { } item)
            {
                var selected = _currentIntervalMs == ms;
                item.Header = selected ? $"●  {label}" : $"    {label}";
            }
        }

        // Update parent menu header to show current selection
        if (this.FindControl<MenuItem>("IntervalMenu") is { } parent)
            parent.Header = $"🔄  Sync: {GetIntervalLabel(_currentIntervalMs)}";
    }

    private void UpdateStartAtBootIndicator()
    {
        if (this.FindControl<MenuItem>("StartAtBootMenuItem") is { } item)
        {
            var enabled = IsStartWithWindowsEnabled();
            item.Header = enabled ? "✅  Start at Boot" : "❌  Start at Boot";
        }
    }

    private static string GetIntervalLabel(int ms) => ms switch
    {
        60000 => "1 min",
        300000 => "5 min",
        900000 => "15 min",
        1800000 => "30 min",
        3600000 => "1 hour",
        _ => $"{ms / 1000}s"
    };

    private bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public async void OnViewLogs(object? sender, RoutedEventArgs e)
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OOFManagerX", "logs");

        if (!Directory.Exists(logsDir))
        {
            await ShowTextDialog("Logs", "No log files found.");
            return;
        }

        var latestLog = Directory.GetFiles(logsDir, "*.log")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (latestLog == null)
        {
            await ShowTextDialog("Logs", "No log files found.");
            return;
        }

        var lines = ReadLogFileLines(latestLog, 100);
        await ShowTextDialog("Logs", string.Join("\n", lines));
    }

    /// <summary>
    /// Reads the last N lines from a log file without conflicting with the active writer.
    /// </summary>
    private static List<string> ReadLogFileLines(string path, int lastN)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var allLines = new List<string>();
        while (reader.ReadLine() is { } line)
            allLines.Add(line);
        return allLines.Count <= lastN ? allLines : allLines.GetRange(allLines.Count - lastN, lastN);
    }

    public void OnOpenLogsFolder(object? sender, RoutedEventArgs e)
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OOFManagerX", "logs");
        Directory.CreateDirectory(logsDir);
        Process.Start(new ProcessStartInfo(logsDir) { UseShellExecute = true });
    }

    public async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "2.0.0";

        var accentBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#6366F1"), 0),
                new GradientStop(Color.Parse("#A78BFA"), 1)
            }
        };

        var logoUri = new Uri("avares://OOFManagerX.App/Assets/oofsponder-logo.png");
        var logoImage = new Image
        {
            Source = new Bitmap(Avalonia.Platform.AssetLoader.Open(logoUri)),
            Width = 60, Height = 60
        };

        var originalLink = new TextBlock
        {
            Text = "github.com/evanbasalik/oofsponder",
            FontSize = 11,
            Foreground = (SolidColorBrush)this.FindResource("AccentLightBrush")!,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline,
        };
        originalLink.PointerPressed += (_, _) =>
            Process.Start(new ProcessStartInfo("https://github.com/evanbasalik/oofsponder") { UseShellExecute = true });

        var dialog = new Window
        {
            Title = "About OOFManagerX",
            Width = 420,
            Height = 380,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (SolidColorBrush)this.FindResource("BgBaseBrush")!,
            Content = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    // Gradient header
                    new Border
                    {
                        Height = 80,
                        Background = accentBrush,
                        Child = new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Orientation = Orientation.Horizontal,
                            Spacing = 14,
                            Children =
                            {
                                logoImage,
                                new TextBlock { Text = "OOFManagerX", FontSize = 26, FontWeight = FontWeight.Bold,
                                    Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
                            }
                        }
                    },
                    // Body
                    new StackPanel
                    {
                        Spacing = 14,
                        Margin = new Thickness(28, 24),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Version {version}",
                                FontSize = 14, FontWeight = FontWeight.SemiBold,
                                Foreground = (SolidColorBrush)this.FindResource("TextPrimaryBrush")!
                            },
                            new TextBlock
                            {
                                Text = "Automatic Out-of-Office Manager\nfor Microsoft 365",
                                FontSize = 13, LineHeight = 20,
                                Foreground = (SolidColorBrush)this.FindResource("TextSecondaryBrush")!
                            },
                            new Border
                            {
                                Height = 1,
                                Background = (SolidColorBrush)this.FindResource("SurfaceBorderBrush")!,
                                Margin = new Thickness(0, 2)
                            },
                            new TextBlock
                            {
                                Text = "Created by Diogo Esteves",
                                FontSize = 12, FontWeight = FontWeight.Medium,
                                Foreground = (SolidColorBrush)this.FindResource("TextPrimaryBrush")!
                            },
                            new StackPanel
                            {
                                Spacing = 4,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = "Inspired by the original OOFSponder\nby Evan Basalik & Cameron Battagler",
                                        FontSize = 11, LineHeight = 16,
                                        Foreground = (SolidColorBrush)this.FindResource("TextMutedBrush")!
                                    },
                                    originalLink
                                }
                            },
                            new TextBlock
                            {
                                Text = "MIT License  •  Open Source",
                                FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
                                Foreground = (SolidColorBrush)this.FindResource("AccentLightBrush")!
                            }
                        }
                    }
                }
            }
        };
        await dialog.ShowDialog(this);
    }

    public void OnToggleExternal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsExternalExpanded = !vm.IsExternalExpanded;
    }

    public void OnOpenUpdateUrl(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.UpdateUrl))
            Process.Start(new ProcessStartInfo(vm.UpdateUrl) { UseShellExecute = true });
    }

    public void OnPreviewMessage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Button btn) return;

        var markdown = btn.Tag?.ToString() == "internal" ? vm.InternalMessage : vm.ExternalMessage;
        var html = MarkdownService.ConvertToHtml(markdown);

        var tempFile = Path.Combine(Path.GetTempPath(), "oofmanagerx_preview.html");
        var styledHtml = $"<html><head><style>body{{font-family:Segoe UI,sans-serif;padding:24px;max-width:600px;margin:0 auto}}</style></head><body>{html}</body></html>";
        File.WriteAllText(tempFile, styledHtml);
        Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
    }

    public void OnExit(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private async System.Threading.Tasks.Task ShowTextDialog(string title, string content)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 540,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (SolidColorBrush)this.FindResource("BgBaseBrush")!,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16),
                    FontSize = 12,
                    Foreground = (SolidColorBrush)this.FindResource("TextPrimaryBrush")!,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace")
                }
            }
        };
        await dialog.ShowDialog(this);
    }
}
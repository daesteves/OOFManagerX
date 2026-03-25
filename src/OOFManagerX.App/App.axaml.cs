using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using OOFManagerX.App.Services;
using OOFManagerX.App.ViewModels;
using OOFManagerX.App.Views;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OOFManagerX.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private static Window? _mainWindow;
    private TrayIcon? _trayIcon;
    private UpdateCheckService? _updateService;

    private static IntPtr GetWindowHandle()
    {
        if (_mainWindow?.TryGetPlatformHandle() is { } handle)
            return handle.Handle;
        return IntPtr.Zero;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            var vm = Services.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow { DataContext = vm };
            _mainWindow = mainWindow;

            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SetupTrayIcon(desktop, mainWindow);

            _updateService = Services.GetRequiredService<UpdateCheckService>();
            _updateService.UpdateAvailable += info =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.OnUpdateAvailable(info));
                ShowUpdateToast(info);
            };
            _updateService.Start();

            desktop.ShutdownRequested += (_, _) =>
            {
                _updateService.Stop();
                vm.Dispose();

                // Ensure the process fully exits
                Environment.Exit(0);
            };

            _ = vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, Window mainWindow)
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "OOFManagerX",
            IsVisible = true,
            Menu = new NativeMenu
            {
                new NativeMenuItem("Open OOFManagerX") { Command = new RelayCommand(() => ShowWindow(mainWindow)) },
                new NativeMenuItemSeparator(),
                new NativeMenuItem("Exit") { Command = new RelayCommand(() =>
                {
                    _trayIcon?.Dispose();
                    desktop.Shutdown();
                }) }
            }
        };

        var iconStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("OOFManagerX.App.Assets.oofsponder.png");
        if (iconStream != null)
            _trayIcon.Icon = new WindowIcon(iconStream);

        _trayIcon.Clicked += (_, _) => ShowWindow(mainWindow);
    }

    private static void ShowUpdateToast(UpdateInfo info)
    {
        try
        {
            var label = info.IsPreRelease ? "pre-release" : "release";
            new ToastContentBuilder()
                .AddText("OOFManagerX Update Available")
                .AddText($"Version {info.Version} ({label}) is ready to download.")
                .AddButton(new ToastButton()
                    .SetContent("View Release")
                    .SetProtocolActivation(new Uri(info.ReleaseUrl)))
                .AddButton(new ToastButton()
                    .SetContent("Dismiss")
                    .SetDismissActivation())
                .Show();
        }
        catch { }
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>
    /// Hints the OS to page out unused memory from the working set.
    /// Does NOT force managed GC — the runtime's GCConserveMemory=9 setting
    /// handles heap compaction safely on its own schedule.
    /// </summary>
    internal static void TrimMemory()
    {
        try
        {
            SetProcessWorkingSetSizeW(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSizeW(IntPtr hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

    private static void ConfigureServices(IServiceCollection services)
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OOFManagerX", "logs");

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new FileLoggerProvider(logsDir));
        });

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IScheduleService, ScheduleService>();
        services.AddSingleton<IOOFService, OOFService>();
        services.AddSingleton<IBackgroundOOFService, BackgroundOOFService>();
        services.AddSingleton<IAuthenticationService>(sp =>
            new AuthenticationService(
                sp.GetRequiredService<ILogger<AuthenticationService>>(),
                windowHandleProvider: GetWindowHandle));
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<UpdateCheckService>();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in dataValidationPluginsToRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }

    private class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _action;
        public RelayCommand(Action action) => _action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }
}
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Navigation;
using OOFManagerX.Core;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Services;
using OOFSponder.UI.ViewModels;
using OOFSponder.UI.Views;
using Velopack;

namespace OOFSponder.UI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private static IntPtr _windowHandle;
        
        public static IServiceProvider Services { get; private set; } = null!;
        public static Window MainWindow => ((App)Current)._window!;
        public static string LogsFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OOFSponder", "logs");

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            // Velopack: Handle install/update/uninstall hooks
            VelopackApp.Build().Run();
            
            this.InitializeComponent();
            Services = ConfigureServices();
        }

        public static void SetWindowHandle(IntPtr hwnd)
        {
            _windowHandle = hwnd;
        }

        public static IntPtr GetWindowHandle() => _windowHandle;

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Ensure logs folder exists
            Directory.CreateDirectory(LogsFolder);
            var logFilePath = Path.Combine(LogsFolder, $"oofsponder_{DateTime.Now:yyyyMMdd}.log");

            // Add logging with file output
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new FileLoggerProvider(logFilePath));
            });

            // Add core services with window handle provider
            services.AddSingleton<IAuthenticationService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<AuthenticationService>>();
                return new AuthenticationService(logger, () => _windowHandle);
            });
            
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IScheduleService, ScheduleService>();
            services.AddSingleton<IOOFService, OOFService>();
            services.AddSingleton<IBackgroundOOFService, BackgroundOOFService>();

            // Add ViewModels
            services.AddTransient<MainViewModel>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            _window ??= new MainWindow();
            _window.Activate();
        }

        public static T GetService<T>() where T : class
        {
            return Services.GetRequiredService<T>();
        }
    }

    /// <summary>
    /// Simple file logger provider for persisting logs to disk.
    /// </summary>
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _logFilePath;
        private readonly object _lock = new();

        public FileLoggerProvider(string logFilePath)
        {
            _logFilePath = logFilePath;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName, _logFilePath, _lock);
        }

        public void Dispose() { }
    }

    public class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logFilePath;
        private readonly object _lock;

        public FileLogger(string categoryName, string logFilePath, object lockObj)
        {
            _categoryName = categoryName;
            _logFilePath = logFilePath;
            _lock = lockObj;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            if (exception != null)
            {
                message += Environment.NewLine + exception.ToString();
            }

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, message + Environment.NewLine);
                }
                catch
                {
                    // Ignore logging failures
                }
            }
        }
    }
}

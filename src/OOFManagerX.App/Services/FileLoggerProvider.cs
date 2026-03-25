using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace OOFManagerX.App.Services;

/// <summary>
/// File-based logger provider with daily rotation and automatic cleanup of old logs.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logsDirectory;
    private readonly int _retentionDays;
    private readonly long _maxFileSizeBytes;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly LogWriter _writer;

    /// <param name="logsDirectory">Directory where log files are stored.</param>
    /// <param name="retentionDays">Number of days to keep log files (default: 30).</param>
    /// <param name="maxFileSizeMB">Maximum size per log file in MB before rolling (default: 10).</param>
    public FileLoggerProvider(string logsDirectory, int retentionDays = 30, int maxFileSizeMB = 10)
    {
        _logsDirectory = logsDirectory;
        _retentionDays = retentionDays;
        _maxFileSizeBytes = maxFileSizeMB * 1024L * 1024L;

        Directory.CreateDirectory(_logsDirectory);

        _writer = new LogWriter(_logsDirectory, _maxFileSizeBytes);

        CleanupOldLogs();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer));

    /// <summary>
    /// Removes log files older than the retention period.
    /// </summary>
    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            var oldFiles = Directory.GetFiles(_logsDirectory, "oofmanagerx_*.log")
                .Where(f =>
                {
                    var fi = new FileInfo(f);
                    return fi.LastWriteTime < cutoff;
                });

            foreach (var file in oldFiles)
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    public void Dispose() => _writer.Dispose();
}

/// <summary>
/// Thread-safe writer that handles daily file rotation and size-based rolling.
/// </summary>
internal sealed class LogWriter : IDisposable
{
    private readonly string _logsDirectory;
    private readonly long _maxFileSizeBytes;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string _currentDate = "";
    private int _currentSegment;
    private bool _disposed;

    public LogWriter(string logsDirectory, long maxFileSizeBytes)
    {
        _logsDirectory = logsDirectory;
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    public void Write(string message)
    {
        if (_disposed) return;

        lock (_lock)
        {
            EnsureWriter();
            _writer!.WriteLine(message);
            _writer.Flush();

            // Roll to new segment if file exceeds max size
            if (_writer.BaseStream.Length >= _maxFileSizeBytes)
            {
                _writer.Dispose();
                _writer = null;
                _currentSegment++;
                EnsureWriter();
            }
        }
    }

    private void EnsureWriter()
    {
        var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        if (today != _currentDate)
        {
            _writer?.Dispose();
            _writer = null;
            _currentDate = today;
            _currentSegment = 0;
        }

        if (_writer != null) return;

        var fileName = _currentSegment == 0
            ? $"oofmanagerx_{_currentDate}.log"
            : $"oofmanagerx_{_currentDate}_{_currentSegment}.log";

        var path = Path.Combine(_logsDirectory, fileName);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

/// <summary>
/// Logger that writes structured log entries to a file via the shared LogWriter.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly LogWriter _writer;

    public FileLogger(string category, LogWriter writer)
    {
        // Shorten category: "OOFManagerX.Core.Services.BackgroundOOFService" → "BackgroundOOFService"
        var lastDot = category.LastIndexOf('.');
        _category = lastDot >= 0 ? category[(lastDot + 1)..] : category;
        _writer = writer;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var level = logLevel switch
        {
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => logLevel.ToString()[..3].ToUpperInvariant()
        };

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var message = formatter(state, exception);
        var line = $"{timestamp} [{level}] {_category}: {message}";

        if (exception != null)
            line += Environment.NewLine + exception;

        _writer.Write(line);
    }
}

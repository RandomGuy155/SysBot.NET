using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace SysBot.Base;

/// <summary>
/// Logic wrapper to handle logging (via NLog).
/// </summary>
public static class LogUtil
{
    private const string GeneralLoggerName = "SysBot.General";
    private static readonly string WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
    private static readonly Lock ConfigLock = new();
    private static readonly ConcurrentDictionary<string, Logger> GameLoggers = new(StringComparer.OrdinalIgnoreCase);

    static LogUtil()
    {
        if (!LogConfig.LoggingEnabled)
            return;

        var config = new LoggingConfiguration();
        Directory.CreateDirectory(Path.Combine(WorkingDirectory, "logs"));
        var logfile = CreateFileTarget("logfile", "SysBotLog");
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile, GeneralLoggerName);
        LogManager.Configuration = config;
    }

    private static FileTarget CreateFileTarget(string targetName, string filePrefix) => new(targetName)
    {
        FileName = Path.Combine(WorkingDirectory, "logs", $"{filePrefix}.txt"),
        ArchiveEvery = FileArchivePeriod.Day,
        ArchiveSuffixFormat = "{1:yyyy-MM-dd}",
        ArchiveFileName = Path.Combine(WorkingDirectory, "logs", $"{filePrefix}.{{#}}.txt"),
        ArchiveAboveSize = 104857600, // 100MB (never)
        MaxArchiveFiles = LogConfig.MaxArchiveFiles,
        Encoding = Encoding.Unicode,
        WriteBom = true,
    };

    private static readonly Logger Logger = LogManager.GetLogger(GeneralLoggerName);

    /// <summary>
    /// Optional resolver mapping a log identity (e.g. bot connection label) to a game tag for per-game log files.
    /// </summary>
    public static Func<string, string?>? GameTagResolver { get; set; }

    private static Logger GetLoggerForTag(string? gameTag)
    {
        if (string.IsNullOrWhiteSpace(gameTag) || !LogConfig.LoggingEnabled)
            return Logger;

        return GameLoggers.GetOrAdd(gameTag, static tag =>
        {
            var loggerName = $"SysBot.Game.{tag}";
            lock (ConfigLock)
            {
                var config = LogManager.Configuration;
                if (config is not null)
                {
                    var targetName = $"logfile_{tag}";
                    if (config.FindTargetByName(targetName) is null)
                    {
                        var target = CreateFileTarget(targetName, $"SysBotLog_{tag}");
                        config.AddRule(LogLevel.Debug, LogLevel.Fatal, target, loggerName);
                        LogManager.ReconfigExistingLoggers();
                    }
                }
            }
            return LogManager.GetLogger(loggerName);
        });
    }

    private static Logger GetLoggerForIdentity(string identity)
    {
        var tag = GameTagResolver?.Invoke(identity);
        return GetLoggerForTag(tag);
    }

    public static void LogText(string message) => Logger.Log(LogLevel.Info, message);
    public static void LogText(string message, string? gameTag) => GetLoggerForTag(gameTag).Log(LogLevel.Info, message);

    // hook in here if you want to forward the message elsewhere???
    public static readonly ForwarderList<ILogForwarder> Forwarders = new();

    public static DateTime LastLogged { get; private set; } = DateTime.Now;

    public static void LogError(string message, [CallerMemberName] string identity = "")
    {
        GetLoggerForIdentity(identity).Log(LogLevel.Error, $"{identity} {message}");
        Log(message, identity);
    }

    public static void LogInfo(string message, [CallerMemberName] string identity = "")
    {
        GetLoggerForIdentity(identity).Log(LogLevel.Info, $"{identity} {message}");
        Log(message, identity);
    }

    private static void Log(string message, [CallerMemberName] string identity = "")
    {
        foreach (var fwd in Forwarders.Snapshot)
        {
            try
            {
                fwd.Forward(message, identity);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, $"Failed to forward log from {identity} - {message}");
                Logger.Log(LogLevel.Error, ex);
            }
        }

        LastLogged = DateTime.Now;
    }

    public static void LogSafe(Exception exception, [CallerMemberName] string identity = "")
    {
        var logger = GetLoggerForIdentity(identity);
        logger.Log(LogLevel.Error, $"Exception from {identity}:");
        logger.Log(LogLevel.Error, exception);

        var err = exception.InnerException;
        while (err is not null)
        {
            logger.Log(LogLevel.Error, err);
            err = err.InnerException;
        }
    }
}

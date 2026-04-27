using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Api.Logging;

/// <summary>
/// Configures logging levels based on command-line arguments.
/// Higher log values = more verbose logging.
/// </summary>
public static class LogLevelConfigurator
{
    /// <summary>
    /// Parses the log level from command-line arguments (format: log=N where N is 0-10).
    /// Returns null if no log argument is found.
    /// </summary>
    public static int? ParseLogLevelFromArgs(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("log=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["log=".Length..];
                if (int.TryParse(value, out var level) && level >= 0)
                {
                    return Math.Min(level, 10);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Configures logging level based on the numeric intensity (0-10).
    /// Higher values enable more verbose logging.
    /// </summary>
    public static void ConfigureLogging(WebApplicationBuilder builder, int logLevel)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        var level = MapToLogLevel(logLevel);

        builder.Logging.SetMinimumLevel(level);

        builder.Logging.AddFilter("Microsoft.AspNetCore", logLevel >= 8 ? LogLevel.Debug : LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting", logLevel >= 7 ? LogLevel.Information : LogLevel.Warning);
        builder.Logging.AddFilter("System", logLevel >= 9 ? LogLevel.Debug : LogLevel.Warning);

        builder.Logging.AddFilter("Jibo.Cloud", logLevel >= 5 ? LogLevel.Debug : LogLevel.Information);
        builder.Logging.AddFilter("Jibo.Cloud.Application", logLevel >= 3 ? LogLevel.Debug : LogLevel.Information);
        builder.Logging.AddFilter("Jibo.Cloud.Infrastructure", logLevel >= 4 ? LogLevel.Debug : LogLevel.Information);
    }

    private static LogLevel MapToLogLevel(int value)
    {
        return value switch
        {
            0 => LogLevel.Error,
            1 => LogLevel.Warning,
            2 => LogLevel.Warning,
            3 => LogLevel.Information,
            4 => LogLevel.Information,
            5 => LogLevel.Information,
            6 => LogLevel.Debug,
            7 => LogLevel.Debug,
            8 => LogLevel.Debug,
            9 => LogLevel.Trace,
            10 => LogLevel.Trace,
            _ => LogLevel.Information
        };
    }
}

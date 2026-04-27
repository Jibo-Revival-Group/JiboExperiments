using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Application.Logging;

/// <summary>
/// Provides detailed operation logging that activates based on log intensity level.
/// Higher log levels = more detailed logging.
/// </summary>
public sealed class DetailedOperationLogger
{
    private readonly ILogger _logger;
    private readonly int _configuredLogLevel;

    public DetailedOperationLogger(ILogger logger, int? configuredLogLevel = null)
    {
        _logger = logger;
        _configuredLogLevel = configuredLogLevel ?? 3;
    }

    /// <summary>
    /// Log method entry at Debug level when log level >= 3
    /// </summary>
    public void LogEntry(string methodName, params (string Key, object? Value)[] parameters)
    {
        if (_configuredLogLevel < 3) return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var paramStr = parameters.Length > 0
                ? string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))
                : "none";
            _logger.LogDebug("[ENTRY] {MethodName}({Parameters})", methodName, paramStr);
        }
    }

    /// <summary>
    /// Log method exit at Debug level when log level >= 3
    /// </summary>
    public void LogExit(string methodName, string? result = null)
    {
        if (_configuredLogLevel < 3) return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var resultStr = result ?? "void";
            _logger.LogDebug("[EXIT] {MethodName} -> {Result}", methodName, resultStr);
        }
    }

    /// <summary>
    /// Log a detailed operation step at Debug level when log level >= 4
    /// </summary>
    public void LogStep(string operation, string step, string? details = null)
    {
        if (_configuredLogLevel < 4) return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var detailStr = details != null ? $" | {details}" : "";
            _logger.LogDebug("[STEP] {Operation}.{Step}{Details}", operation, step, detailStr);
        }
    }

    /// <summary>
    /// Log state information at Debug level when log level >= 5
    /// </summary>
    public void LogState(string context, string stateName, object? value)
    {
        if (_configuredLogLevel < 5) return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[STATE] {Context}.{StateName} = {Value}", context, stateName, value);
        }
    }

    /// <summary>
    /// Log decision information at Information level when log level >= 3
    /// </summary>
    public void LogDecision(string context, string decision, string? reason = null)
    {
        if (_configuredLogLevel < 3) return;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var reasonStr = reason != null ? $" (reason: {reason})" : "";
            _logger.LogInformation("[DECISION] {Context}: {Decision}{Reason}", context, decision, reasonStr);
        }
    }

    /// <summary>
    /// Log performance timing at Debug level when log level >= 6
    /// </summary>
    public void LogTiming(string operation, long elapsedMs)
    {
        if (_configuredLogLevel < 6) return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[TIMING] {Operation} completed in {ElapsedMs}ms", operation, elapsedMs);
        }
    }

    /// <summary>
    /// Log data payload at Trace level when log level >= 8
    /// </summary>
    public void LogPayload(string context, string dataType, int dataSize, string? preview = null)
    {
        if (_configuredLogLevel < 8) return;

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            var previewStr = preview != null ? $" preview: {preview}" : "";
            _logger.LogTrace("[PAYLOAD] {Context} {DataType} size={Size}{Preview}", context, dataType, dataSize, previewStr);
        }
    }

    /// <summary>
    /// Log external call at Debug level when log level >= 5
    /// </summary>
    public void LogExternalCall(string service, string operation, string? details = null)
    {
        if (_configuredLogLevel < 5) return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var detailStr = details != null ? $" ({details})" : "";
            _logger.LogDebug("[EXTERNAL] {Service}.{Operation}{Details}", service, operation, detailStr);
        }
    }

    /// <summary>
    /// Log match/pattern information at Debug level when log level >= 4
    /// </summary>
    public void LogMatch(string context, string pattern, string input, bool matched)
    {
        if (_configuredLogLevel < 4) return;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[MATCH] {Context}: Pattern '{Pattern}' against '{Input}' => {Result}",
                context, pattern, input, matched ? "MATCHED" : "NO MATCH");
        }
    }
}

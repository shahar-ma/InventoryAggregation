namespace InventoryAggregation.Api.Infrastructure;

internal static class InventoryLoggingExtensions
{
    private static readonly Action<ILogger, int, Exception?> LogApiPageCallCanceledDelegate =
    LoggerMessage.Define<int>(
        logLevel: LogLevel.Warning,
        eventId: new EventId(1001, nameof(LogApiPageCallCanceled)),
        formatString: "Inventory API call for page {Page} was cancelled by a timeout.");

    private static readonly Action<ILogger, int, Exception?> LogApiPageCallFailureDelegate =
    LoggerMessage.Define<int>(
        logLevel: LogLevel.Warning,
        eventId: new EventId(1002, nameof(LogApiPageCallFailure)),
        formatString: "Transport failure calling the inventory API for page {Page}.");

    private static readonly Action<ILogger, int, int, Exception?> LogApiPageCallFailureWithStatusDelegate =
    LoggerMessage.Define<int, int>(
        logLevel: LogLevel.Warning,
        eventId: new EventId(1003, nameof(LogApiPageCallFailureWithStatus)),
        formatString: "Inventory API returned HTTP {StatusCode} for page {Page}.");

    private static readonly Action<ILogger, int, Exception?> LogApiPageCallRejectedDelegate =
    LoggerMessage.Define<int>(
        logLevel: LogLevel.Warning,
        eventId: new EventId(1004, nameof(LogApiPageCallTimeout)),
        formatString: "Inventory API call for page {Page} was rejected by the resilience pipeline.");

    private static readonly Action<ILogger, int, Exception?> LogApiPageCallReturnInvalidContentDelegate =
    LoggerMessage.Define<int>(
        logLevel: LogLevel.Warning,
        eventId: new EventId(1005, nameof(LogApiPageCallReturnInvalidContent)),
        formatString: "Inventory API returned an unreadable body for page {Page}.");

    private static readonly Action<ILogger, int, Exception?> LogApiPageCallTimeoutDelegate =
    LoggerMessage.Define<int>(
        logLevel: LogLevel.Warning,
        eventId: new EventId(1006, nameof(LogApiPageCallTimeout)),
        formatString: "Inventory API call for page {Page} timed out.");

    private static readonly Action<ILogger, int, int, int, string, Exception?> LogPageFailedValidationDelegate =
    LoggerMessage.Define<int, int, int, string>(
        logLevel: LogLevel.Information,
        eventId: new EventId(1007, nameof(LogPageFailedValidation)),
        formatString: "Page {Page} failed validation (attempt {Attempt} of {MaxAttempts}): {Problem}");

    private static readonly Action<ILogger, int, int, int, Exception?> LogRetrievedRecordsDelegate =
        LoggerMessage.Define<int, int, int>(
        logLevel: LogLevel.Information,
        eventId: new EventId(1008, nameof(LogRetrievedRecords)),
        formatString: "Retrieved {RecordCount} records from {PageCount} page(s); {Duplicates} duplicate(s) discarded.");

    private static readonly Action<ILogger, int, int, Exception?> LogUnexpectedPageSizeDelegate =
        LoggerMessage.Define<int, int> (
        logLevel: LogLevel.Information,
        eventId: new EventId(1008, nameof(LogUnexpectedPageSize)),
        formatString: "expected {ExpectedCount} records but received {Count}");

    internal static void LogApiPageCallCanceled(this ILogger logger, int page, Exception? exception = null)
        => LogApiPageCallCanceledDelegate(logger, page, exception);

    internal static void LogApiPageCallFailure(this ILogger logger, int page, Exception? exception = null)
        => LogApiPageCallFailureDelegate(logger, page, exception);

    internal static void LogApiPageCallFailureWithStatus(this ILogger logger, int status, int page, Exception? exception = null)
        => LogApiPageCallFailureWithStatusDelegate(logger, status, page, exception);

    internal static void LogApiPageCallRejected(this ILogger logger, int page, Exception? exception = null)
        => LogApiPageCallRejectedDelegate(logger, page, exception);

    internal static void LogApiPageCallReturnInvalidContent(this ILogger logger, int page, Exception? exception = null)
        => LogApiPageCallReturnInvalidContentDelegate(logger, page, exception);

    internal static void LogApiPageCallTimeout(this ILogger logger, int page, Exception? exception = null)
        => LogApiPageCallTimeoutDelegate(logger, page, exception);

    internal static void LogPageFailedValidation(this ILogger logger, int page, int attempt, int maxAttempts, string problem, Exception? exception = null)
        => LogPageFailedValidationDelegate(logger, page, attempt, maxAttempts, problem, exception);

    internal static void LogRetrievedRecords(this ILogger logger, int count, int length, int duplicates, Exception? exception = null)
        => LogRetrievedRecordsDelegate(logger, count, length, duplicates, exception);

    internal static void LogUnexpectedPageSize(this ILogger logger, int expectedCount, int actualCount, Exception? exception = null)
        => LogUnexpectedPageSizeDelegate(logger, expectedCount, actualCount, exception);
}

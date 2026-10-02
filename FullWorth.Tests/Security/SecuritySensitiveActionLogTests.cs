using FullWorth.API.Infrastructure;
using Microsoft.Extensions.Logging;

namespace FullWorth.Tests.Security;

public sealed class SecuritySensitiveActionLogTests
{
    [Fact]
    public void AdminMutation_UsesDedicatedCategoryAndFixedEvent()
    {
        var factory =
            new RecordingLoggerFactory();
        var logger =
            SecuritySensitiveActionLog.CreateLogger(
                factory);

        SecuritySensitiveActionLog.AdminMutationCompleted(
            logger,
            "StaffRoleAssigned");

        Assert.Equal(
            "FullWorth.SecurityEvents",
            factory.CategoryName);

        var entry =
            Assert.Single(
                factory.Entries);

        Assert.Equal(
            29011,
            entry.EventId.Id);

        Assert.Contains(
            "admin_mutation_completed",
            entry.Message,
            StringComparison.Ordinal);

        Assert.Contains(
            "StaffRoleAssigned",
            entry.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AdminMutation_RejectsUnknownActionInsteadOfLoggingIt()
    {
        var factory =
            new RecordingLoggerFactory();
        var logger =
            SecuritySensitiveActionLog.CreateLogger(
                factory);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                SecuritySensitiveActionLog.AdminMutationCompleted(
                    logger,
                    "attacker@example.com secret-token"));

        Assert.Empty(
            factory.Entries);
    }

    [Fact]
    public void AccountExport_InvalidRequestIdIsReplaced()
    {
        var factory =
            new RecordingLoggerFactory();
        var logger =
            SecuritySensitiveActionLog.CreateLogger(
                factory);

        SecuritySensitiveActionLog.AccountExportCompleted(
            logger,
            "attacker@example.com\r\nsecret-token");

        var entry =
            Assert.Single(
                factory.Entries);

        Assert.Equal(
            29012,
            entry.EventId.Id);

        Assert.Contains(
            "<unavailable>",
            entry.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "attacker",
            entry.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "secret",
            entry.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountDeletion_PreservesServerGeneratedRequestId()
    {
        const string requestId =
            "0123456789abcdef0123456789abcdef";

        var factory =
            new RecordingLoggerFactory();
        var logger =
            SecuritySensitiveActionLog.CreateLogger(
                factory);

        SecuritySensitiveActionLog.AccountDeletionCompleted(
            logger,
            requestId);

        var entry =
            Assert.Single(
                factory.Entries);

        Assert.Equal(
            29013,
            entry.EventId.Id);

        Assert.Contains(
            "account_deletion_completed",
            entry.Message,
            StringComparison.Ordinal);

        Assert.Contains(
            requestId,
            entry.Message,
            StringComparison.Ordinal);
    }

    private sealed class RecordingLoggerFactory
        : ILoggerFactory
    {
        internal List<LogEntry> Entries { get; } =
            [];

        internal string? CategoryName { get; private set; }

        public ILogger CreateLogger(
            string categoryName)
        {
            CategoryName =
                categoryName;

            return new RecordingLogger(
                Entries);
        }

        public void AddProvider(
            ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(
        List<LogEntry> entries)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(
            TState state)
            where TState : notnull =>
                null;

        public bool IsEnabled(
            LogLevel logLevel) =>
                true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(
                new LogEntry(
                    logLevel,
                    eventId,
                    formatter(
                        state,
                        exception)));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message);
}

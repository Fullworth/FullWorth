using System.Net;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidResponseDeadlineTests
{
    private static readonly TimeSpan TestGuard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(10, 10, false)]
    [InlineData(-1, 100, false)]
    [InlineData(300, 100, false)]
    [InlineData(10, 10, true)]
    public async Task StalledBody_UsesFiniteDeadline_AndDisposesResponse(
        int configuredSeconds, int deadlineSeconds, bool providerError)
    {
        var clock = new ManualClock();
        using var body = new ControlledBody();
        using var handler = new ResponseHandler(body,
            providerError ? HttpStatusCode.BadGateway : HttpStatusCode.OK);
        using var http = new HttpClient(handler)
        {
            Timeout = configuredSeconds < 0
                ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(configuredSeconds)
        };
        using var caller = new CancellationTokenSource();
        var operation = CreateClient(http, clock).PostAsync("transactions/sync", new { }, caller.Token);
        try
        {
            await body.Started[0].Task.WaitAsync(TestGuard);
            clock.Advance(TimeSpan.FromSeconds(deadlineSeconds - 1));
            Assert.False(operation.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestGuard));
            Assert.False(caller.IsCancellationRequested);
            Assert.True(body.Disposed);
            Assert.Equal(0, clock.ActiveTimers);
            Assert.Equal(1, handler.RequestCount); // No automatic replay of provider requests.
        }
        finally
        {
            caller.Cancel();
        }
    }

    [Fact]
    public async Task StalledHeadersUseTheSameDeadline()
    {
        var clock = new ManualClock();
        using var body = new ControlledBody();
        using var handler = new ResponseHandler(body, holdHeaders: true);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var caller = new CancellationTokenSource();
        var operation = CreateClient(http, clock).PostAsync("accounts/get", new { }, caller.Token);
        try
        {
            await handler.Started.Task.WaitAsync(TestGuard);
            clock.Advance(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestGuard));
            Assert.False(caller.IsCancellationRequested);
            Assert.Equal(0, clock.ActiveTimers);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            caller.Cancel();
        }
    }

    [Fact]
    public async Task HeaderTimeAndPartialBodyShareOneDeadline()
    {
        var clock = new ManualClock();
        using var body = new ControlledBody();
        using var handler = new ResponseHandler(body, HttpStatusCode.OK, holdHeaders: true);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var caller = new CancellationTokenSource();
        var operation = CreateClient(http, clock).PostAsync("accounts/get", new { }, caller.Token);
        try
        {
            await handler.Started.Task.WaitAsync(TestGuard);
            clock.Advance(TimeSpan.FromSeconds(6));
            handler.Headers.TrySetResult();
            await body.Started[0].Task.WaitAsync(TestGuard);
            body.Results[0].TrySetResult(Encoding.UTF8.GetBytes("{"));
            await body.Started[1].Task.WaitAsync(TestGuard);
            clock.Advance(TimeSpan.FromSeconds(4));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestGuard));
            Assert.True(body.Disposed);
            Assert.Equal(0, clock.ActiveTimers);
        }
        finally
        {
            caller.Cancel();
        }
    }

    [Fact]
    public async Task SlowBodyProgressDoesNotRenewDeadline()
    {
        var clock = new ManualClock();
        using var body = new ControlledBody();
        using var handler = new ResponseHandler(body);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var caller = new CancellationTokenSource();
        var operation = CreateClient(http, clock).PostAsync("accounts/get", new { }, caller.Token);
        try
        {
            await body.Started[0].Task.WaitAsync(TestGuard);
            clock.Advance(TimeSpan.FromSeconds(6));
            body.Results[0].TrySetResult(Encoding.UTF8.GetBytes("{"));
            await body.Started[1].Task.WaitAsync(TestGuard);
            clock.Advance(TimeSpan.FromSeconds(4));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestGuard));
            Assert.True(body.Disposed);
        }
        finally
        {
            caller.Cancel();
        }
    }

    [Fact]
    public async Task CallerCancellationRemainsImmediate_AndDisposesDeadline()
    {
        var clock = new ManualClock();
        using var body = new ControlledBody();
        using var handler = new ResponseHandler(body);
        using var http = new HttpClient(handler);
        using var caller = new CancellationTokenSource();
        var operation = CreateClient(http, clock).PostAsync("accounts/get", new { }, caller.Token);
        await body.Started[0].Task.WaitAsync(TestGuard);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestGuard));
        Assert.True(caller.IsCancellationRequested);
        Assert.True(body.Disposed);
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedResponseDisposesDeadline_AndPreservesResult(bool providerError)
    {
        var clock = new ManualClock();
        using var body = new ControlledBody();
        body.Results[0].TrySetResult(Encoding.UTF8.GetBytes(providerError
            ? "{\"error_type\":\"ITEM_ERROR\",\"error_code\":\"ITEM_LOGIN_REQUIRED\"}"
            : "{\"accounts\":[]}"));
        body.Results[1].TrySetResult(null);
        using var handler = new ResponseHandler(body,
            providerError ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var client = CreateClient(http, clock);
        if (providerError)
        {
            var error = await Assert.ThrowsAsync<PlaidApiException>(() =>
                client.PostAsync("accounts/get", new { }));
            Assert.Equal("ITEM_LOGIN_REQUIRED", error.ErrorCode);
        }
        else
        {
            using var result = await client.PostAsync("accounts/get", new { });
            Assert.Equal(0, result.RootElement.GetProperty("accounts").GetArrayLength());
        }

        Assert.True(body.Disposed);
        Assert.Equal(0, clock.ActiveTimers);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TimedOutDeltaDoesNotAdvanceCursorOrTransactionFreshness()
    {
        await using var database = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var clock = new ManualClock();
        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        var userId = Guid.NewGuid();
        var oldSync = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var connection = new BankConnectionEntity
        {
            UserId = userId, InstitutionName = "Fixture bank", PlaidItemId = "fixture-item",
            ProtectedPlaidAccessToken = protector.Protect("fixture-access-token"),
            Status = BankConnectionStatus.Active, TransactionsCursor = "cursor-before",
            LastSuccessfulSyncAtUtc = oldSync
        };
        database.BankConnections.Add(connection);
        database.BankAccounts.Add(new BankAccountEntity
        {
            UserId = userId, BankConnectionId = connection.Id, PlaidAccountId = "fixture-account",
            Name = "Checking", AccountType = BankAccountType.Checking, IsActive = true
        });
        await database.SaveChangesAsync();
        using var body = new ControlledBody();
        using var handler = new ResponseHandler(body);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var caller = new CancellationTokenSource();
        var sync = new PlaidTransactionSyncService(database, CreateClient(http, clock), protector);
        var operation = sync.SyncConnectionAsync(userId, connection.Id, caller.Token);
        try
        {
            await body.Started[0].Task.WaitAsync(TestGuard);
            body.Results[0].TrySetResult(Encoding.UTF8.GetBytes("{\"added\":["));
            await body.Started[1].Task.WaitAsync(TestGuard);
            clock.Advance(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestGuard));
            Assert.Equal("cursor-before", connection.TransactionsCursor);
            Assert.Equal(oldSync, connection.LastSuccessfulSyncAtUtc);
            await database.SaveChangesAsync();
            var stored = await database.BankConnections.AsNoTracking().SingleAsync();
            Assert.Equal("cursor-before", stored.TransactionsCursor);
            Assert.Equal(oldSync, stored.LastSuccessfulSyncAtUtc);
            Assert.Empty(await database.BankTransactions.ToListAsync());
        }
        finally
        {
            caller.Cancel();
        }
    }

    private static PlaidApiClient CreateClient(HttpClient http, TimeProvider clock) =>
        new(http, Options.Create(new PlaidOptions
        {
            ClientId = "fixture-client", Secret = "fixture-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }), clock);

    private sealed class ResponseHandler(
        ControlledBody body,
        HttpStatusCode status = HttpStatusCode.OK,
        bool holdHeaders = false) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Headers { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Started.TrySetResult();
            if (holdHeaders) await Headers.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StreamContent(body) };
        }
    }

    private sealed class ControlledBody : Stream
    {
        private int _read;
        public TaskCompletionSource[] Started { get; } = Enumerable.Range(0, 4)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        public TaskCompletionSource<byte[]?>[] Results { get; } = Enumerable.Range(0, 4)
            .Select(_ => new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var index = _read++;
            Started[index].TrySetResult();
            var bytes = await Results[index].Task.WaitAsync(cancellationToken);
            if (bytes is null) return 0;
            bytes.AsMemory().CopyTo(buffer);
            return bytes.Length;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }

    // The tests advance the CTS timer directly, without wall-clock sleeps or a
    // new test package. Implements the one-shot timers used by the client.
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _elapsed;
        public int ActiveTimers { get { lock (_timers) return _timers.Count(t => !t.Disposed); } }
        public override DateTimeOffset GetUtcNow() { lock (_timers) return DateTimeOffset.UnixEpoch + _elapsed; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Only one-shot timers are used.");
            lock (_timers)
            {
                var timer = new ManualTimer(this, callback, state);
                _timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }
        public void Advance(TimeSpan amount)
        {
            ManualTimer[] due;
            lock (_timers)
            {
                _elapsed += amount;
                due = _timers.Where(t => !t.Disposed && t.Due is { } deadline && deadline <= _elapsed).ToArray();
                foreach (var timer in due) timer.Due = null;
            }
            foreach (var timer in due) timer.Fire();
        }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan? Due { get; set; }
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._timers)
                {
                    if (Disposed) return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._elapsed + dueTime;
                    return true;
                }
            }
            public void Fire() => callback(state);
            public void Dispose() { lock (clock._timers) { Disposed = true; Due = null; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

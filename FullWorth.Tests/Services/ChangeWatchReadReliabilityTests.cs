using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.API.Services.Statements;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class ChangeWatchReadReliabilityTests
{
    [Fact]
    public async Task IncompleteFirstRead_PreventsAnyOtherScopedReadFromStarting()
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ReadProbe(owner) { FirstReadGate = gate };
        var bills = new[] { new BillStreamReadRecord(Guid.NewGuid(), "Fixture", BillCategory.Internet) };
        var service = new PlanningBillChangeWatchService(new Streams(owner, bills), new Facts(probe),
            new Changes(probe), new PlanningPaycheckAllocationStore(db, TimeProvider.System));
        var request = service.GetAsync(owner);
        try
        {
            Assert.Equal(new[] { "facts" }, probe.Calls.Select(x => x.Kind));
            Assert.False(request.IsCompleted);
            gate.SetResult(true);
            Assert.Single(await request);
        }
        finally
        {
            gate.TrySetResult(true);
            await Record.ExceptionAsync(() => request);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(1001)]
    public async Task Reads_AwaitScopedAdapters_AndIncludeEveryBoundedBatch(int count)
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var bills = Enumerable.Range(0, count)
            .Select(i => new BillStreamReadRecord(Guid.NewGuid(), $"Fixture {i}", BillCategory.Internet))
            .ToArray();
        var probe = new ReadProbe(owner);
        var streams = new Streams(owner, bills);
        var facts = new Facts(probe);
        var changes = new Changes(probe);
        var service = new PlanningBillChangeWatchService(streams, facts, changes,
            new PlanningPaycheckAllocationStore(db, TimeProvider.System));

        if (count > 0)
            db.PlanningPaycheckAllocations.Add(new PlanningPaycheckAllocationEntity
            {
                UserId = owner, PayrollTransactionId = Guid.NewGuid(),
                BillStreamId = bills[^1].BillStreamId, SourceStatementId = bills[^1].BillStreamId,
                PaycheckPostedDate = new DateOnly(2026, 9, 25),
                BillPeriodEnd = new DateOnly(2026, 10, 31),
                BillDueDate = new DateOnly(2026, 11, 15), PlannedAmount = 200m, CurrencyCode = "USD"
            });
        await db.SaveChangesAsync();
        var result = await service.GetAsync(owner);
        Assert.Equal(count, result.Count);
        Assert.Equal(count, result.Select(x => x.BillStreamId).Distinct().Count());
        Assert.Equal(bills.Select(x => x.BillStreamId).Order(), result.Select(x => x.BillStreamId).Order());
        Assert.All(result, item =>
        {
            Assert.Equal(100m, item.AmountDifference);
            Assert.Equal(1200m, item.AnnualizedImpact);
            Assert.Equal(600m - item.AlreadyPlanned, item.RemainingAmountToPlan);
        });
        if (count > 0)
            Assert.Equal(200m, result.Single(x => x.BillStreamId == bills[^1].BillStreamId).AlreadyPlanned);
        Assert.Equal((count + 499) / 500 * 2, probe.Calls.Count);
        Assert.All(probe.Calls, call => Assert.InRange(call.Count, 1, 500));
        Assert.Equal(Enumerable.Range(0, (count + 499) / 500)
            .SelectMany(_ => new[] { "facts", "changes" }), probe.Calls.Select(x => x.Kind));
        Assert.False(probe.Busy);
    }

    [Theory]
    [InlineData("facts", false)]
    [InlineData("facts", true)]
    [InlineData("changes", false)]
    [InlineData("changes", true)]
    public async Task AdapterFailure_StopsFurtherReads_AndCanRetry(string stage, bool canceled)
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var bills = Enumerable.Range(0, 501)
            .Select(i => new BillStreamReadRecord(Guid.NewGuid(), $"Fixture {i}", BillCategory.Other)).ToArray();
        using var stop = new CancellationTokenSource();
        var probe = new ReadProbe(owner) { FailStage = stage, Cancel = canceled ? stop.Cancel : null };
        var service = new PlanningBillChangeWatchService(new Streams(owner, bills),
            new Facts(probe), new Changes(probe), new PlanningPaycheckAllocationStore(db, TimeProvider.System));
        var error = await Record.ExceptionAsync(() => service.GetAsync(owner, stop.Token));
        if (canceled) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(stage == "facts" ? 1 : 2, probe.Calls.Count);
        Assert.False(db.ChangeTracker.HasChanges());
        probe.FailStage = null;
        Assert.Equal(501, (await service.GetAsync(owner)).Count);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(1001)]
    public async Task RealScopedAdapters_ReadAllBills_WithForeignEvidenceExcluded(int count)
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        for (var i = 0; i < count; i++)
        {
            var stream = new BillStreamEntity { UserId = owner, ProviderName = $"Fixture {i}", IsActive = true };
            db.Add(stream);
            if (i != 0 && i != count - 1) continue;
            AddEvidence(db, owner, stream.Id);
        }
        var otherStream = new BillStreamEntity { UserId = foreign, ProviderName = "Foreign", IsActive = true };
        db.Add(otherStream);
        AddEvidence(db, foreign, otherStream.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new PlanningBillChangeWatchService(new BillStreamReadGateway(db),
            new StatementBillPlanningFactsGateway(db), new StatementBillPlanningChangeFactsGateway(db),
            new PlanningPaycheckAllocationStore(db, TimeProvider.System));
        var result = await service.GetAsync(owner);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, item => item.BillStreamId == otherStream.Id);
        Assert.All(result, item => Assert.Equal(600m, item.RemainingAmountToPlan));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static void AddEvidence(FullWorthDbContext db, Guid owner, Guid stream)
    {
        var previous = new BillStatementEntity { UserId = owner, BillStreamId = stream,
            PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30), TotalAmount = 500m, CurrencyCode = "USD" };
        var current = new BillStatementEntity { UserId = owner, BillStreamId = stream,
            PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31), DueDate = new(2026, 11, 15),
            TotalAmount = 600m, CurrencyCode = "USD" };
        db.AddRange(previous, current);
        db.Add(new BillChangeEntity { UserId = owner, BillStreamId = stream,
            PreviousStatementId = previous.Id, CurrentStatementId = current.Id,
            ChangeType = BillChangeType.TotalIncrease, Confidence = BillChangeConfidence.Confirmed,
            PreviousAmount = 500m, CurrentAmount = 600m, AmountDifference = 100m, AnnualizedImpact = 1200m,
            Description = "Synthetic confirmed change" });
    }

    private static FullWorthDbContext Db() => new(new DbContextOptionsBuilder<FullWorthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class ReadProbe(Guid owner)
    {
        public bool Busy { get; private set; }
        public TaskCompletionSource<bool>? FirstReadGate { get; init; }
        public string? FailStage { get; set; }
        public Action? Cancel { get; set; }
        public List<(string Kind, int Count)> Calls { get; } = [];
        public async Task Read(Guid user, IReadOnlyCollection<Guid> ids, string kind, CancellationToken token)
        {
            Assert.Equal(owner, user);
            Assert.False(Busy, "Scoped database adapters overlapped.");
            Assert.InRange(ids.Count, 1, 500);
            Calls.Add((kind, ids.Count));
            Busy = true;
            try
            {
                if (kind == "facts" && FirstReadGate is not null)
                    await FirstReadGate.Task.WaitAsync(token);
                else
                    await Task.Yield();
                token.ThrowIfCancellationRequested();
                if (FailStage == kind)
                {
                    Cancel?.Invoke();
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Synthetic scoped read failure.");
                }
            }
            finally { Busy = false; }
        }
    }
    private sealed class Facts(ReadProbe probe) : IBillPlanningFactsGateway
    {
        public async Task<IReadOnlyDictionary<Guid, BillPlanningFact>> GetLatestAsync(
            Guid user, IReadOnlyCollection<Guid> ids, CancellationToken token = default)
        {
            await probe.Read(user, ids, "facts", token);
            return ids.ToDictionary(id => id, id => new BillPlanningFact(id, id, 600m, "USD",
                new DateOnly(2026, 11, 15), new DateOnly(2026, 10, 31)));
        }
    }
    private sealed class Changes(ReadProbe probe) : IBillPlanningChangeFactsGateway
    {
        public async Task<IReadOnlyDictionary<Guid, BillPlanningChangeFact>> GetLatestConfirmedAsync(
            Guid user, IReadOnlyCollection<Guid> ids, CancellationToken token = default)
        {
            await probe.Read(user, ids, "changes", token);
            return ids.ToDictionary(id => id, id => new BillPlanningChangeFact(id, Guid.NewGuid(),
                Guid.NewGuid(), id, 500m, 600m, 100m, 1200m, "Synthetic confirmed change", new DateOnly(2026, 10, 31)));
        }
    }
    private sealed class Streams(Guid owner, IReadOnlyList<BillStreamReadRecord> bills) : IBillStreamReadGateway
    {
        public Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedActiveAsync(Guid user,
            CancellationToken token = default) => Task.FromResult(user == owner ? bills : (IReadOnlyList<BillStreamReadRecord>)[]);
        public Task<BillStreamReadRecord?> GetOwnedAsync(Guid user, Guid id, CancellationToken token = default) =>
            Task.FromResult(user == owner ? bills.SingleOrDefault(x => x.BillStreamId == id) : null);
    }
}

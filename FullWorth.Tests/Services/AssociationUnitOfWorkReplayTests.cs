using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class AssociationUnitOfWorkReplayTests
{
    public static IEnumerable<object[]> ReplayCases()
    {
        foreach (var tracked in new[] { false, true })
        foreach (var operation in new[] { "create-replay", "create-reassign", "create-remove",
                     "update-replay", "remove-recreate", "remove-reassign", "remove-replay",
                     "update-remove-recreate", "update-remove-restore" })
            yield return [operation, tracked];
    }

    [Theory]
    [MemberData(nameof(ReplayCases))]
    public async Task RepeatedStaging_ObservesPendingState_AndSavesOnce(string operation, bool pretracked)
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var owner = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var transaction = Guid.NewGuid();
        var first = new BillStreamEntity { UserId = owner, ProviderName = "First" };
        var second = new BillStreamEntity { UserId = owner, ProviderName = "Second" };
        var other = new BillStreamEntity { UserId = foreign, ProviderName = "Foreign" };
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var hadExisting = !operation.StartsWith("create", StringComparison.Ordinal);
        await using (var seed = new FullWorthDbContext(options))
        {
            seed.AddRange(first, second, other);
            if (hadExisting)
                seed.Add(new BillTransactionAssociationEntity { UserId = owner,
                    BankTransactionId = transaction, BillStreamId = first.Id, CreatedAtUtc = now, UpdatedAtUtc = now });
            seed.Add(new BillTransactionAssociationEntity { UserId = foreign,
                BankTransactionId = transaction, BillStreamId = other.Id, CreatedAtUtc = now, UpdatedAtUtc = now });
            await seed.SaveChangesAsync();
        }
        await using var db = new FullWorthDbContext(options);
        if (pretracked) await db.BillTransactionAssociations.ToListAsync();
        var gateway = new BillTransactionAssociationGateway(db);
        async Task Stage(Guid? expected, Guid? desired) => await gateway.StageAssignmentsAsync(owner,
            [new BillTransactionAssociationAssignment(transaction, expected, desired)], now.AddHours(1));
        Guid? final;
        switch (operation)
        {
            case "create-replay": await Stage(null, first.Id); await Stage(first.Id, first.Id); final = first.Id; break;
            case "create-reassign": await Stage(null, first.Id); await Stage(first.Id, second.Id); final = second.Id; break;
            case "create-remove": await Stage(null, first.Id); await Stage(first.Id, null); final = null; break;
            case "update-replay": await Stage(first.Id, second.Id); await Stage(second.Id, second.Id); final = second.Id; break;
            case "remove-recreate": await Stage(first.Id, null); await Stage(null, first.Id); final = first.Id; break;
            case "remove-reassign": await Stage(first.Id, null); await Stage(null, second.Id); final = second.Id; break;
            case "update-remove-recreate": await Stage(first.Id, second.Id); await Stage(second.Id, null);
                await Stage(null, second.Id); final = second.Id; break;
            case "update-remove-restore": await Stage(first.Id, second.Id); await Stage(second.Id, null);
                await Stage(null, first.Id); final = first.Id; break;
            default: await Stage(first.Id, null); await Stage(null, null); final = null; break;
        }
        // A stale expectation still fails against the staged state.
        if (final.HasValue)
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(null, second.Id));
        await using (var before = new FullWorthDbContext(options))
            Assert.Equal(hadExisting ? first.Id : (Guid?)null,
                (await before.BillTransactionAssociations.SingleOrDefaultAsync(x => x.UserId == owner))?.BillStreamId);
        if (hadExisting && final.HasValue)
            Assert.Equal(first.Id, db.ChangeTracker.Entries<BillTransactionAssociationEntity>()
                .Single(x => x.Entity.UserId == owner).OriginalValues
                .GetValue<Guid>(nameof(BillTransactionAssociationEntity.BillStreamId)));
        await db.SaveChangesAsync();
        await using var verify = new FullWorthDbContext(options);
        Assert.Equal(final, (await verify.BillTransactionAssociations.SingleOrDefaultAsync(x => x.UserId == owner))?.BillStreamId);
        var unchanged = await verify.BillTransactionAssociations.SingleAsync(x => x.UserId == foreign);
        Assert.Equal(other.Id, unchanged.BillStreamId);
        Assert.Equal(now, unchanged.UpdatedAtUtc);
        if (final.HasValue && hadExisting)
            Assert.Equal(now, (await verify.BillTransactionAssociations.SingleAsync(x => x.UserId == owner)).CreatedAtUtc);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task InvalidLaterTarget_StagesNoEarlierAssignment(string target)
    {
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = Guid.NewGuid();
        var valid = new BillStreamEntity { UserId = owner, ProviderName = "Valid" };
        var invalid = new BillStreamEntity { UserId = target == "foreign" ? Guid.NewGuid() : owner,
            ProviderName = "Unavailable" };
        db.AddRange(valid, invalid);
        await db.SaveChangesAsync();
        if (target == "deleted") db.Remove(invalid);
        var id = target == "missing" ? Guid.NewGuid() : target == "empty" ? Guid.Empty : invalid.Id;
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new BillTransactionAssociationGateway(db).StageAssignmentsAsync(owner,
                [new(Guid.NewGuid(), null, valid.Id), new(Guid.NewGuid(), null, id)], DateTimeOffset.UtcNow));
        Assert.Empty(db.ChangeTracker.Entries<BillTransactionAssociationEntity>());
        Assert.Equal(target == "deleted", db.Entry(invalid).State == EntityState.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledRestaging_PreservesPendingAssignment(bool removal)
    {
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = Guid.NewGuid();
        var stream = new BillStreamEntity { UserId = owner, ProviderName = "Fixture" };
        db.Add(stream);
        await db.SaveChangesAsync();
        var transaction = Guid.NewGuid();
        var gateway = new BillTransactionAssociationGateway(db);
        await gateway.StageAssignmentsAsync(owner, [new(transaction, null, stream.Id)], DateTimeOffset.UtcNow);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.StageAssignmentsAsync(owner, [new(transaction, stream.Id, removal ? null : stream.Id)],
                DateTimeOffset.UtcNow, stop.Token));
        await db.SaveChangesAsync();
        Assert.Equal(stream.Id, (await db.BillTransactionAssociations.SingleAsync()).BillStreamId);
    }
}

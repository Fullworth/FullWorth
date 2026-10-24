using FullWorth.API.Controllers;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed partial class BillConfirmationReliabilityTests
{
    public static IEnumerable<object[]> DuplicateConfirmationCases()
    {
        foreach (var count in new[] { 2, 3 })
        foreach (var activity in new[] { "active", "inactive", "mixed" })
        foreach (var source in new[] { BillStreamSource.Manual, BillStreamSource.AutomaticDiscovery })
        foreach (var casing in new[] { false, true })
            yield return [count, activity, source, casing];
    }

    [Theory]
    [MemberData(nameof(DuplicateConfirmationCases))]
    public async Task AmbiguousProvider_RequiresExactId_WithoutChoosingAnArbitraryObligation(
        int count, string activity, BillStreamSource source, bool casing)
    {
        var f = await Fixture.CreateAsync();
        await using (var seed = f.Open())
        {
            var first = await seed.BillStreams.SingleAsync(x => x.Id == f.Bill);
            first.Source = source;
            first.IsActive = activity != "inactive";
            first.UpdatedAtUtc = Now;
            for (var i = 1; i < count; i++)
                seed.Add(new BillStreamEntity { UserId = f.Owner,
                    ProviderName = casing ? "TARGET" : "Target", Source = source,
                    IsActive = activity == "active", UpdatedAtUtc = Now });
            // The same name under another owner never counts toward our ambiguity.
            (await seed.BillStreams.SingleAsync(x => x.Id == f.ForeignBill)).ProviderName = "Target";
            await seed.SaveChangesAsync();
        }
        await using var db = f.Open();
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Unrelated pending caller" };
        db.Add(caller);
        var before = Capture(db);
        using var host = new Host(db, f.Owner);
        Assert.IsType<ConflictObjectResult>(await Act(host, "existing", f.Bill));
        AssertRetained(db, before);
        Assert.Single(db.ChangeTracker.Entries());
        await using (var verify = f.Open())
        {
            var unchanged = await verify.BillStreams.AsNoTracking()
                .Where(x => x.UserId == f.Owner).ToListAsync();
            Assert.Equal(count, unchanged.Count);
            Assert.All(unchanged, x =>
            {
                Assert.Equal(source, x.Source);
                Assert.Equal(Now, x.UpdatedAtUtc);
                Assert.Equal(BillCategory.Unknown, x.Category);
            });
            Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        }
        Assert.IsType<NoContentResult>(await host.Controller.ConfirmBillStream(f.Bill));
        await db.SaveChangesAsync();
        await using var stored = f.Open();
        var chosen = await stored.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.Bill);
        Assert.Equal(BillStreamSource.Manual, chosen.Source);
        Assert.True(chosen.IsActive);
        Assert.Equal(BillCategory.Unknown, chosen.Category);
        Assert.All(await stored.BillStreams.AsNoTracking()
            .Where(x => x.UserId == f.Owner && x.Id != f.Bill && x.Id != caller.Id).ToListAsync(),
            x => { Assert.Equal(source, x.Source); Assert.Equal(Now, x.UpdatedAtUtc); });
        var foreign = await stored.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.ForeignBill);
        Assert.Equal("Target", foreign.ProviderName);
        Assert.Equal(BillStreamSource.Manual, foreign.Source);
        await f.AssertHistoryAsync(stored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignNameMatch_DoesNotBlockNewOwnedBill(bool active)
    {
        var f = await Fixture.CreateAsync();
        await using (var seed = f.Open())
        {
            seed.Remove(await seed.BillStreams.SingleAsync(x => x.Id == f.Bill));
            var foreign = await seed.BillStreams.SingleAsync(x => x.Id == f.ForeignBill);
            foreign.ProviderName = "Target";
            foreign.IsActive = active;
            await seed.SaveChangesAsync();
        }
        await using var db = f.Open();
        using var host = new Host(db, f.Owner);
        var result = Created(await Act(host, "new", f.Bill));
        Assert.NotEqual(f.ForeignBill, result.Id);
        Assert.Equal("Manual", result.Source);
        await using var verify = f.Open();
        Assert.Equal(1, await verify.BillStreams.CountAsync(x => x.UserId == f.Owner));
        Assert.Equal(active, (await verify.BillStreams.AsNoTracking()
            .SingleAsync(x => x.Id == f.ForeignBill)).IsActive);
        await f.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData("", "Internet")]
    [InlineData("   ", "Internet")]
    [InlineData("Target", "1")]
    [InlineData("Target", "NoSuchCategory")]
    [InlineData("oversized", "Internet")]
    public async Task InvalidManualCreate_IsReadOnly(string provider, string category)
    {
        var f = await Fixture.CreateAsync();
        await using var db = f.Open();
        using var host = new Host(db, f.Owner);
        if (provider == "oversized") provider = new string('x', 201);
        var before = Capture(db);
        var response = await host.Controller.CreateBillStream(new(provider, category));
        Assert.IsType<BadRequestObjectResult>(response.Result);
        AssertRetained(db, before);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("existing")]
    public async Task CanceledMutation_DoesNotTrackOrChangeBills(string operation)
    {
        var f = await Fixture.CreateAsync();
        await using var db = f.Open();
        using var host = new Host(db, f.Owner);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Act(host, operation, f.Bill, stop.Token));
        Assert.Empty(db.ChangeTracker.Entries());
    }
}

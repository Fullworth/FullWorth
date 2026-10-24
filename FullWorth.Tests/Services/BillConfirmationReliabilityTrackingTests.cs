using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed partial class BillConfirmationReliabilityTests
{
    public static IEnumerable<object[]> StaleConfirmationCases()
    {
        foreach (var operation in new[] { "confirm", "existing" })
        foreach (var field in new[] { "source", "active", "category" })
        foreach (var origin in new[] { "caller", "database" })
            yield return [operation, field, origin];
    }

    [Theory]
    [MemberData(nameof(StaleConfirmationCases))]
    public async Task StaleOrUnsavedConfirmationState_ReturnsConflict_WithoutOverwritingIntent(
        string operation, string field, string origin)
    {
        var f = await Fixture.CreateAsync();
        await using var db = f.Open();
        var target = await db.BillStreams.SingleAsync(x => x.Id == f.Bill);
        void Change(BillStreamEntity bill)
        {
            if (field == "source") bill.Source = BillStreamSource.Manual;
            if (field == "active") bill.IsActive = false;
            if (field == "category") bill.Category = BillCategory.Internet;
        }
        if (origin == "caller") Change(target);
        else
        {
            await using var other = f.Open();
            Change(await other.BillStreams.SingleAsync(x => x.Id == f.Bill));
            await other.SaveChangesAsync();
        }
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Do not save caller" };
        db.Add(caller);
        var before = Capture(db);
        using var host = new Host(db, f.Owner);
        Assert.IsType<ConflictObjectResult>(await Act(host, operation, f.Bill));
        AssertRetained(db, before);
        Assert.Equal(before.Count, db.ChangeTracker.Entries().Count());
        await using var verify = f.Open();
        Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        var durable = await verify.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.Bill);
        Assert.Equal(origin == "database" && field == "source" ? BillStreamSource.Manual
            : BillStreamSource.AutomaticDiscovery, durable.Source);
        Assert.Equal(!(origin == "database" && field == "active"), durable.IsActive);
        await f.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData("confirm", "added")]
    [InlineData("confirm", "deleted")]
    [InlineData("confirm", "provider")]
    [InlineData("existing", "added")]
    [InlineData("existing", "deleted")]
    [InlineData("existing", "provider")]
    public async Task PendingIdentityLifecycleOrProviderIntent_IsPreserved(string operation, string mode)
    {
        var f = await Fixture.CreateAsync();
        await using var db = f.Open();
        var target = await db.BillStreams.SingleAsync(x => x.Id == f.Bill);
        if (mode == "added") db.Entry(target).State = EntityState.Added;
        if (mode == "deleted") db.Remove(target);
        if (mode == "provider") target.ProviderName = "Caller intended name";
        var before = Capture(db);
        using var host = new Host(db, f.Owner);
        Assert.IsType<ConflictObjectResult>(await Act(host, operation, f.Bill));
        AssertRetained(db, before);
    }

    [Theory]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    [InlineData("existing", false)]
    [InlineData("existing", true)]
    public async Task AlreadyConfirmed_NoOpDoesNotSaveCallerEdits(string operation, bool timestampEdit)
    {
        var f = await Fixture.CreateAsync();
        await using (var seed = f.Open())
        {
            var target = await seed.BillStreams.SingleAsync(x => x.Id == f.Bill);
            target.Source = BillStreamSource.Manual;
            target.Category = BillCategory.Internet;
            target.UpdatedAtUtc = Now;
            await seed.SaveChangesAsync();
        }
        await using var db = f.Open();
        var targetRow = await db.BillStreams.SingleAsync(x => x.Id == f.Bill);
        if (timestampEdit) targetRow.UpdatedAtUtc = Now.AddMinutes(1);
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Pending no-op caller" };
        db.Add(caller);
        var before = Capture(db);
        using var host = new Host(db, f.Owner);
        var result = await Act(host, operation, f.Bill);
        if (operation == "confirm") Assert.IsType<NoContentResult>(result);
        else Assert.Equal(f.Bill, Created(result).Id);
        AssertRetained(db, before);
        await using var verify = f.Open();
        Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        Assert.Equal(Now, (await verify.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.Bill)).UpdatedAtUtc);
    }

    [Theory]
    [InlineData("confirm", false, false)]
    [InlineData("confirm", false, true)]
    [InlineData("confirm", true, false)]
    [InlineData("confirm", true, true)]
    [InlineData("existing", false, false)]
    [InlineData("existing", false, true)]
    [InlineData("existing", true, false)]
    [InlineData("existing", true, true)]
    public async Task OrdinaryConfirmationAndRepeat_PreserveIdentityAndSupportedCategoryPolicy(
        string operation, bool active, bool knownCategory)
    {
        var f = await Fixture.CreateAsync();
        await using (var seed = f.Open())
        {
            var bill = await seed.BillStreams.SingleAsync(x => x.Id == f.Bill);
            bill.IsActive = active;
            bill.Category = knownCategory ? BillCategory.Internet : BillCategory.Unknown;
            await seed.SaveChangesAsync();
        }
        await using var db = f.Open();
        using var host = new Host(db, f.Owner);
        for (var i = 0; i < 2; i++)
        {
            var result = await Act(host, operation, f.Bill);
            if (operation == "confirm") Assert.IsType<NoContentResult>(result);
            else Assert.Equal(f.Bill, Created(result).Id);
        }
        await using var verify = f.Open();
        var billRow = await verify.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.Bill);
        Assert.Equal(BillStreamSource.Manual, billRow.Source);
        Assert.True(billRow.IsActive);
        Assert.Equal(knownCategory || operation == "existing" ? BillCategory.Internet : BillCategory.Unknown,
            billRow.Category);
        Assert.Equal(1, await verify.BillStreams.CountAsync(x => x.UserId == f.Owner));
        await f.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task ConfirmUnavailableIdentity_ReturnsNotFound_WithoutTrackingForeignRows(string mode)
    {
        var f = await Fixture.CreateAsync();
        await using var db = f.Open();
        using var host = new Host(db, f.Owner);
        var id = mode == "foreign" ? f.ForeignBill : mode == "empty" ? Guid.Empty : Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await host.Controller.ConfirmBillStream(id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    [InlineData("existing", false)]
    [InlineData("existing", true)]
    public async Task MissingOrEmptyAuthenticatedSubject_FailsClosed(string operation, bool empty)
    {
        var f = await Fixture.CreateAsync();
        await using var db = f.Open();
        using var host = new Host(db, empty ? Guid.Empty : null);
        Assert.IsType<UnauthorizedResult>(await Act(host, operation, f.Bill));
        Assert.Empty(db.ChangeTracker.Entries());
    }
}

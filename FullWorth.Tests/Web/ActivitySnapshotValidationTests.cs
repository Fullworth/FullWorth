using System.Reflection;
using FullWorth.Web.Components.Pages.App;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class ActivitySnapshotValidationTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

    private static Activity.BillAlertViewModel Valid() => new()
    {
        Id = Guid.NewGuid(),
        AlertType = "BillIncrease", Severity = "Warning",
        Title = "Example recurring increase",
        Message = "A supported charge changed.",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static bool IsValid(Activity.BillAlertViewModel[]? alerts) =>
        (bool)typeof(Activity).GetMethod("IsValidActivitySnapshot", Flags)!
            .Invoke(null, [alerts])!;

    [Fact]
    public void SnapshotRejectsMissingFieldsAndIdentity()
    {
        Assert.True(IsValid([]));
        Assert.True(IsValid([Valid()]));
        Assert.False(IsValid(null));
        Assert.False(IsValid([new Activity.BillAlertViewModel()]));
        var valid = Valid();
        Assert.False(IsValid([valid, valid]));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("message")]
    [InlineData("type")]
    [InlineData("severity")]
    [InlineData("created")]
    [InlineData("updated")]
    public void SnapshotRejectsIncompleteAlert(string missing)
    {
        var record = Valid();
        switch (missing)
        {
            case "id": record.Id = Guid.Empty; break;
            case "title": record.Title = " "; break;
            case "message": record.Message = ""; break;
            case "type": record.AlertType = ""; break;
            case "severity": record.Severity = ""; break;
            case "created": record.CreatedAtUtc = default; break;
            case "updated": record.UpdatedAtUtc = default; break;
        }
        Assert.False(IsValid([record]));
    }

    [Fact]
    public async Task MalformedRefreshPreservesPreviouslyLoadedFeed()
    {
        var valid = Valid();
        var module = new Module { Results = [valid] };
        var page = new Activity();
        typeof(Activity).GetField("_bffModule", Flags)!.SetValue(page, module);
        typeof(Activity).GetProperty("L", Flags)!
            .SetValue(page, new Localizer());
        var load = typeof(Activity).GetMethod("LoadAsync", Flags)!;
        await (Task)load.Invoke(page, null)!;
        module.Results = [new Activity.BillAlertViewModel()];
        await (Task)load.Invoke(page, null)!;
        var alerts = (List<Activity.BillAlertViewModel>)typeof(Activity)
            .GetField("_alerts", Flags)!.GetValue(page)!;
        Assert.Same(valid, Assert.Single(alerts));
        Assert.NotNull(typeof(Activity).GetField("_refreshError", Flags)!.GetValue(page));
        Assert.Equal(2, module.Reads);
        Assert.Equal(0, module.Writes);
        await page.DisposeAsync();
    }

    private sealed class Localizer : IStringLocalizer<Activity>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class Module : IJSObjectReference
    {
        public Activity.BillAlertViewModel[] Results { get; set; } = [];
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            InvokeAsync<T>(identifier, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "getAlerts")
            {
                Reads++;
                return ValueTask.FromResult((T)(object)Results);
            }
            Writes++;
            throw new InvalidOperationException("Unexpected mutation.");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

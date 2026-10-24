using System.Reflection;
using FullWorth.Web.Components.Shared;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class StatementUploadStatusResumeTests
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class Localizer : IStringLocalizer<StatementUploadPanel>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] args] =>
            new(name, string.Format(name, args));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class Module : IJSObjectReference
    {
        public StatementUploadPanel.BillStatementUploadStatusViewModel? Latest { get; set; }
        public StatementUploadPanel.BillStatementUploadStatusViewModel? Current { get; set; }
        public List<string> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) =>
            InvokeAsync<T>(name, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            Calls.Add(name);
            object? value = name switch
            {
                "getLatestBillStatementUpload" => Latest,
                "getBillStatementUploadStatus" => Current,
                "clearStatementFileInput" => null,
                _ => throw new InvalidOperationException("Unexpected write to upload endpoint")
            };
            return ValueTask.FromResult((T)value!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static StatementUploadPanel Create(Guid streamId, Module module)
    {
        // This is a renderer-free unit fixture, not a component consumer.
        // The reflection-based parameter setup matches the injected private
        // dependencies below and avoids assigning a [Parameter] in C# markup.
        var component = new StatementUploadPanel();
        typeof(StatementUploadPanel).GetProperty(
            nameof(StatementUploadPanel.BillStreamId), Flags)!
            .SetValue(component, streamId);
        typeof(StatementUploadPanel).GetProperty("L", Flags)!.SetValue(component, new Localizer());
        typeof(StatementUploadPanel).GetField("_bffModule", Flags)!.SetValue(component, module);
        return component;
    }

    private static Guid? Pending(StatementUploadPanel component) =>
        (Guid?)typeof(StatementUploadPanel).GetField("_pendingUploadId", Flags)!
            .GetValue(component);

    private static async Task Run(StatementUploadPanel component, string method, params object?[] args) =>
        await (Task)typeof(StatementUploadPanel).GetMethod(method, Flags)!
            .Invoke(component, args)!;

    [Fact]
    public async Task PendingUploadCanResumeAfterNavigationWithoutAnotherPost()
    {
        var streamId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var module = new Module
        {
            Latest = new()
            {
                Id = uploadId, BillStreamId = streamId, Status = "Processing"
            },
            Current = new()
            {
                Id = uploadId, BillStreamId = streamId, Status = "Processed"
            }
        };
        var component = Create(streamId, module);
        await Run(component, "OnAfterRenderAsync", true);
        Assert.Equal(uploadId, Pending(component));

        await Run(component, "CheckPendingStatusAsync");
        Assert.Null(Pending(component));
        Assert.Contains("getLatestBillStatementUpload", module.Calls);
        Assert.Contains("getBillStatementUploadStatus", module.Calls);
        Assert.DoesNotContain("uploadBillStatement", module.Calls);
        await component.DisposeAsync();
    }

    [Fact]
    public async Task ReadOnlyStatusCheckKeepsPendingIdentityUntilTerminalResult()
    {
        var streamId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var module = new Module
        {
            Latest = new() { Id = uploadId, BillStreamId = streamId, Status = "Uploaded" },
            Current = new() { Id = uploadId, BillStreamId = streamId, Status = "Processing" }
        };
        var component = Create(streamId, module);
        await Run(component, "OnAfterRenderAsync", true);
        await Run(component, "CheckPendingStatusAsync");

        Assert.Equal(uploadId, Pending(component));
        Assert.All(module.Calls, name =>
            Assert.DoesNotContain("uploadBillStatement", name));
        await component.DisposeAsync();
    }

    [Fact]
    public async Task AcceptedUploadStatusFailureCannotTriggerAnotherPost()
    {
        var stream = Guid.NewGuid();
        var upload = Guid.NewGuid();
        var module = new Module
        {
            Latest = new()
            {
                Id = upload, BillStreamId = stream, Status = "Processing"
            }
        };
        var component = Create(stream, module);
        await Run(component, "OnAfterRenderAsync", true);
        typeof(StatementUploadPanel).GetField("_selectedFile", Flags)!
            .SetValue(component, new StatementUploadPanel.SelectedStatementFileViewModel
            {
                Name = "safe-test.pdf", Size = 100
            });

        var canUpload = typeof(StatementUploadPanel)
            .GetProperty("CanUpload", Flags)!;
        Assert.False((bool)canUpload.GetValue(component)!);
        typeof(StatementUploadPanel).GetMethod("SetUploadOrStatusError", Flags)!
            .Invoke(component, null);
        var message = (string?)typeof(StatementUploadPanel)
            .GetField("_message", Flags)!.GetValue(component);
        Assert.NotNull(message);
        Assert.Contains("Check statement status", message);
        Assert.DoesNotContain("uploadBillStatement", module.Calls);
        await component.DisposeAsync();
    }

    [Fact]
    public async Task ForeignOrMismatchedLatestUploadIsNotRestored()
    {
        var streamId = Guid.NewGuid();
        var module = new Module
        {
            Latest = new()
            {
                Id = Guid.NewGuid(), BillStreamId = Guid.NewGuid(), Status = "Processing"
            }
        };
        var component = Create(streamId, module);
        await Run(component, "OnAfterRenderAsync", true);
        Assert.Null(Pending(component));
        await component.DisposeAsync();
    }
}

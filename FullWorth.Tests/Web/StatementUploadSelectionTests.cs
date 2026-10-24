using System.Reflection;
using FullWorth.Web.Components.Shared;

namespace FullWorth.Tests.Web;

public sealed class StatementUploadSelectionTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("statement.pdf", true)]
    [InlineData("statement.PDF", true)]
    [InlineData("statement.jpg", true)]
    [InlineData("statement.jpeg", true)]
    [InlineData("statement.png", true)]
    [InlineData("statement.txt", false)]
    [InlineData("statement.pdf.exe", false)]
    [InlineData("statement", false)]
    public void UploadAvailability_MatchesAllowedExtensions(string name, bool expected)
    {
        var panel = CreatePanel(name, 128);
        Assert.Equal(expected, CanUpload(panel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(15L * 1024 * 1024 + 1)]
    public void UploadAvailability_RejectsInvalidLengths(long size)
    {
        Assert.False(CanUpload(CreatePanel("statement.pdf", size)));
    }

    [Fact]
    public void UploadAvailability_RejectsMissingBillId()
    {
        var panel = CreatePanel("statement.pdf", 128);
        typeof(StatementUploadPanel).GetProperty(nameof(StatementUploadPanel.BillStreamId))!
            .SetValue(panel, Guid.Empty);
        Assert.False(CanUpload(panel));
    }

    [Fact]
    public void UploadAvailability_RejectsDisposedPanel()
    {
        var panel = CreatePanel("statement.pdf", 128);
        typeof(StatementUploadPanel).GetField("_isDisposed", PrivateInstance)!
            .SetValue(panel, true);
        Assert.False(CanUpload(panel));
    }

    private static StatementUploadPanel CreatePanel(string name, long size)
    {
        var panel = new StatementUploadPanel();
        typeof(StatementUploadPanel).GetProperty(nameof(StatementUploadPanel.BillStreamId))!
            .SetValue(panel, Guid.NewGuid());
        typeof(StatementUploadPanel).GetField("_selectedFile", PrivateInstance)!
            .SetValue(panel, new StatementUploadPanel.SelectedStatementFileViewModel
            {
                Name = name,
                Size = size
            });
        return panel;
    }

    private static bool CanUpload(StatementUploadPanel panel) =>
        (bool)typeof(StatementUploadPanel).GetProperty(
            "CanUpload", PrivateInstance)!.GetValue(panel)!;
}

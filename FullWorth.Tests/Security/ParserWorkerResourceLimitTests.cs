using System.Text;
using Xunit;

public sealed class ParserWorkerResourceLimitTests
{
    [Theory]
    [InlineData("1073741824", true)]
    [InlineData("1", true)]
    [InlineData("max", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("not-a-number", false)]
    public void MemoryLimitParserRequiresFinitePositiveBytes(string value, bool expected)
    {
        Assert.Equal(expected, WorkerResourceLimits.HasFiniteMemoryLimit(value));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1073741824", true)]
    [InlineData("max", false)]
    [InlineData("-1", false)]
    [InlineData("not-a-number", false)]
    public void SwapLimitParserRequiresFiniteNonNegativeBytes(string value, bool expected)
    {
        Assert.Equal(expected, WorkerResourceLimits.HasFiniteSwapLimit(value));
    }

    [Theory]
    [InlineData("200000 100000", true)]
    [InlineData("1 1", true)]
    [InlineData("max 100000", false)]
    [InlineData("0 100000", false)]
    [InlineData("200000 0", false)]
    [InlineData("200000", false)]
    [InlineData("invalid 100000", false)]
    public void CpuLimitParserRequiresFinitePositiveQuotaAndPeriod(string value, bool expected)
    {
        Assert.Equal(expected, WorkerResourceLimits.HasFiniteCpuLimit(value));
    }

    [Theory]
    [InlineData("200000", "100000", "1073741824", "0", true)]
    [InlineData("1", "1", "1", "1", true)]
    [InlineData("max", "100000", "1073741824", "0", false)]
    [InlineData("200000", "0", "1073741824", "0", false)]
    [InlineData("200000", "100000", "max", "0", false)]
    [InlineData("200000", "100000", "1073741824", "max", false)]
    [InlineData("200000", "100000", "1073741824", "-1", false)]
    public void WorkerConfigurationRequiresExplicitFiniteCpuAndMemoryLimits(
        string cpuQuota,
        string cpuPeriod,
        string memoryMax,
        string swapMax,
        bool expected)
    {
        Assert.Equal(
            expected,
            WorkerResourceLimits.HasValidWorkerLimitConfiguration(
                cpuQuota,
                cpuPeriod,
                memoryMax,
                swapMax));
    }

    [Fact]
    public void WorkerCgroupIsCreatedInsideTheDelegatedParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "fullworth-parser-parent");

        var child = WorkerResourceLimits.BuildWorkerCgroupPath(parent, 1234, "test-instance");

        Assert.Equal(
            Path.Combine(parent, "fullworth-parser-worker-1234-test-instance"),
            child);
    }

    [Fact]
    public void DocumentCgroupIsCreatedInsideTheDelegatedParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "fullworth-parser-container");

        var child = DocumentProcessCgroup.BuildDocumentCgroupPath(
            parent,
            "ocr",
            4321,
            "test-instance");

        Assert.Equal(
            Path.Combine(parent, "fullworth-ocr-4321-test-instance"),
            child);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("../ocr")]
    [InlineData("")]
    public void DocumentCgroupRejectsUnsupportedWorkloadNames(string workload)
    {
        var parent = Path.Combine(Path.GetTempPath(), "fullworth-parser-container");

        Assert.ThrowsAny<ArgumentException>(
            () => DocumentProcessCgroup.BuildDocumentCgroupPath(
                parent,
                workload,
                4321,
                "test-instance"));
    }

    [Fact]
    public async Task ProtocolRejectsWhenResourceLimitsAreUnverified()
    {
        var response = await WorkerProtocol.BuildResponseAsync(
            Request(),
            1024,
            TimeSpan.FromSeconds(1),
            () => false);

        Assert.Equal("worker_limits_unverified", response.ErrorCode);
    }

    [Fact]
    public async Task ProtocolRemainsFailClosedAfterVerifiedResourceContext()
    {
        var response = await WorkerProtocol.BuildResponseAsync(
            Request(),
            1024,
            TimeSpan.FromSeconds(1),
            () => true);

        Assert.Equal("worker_not_ready", response.ErrorCode);
    }

    [Fact]
    public async Task ProtocolRejectsWhenResourceVerificationThrows()
    {
        var response = await WorkerProtocol.BuildResponseAsync(
            Request(),
            1024,
            TimeSpan.FromSeconds(1),
            () => throw new InvalidOperationException("probe failed"));

        Assert.Equal("worker_limits_unverified", response.ErrorCode);
    }

    private static MemoryStream Request()
    {
        return new MemoryStream(Encoding.UTF8.GetBytes("{\"protocolVersion\":1}"));
    }
}

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

    [Theory]
    [InlineData("100000", "100000", "402653184", "0", true)]
    [InlineData("max", "100000", "402653184", "0", false)]
    [InlineData("100000", "0", "402653184", "0", false)]
    [InlineData("100000", "100000", "max", "0", false)]
    [InlineData("100000", "100000", "402653184", "max", false)]
    public void OcrImageConfigurationRequiresFiniteLimits(
        string cpuQuota,
        string cpuPeriod,
        string memoryMax,
        string swapMax,
        bool expected)
    {
        Assert.Equal(
            expected,
            OcrImageProcessCgroup.HasValidLimitConfiguration(
                cpuQuota,
                cpuPeriod,
                memoryMax,
                swapMax));
    }

    [Theory]
    [InlineData("100000 100000", "100000 100000", "402653184", "402653184", "0", "0", true)]
    [InlineData("100001 100000", "100000 100000", "402653184", "402653184", "0", "0", false)]
    [InlineData("100000 100000", "100000 100000", "402653185", "402653184", "0", "0", false)]
    [InlineData("100000 100000", "100000 100000", "402653184", "402653184", "1", "0", false)]
    [InlineData("100000 99999", "100000 100000", "402653184", "402653184", "0", "0", false)]
    public void OcrImageLimitsCannotExceedDocumentLimits(
        string imageCpu,
        string documentCpu,
        string imageMemory,
        string documentMemory,
        string imageSwap,
        string documentSwap,
        bool expected)
    {
        Assert.Equal(
            expected,
            OcrImageProcessCgroup.LimitsFitInsideParent(
                imageCpu,
                documentCpu,
                imageMemory,
                documentMemory,
                imageSwap,
                documentSwap));
    }

    [Fact]
    public void OcrImageCgroupIsCreatedBesideDocumentInsideDelegatedParent()
    {
        var delegatedParent = Path.Combine(
            Path.GetTempPath(),
            "fullworth-parser-container");
        var document = Path.Combine(
            delegatedParent,
            "fullworth-ocr-4321-document-instance");

        var image = OcrImageProcessCgroup.BuildCgroupPath(
            delegatedParent,
            4321,
            "test-instance");

        Assert.Equal(
            Path.Combine(
                delegatedParent,
                "fullworth-ocr-image-4321-test-instance"),
            image);
        Assert.Equal(
            Path.GetDirectoryName(document),
            Path.GetDirectoryName(image));
        Assert.NotEqual(
            document,
            image);
    }

    [Theory]
    [InlineData("nested/leaf")]
    [InlineData(@"nested\\leaf")]
    public void OcrImageCgroupRejectsDirectorySeparatorsInInstanceId(string instanceId)
    {
        var parent = Path.Combine(Path.GetTempPath(), "fullworth-parser-container");


        Assert.ThrowsAny<ArgumentOutOfRangeException>(
            () => OcrImageProcessCgroup.BuildCgroupPath(
                parent,
                4321,
                instanceId));
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

    [Fact]
    public void OcrChildEnvironmentCopiesConfiguredContainmentVariables()
    {
        var configured = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FULLWORTH_PARSER_IMAGE_CPU_QUOTA_US"] = "100000",
            ["FULLWORTH_PARSER_IMAGE_CPU_PERIOD_US"] = "100000",
            ["FULLWORTH_PARSER_IMAGE_MEMORY_MAX_BYTES"] = "402653184",
            ["FULLWORTH_PARSER_IMAGE_SWAP_MAX_BYTES"] = "0",
            ["FULLWORTH_PARSER_DOCUMENT_CPU_QUOTA_US"] = "100000",
            ["FULLWORTH_PARSER_DOCUMENT_CPU_PERIOD_US"] = "100000",
            ["FULLWORTH_PARSER_DOCUMENT_MEMORY_MAX_BYTES"] = "402653184",
            ["FULLWORTH_PARSER_DOCUMENT_SWAP_MAX_BYTES"] = "0",
            ["FULLWORTH_PARSER_IMAGE_OBSERVATION_DELAY_MS"] = "250"
        };
        var childEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["UNRELATED"] = "preserved"
        };

        OcrImageProcessCgroup.CopyConfiguredEnvironment(
            childEnvironment,
            name => configured.TryGetValue(name, out var value) ? value : null);

        Assert.Equal("100000", childEnvironment["FULLWORTH_PARSER_IMAGE_CPU_QUOTA_US"]);
        Assert.Equal("402653184", childEnvironment["FULLWORTH_PARSER_IMAGE_MEMORY_MAX_BYTES"]);
        Assert.Equal("100000", childEnvironment["FULLWORTH_PARSER_DOCUMENT_CPU_QUOTA_US"]);
        Assert.Equal("0", childEnvironment["FULLWORTH_PARSER_DOCUMENT_SWAP_MAX_BYTES"]);
        Assert.Equal("250", childEnvironment["FULLWORTH_PARSER_IMAGE_OBSERVATION_DELAY_MS"]);
        Assert.Equal("preserved", childEnvironment["UNRELATED"]);
    }

    private static MemoryStream Request()
    {
        return new MemoryStream(Encoding.UTF8.GetBytes("{\"protocolVersion\":1}"));
    }
}

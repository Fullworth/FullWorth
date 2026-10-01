using System.Text;

public sealed class ParserWorkerProtocolTests
{
    [Fact]
    public async Task BuildResponseAsync_ReturnsTimeout_WhenInputNeverCompletes()
    {
        var response = await WorkerProtocol.BuildResponseAsync(
            new BlockingReadStream(),
            WorkerProtocol.MaxRequestBytes,
            TimeSpan.FromMilliseconds(50));

        Assert.Equal(1, response.ProtocolVersion);
        Assert.Equal("rejected", response.Outcome);
        Assert.Equal("request_timeout", response.ErrorCode);
    }

    [Fact]
    public async Task BuildResponseAsync_ReturnsRequestTooLarge_WhenInputExceedsBound()
    {
        var oversizedRequest = new byte[WorkerProtocol.MaxRequestBytes + 1];

        var response = await WorkerProtocol.BuildResponseAsync(
            new MemoryStream(oversizedRequest),
            WorkerProtocol.MaxRequestBytes,
            TimeSpan.FromSeconds(1));

        Assert.Equal("request_too_large", response.ErrorCode);
    }

    [Fact]
    public async Task BuildResponseAsync_ReturnsInvalidRequest_WhenJsonIsMalformed()
    {
        var response = await WorkerProtocol.BuildResponseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("{")),
            WorkerProtocol.MaxRequestBytes,
            TimeSpan.FromSeconds(1));

        Assert.Equal("invalid_request", response.ErrorCode);
    }

    [Fact]
    public async Task BuildResponseAsync_ReturnsUnsupportedProtocol_WhenVersionIsNotOne()
    {
        var response = await WorkerProtocol.BuildResponseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"protocolVersion\":2}")),
            WorkerProtocol.MaxRequestBytes,
            TimeSpan.FromSeconds(1));

        Assert.Equal("unsupported_protocol", response.ErrorCode);
    }

    [Fact]
    public async Task BuildResponseAsync_RemainsFailClosed_WhenProtocolIsValidButWorkerIsNotReady()
    {
        var response = await WorkerProtocol.BuildResponseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"protocolVersion\":1}")),
            WorkerProtocol.MaxRequestBytes,
            TimeSpan.FromSeconds(1));

        Assert.Equal(1, response.ProtocolVersion);
        Assert.Equal("rejected", response.Outcome);
        Assert.Equal("worker_not_ready", response.ErrorCode);
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

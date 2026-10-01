using System.Globalization;
using System.Security.Cryptography;
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

    [Fact]
    public void Authentication_AcceptsSignedRequestExactlyOnce()
    {
        const string token =
            "worker-authentication-test-token-more-than-32-characters";
        var now = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
        var authentication =
            new ParserWorkerAuthentication(
                token,
                isDevelopment: false,
                new FixedTimeProvider(now));
        var signed = CreateSignedRequest(token, now.ToUnixTimeSeconds());

        Assert.True(authentication.IsAuthorized(
            $"Bearer {token}",
            signed.Timestamp,
            signed.Nonce,
            signed.Signature,
            "POST",
            "/v1/pdf/extract"));
        Assert.False(authentication.IsAuthorized(
            $"Bearer {token}",
            signed.Timestamp,
            signed.Nonce,
            signed.Signature,
            "POST",
            "/v1/pdf/extract"));
    }

    [Fact]
    public void Authentication_RejectsInvalidCredentialSignatureAndTimestamp()
    {
        const string token =
            "worker-authentication-test-token-more-than-32-characters";
        var now = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
        var authentication =
            new ParserWorkerAuthentication(
                token,
                isDevelopment: false,
                new FixedTimeProvider(now));
        var signed = CreateSignedRequest(token, now.ToUnixTimeSeconds());

        Assert.False(authentication.IsAuthorized(
            "Bearer wrong-worker-authentication-token-more-than-32-characters",
            signed.Timestamp,
            signed.Nonce,
            signed.Signature,
            "POST",
            "/v1/pdf/extract"));
        Assert.False(authentication.IsAuthorized(
            $"Bearer {token}",
            signed.Timestamp,
            signed.Nonce,
            new string('0', 64),
            "POST",
            "/v1/pdf/extract"));

        var expired =
            CreateSignedRequest(
                token,
                now.ToUnixTimeSeconds() - 61,
                nonce: "11111111111111111111111111111111");
        Assert.False(authentication.IsAuthorized(
            $"Bearer {token}",
            expired.Timestamp,
            expired.Nonce,
            expired.Signature,
            "POST",
            "/v1/pdf/extract"));

        var future =
            CreateSignedRequest(
                token,
                now.ToUnixTimeSeconds() + 61,
                nonce: "22222222222222222222222222222222");
        Assert.False(authentication.IsAuthorized(
            $"Bearer {token}",
            future.Timestamp,
            future.Nonce,
            future.Signature,
            "POST",
            "/v1/pdf/extract"));
    }

    [Fact]
    public void Authentication_RejectsMissingProductionToken()
    {
        Assert.Throws<InvalidOperationException>(
            () => new ParserWorkerAuthentication(
                configuredToken: null,
                isDevelopment: false));
    }

    [Fact]
    public async Task DrainAsync_ConsumesLargeDiagnosticStreamWithoutMaterializingIt()
    {
        const int length = 32 * 1024 * 1024;
        var source = new GeneratedDiagnosticStream(length);

        await DiscardedProcessOutput.DrainAsync(source, CancellationToken.None);

        Assert.Equal(length, source.BytesRead);
    }

    private static SignedRequest CreateSignedRequest(
        string token,
        long timestamp,
        string nonce = "0123456789abcdef0123456789abcdef")
    {
        var timestampText =
            timestamp.ToString(CultureInfo.InvariantCulture);
        var canonical =
            $"POST\\n/v1/pdf/extract\\n{timestampText}\\n{nonce}";
        var signature =
            Convert.ToHexString(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(token),
                        Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        return new SignedRequest(timestampText, nonce, signature);
    }

    private sealed record SignedRequest(
        string Timestamp,
        string Nonce,
        string Signature);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class GeneratedDiagnosticStream(int length) : Stream
    {
        private int _remaining = length;

        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, _remaining);
            buffer.Span[..count].Fill((byte)'x');
            _remaining -= count;
            BytesRead += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
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

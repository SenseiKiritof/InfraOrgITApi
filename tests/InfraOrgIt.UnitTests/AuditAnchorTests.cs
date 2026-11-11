using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.Features.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.UnitTests;

public sealed class AuditAnchorTests
{
    [Fact]
    public async Task Publisher_signs_the_exact_json_body()
    {
        var options = AnchorOptions();
        var handler = new RecordingHandler(HttpStatusCode.NoContent);
        var publisher = new AuditAnchorPublisher(
            new HttpClient(handler), Options.Create(options));
        var payload = new AuditAnchorPayload(
            "api-1",
            42,
            "abc123",
            new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 12, 10, 5, 0, DateTimeKind.Utc));

        await publisher.PublishAsync(payload, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://siem.example.test/audit", request.Uri.ToString());
        Assert.Equal("application/json", request.ContentType);

        var expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(options.HmacKey), request.Body));
        Assert.Equal($"sha256={expected}", request.Signature);

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Equal("api-1", root.GetProperty("instanceId").GetString());
        Assert.Equal(42, root.GetProperty("lastId").GetInt32());
        Assert.Equal("abc123", root.GetProperty("chainHead").GetString());
        Assert.Equal(payload.OccurredAt,
            root.GetProperty("occurredAt").GetDateTime());
        Assert.Equal(payload.Checked, root.GetProperty("checked").GetDateTime());
    }

    [Fact]
    public async Task Disabled_anchor_does_not_read_or_publish()
    {
        var source = new FakeSource(Payload());
        var handler = new RecordingHandler(HttpStatusCode.NoContent);
        var runner = CreateRunner(
            source, handler, new AuditAnchorOptions { Enabled = false });

        Assert.False(await runner.RunAsync(CancellationToken.None));
        Assert.Equal(0, source.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Empty_chain_is_skipped()
    {
        var source = new FakeSource(null);
        var handler = new RecordingHandler(HttpStatusCode.NoContent);
        var runner = CreateRunner(source, handler, AnchorOptions());

        Assert.False(await runner.RunAsync(CancellationToken.None));
        Assert.Equal(1, source.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Webhook_failure_is_not_thrown_by_the_runner()
    {
        var source = new FakeSource(Payload());
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
        var runner = CreateRunner(source, handler, AnchorOptions());

        Assert.False(await runner.RunAsync(CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Publisher_rejects_a_non_https_webhook()
    {
        var options = AnchorOptions();
        options.WebhookUrl = "http://siem.example.test/audit";
        var publisher = new AuditAnchorPublisher(
            new HttpClient(new RecordingHandler(HttpStatusCode.NoContent)),
            Options.Create(options));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(Payload(), CancellationToken.None));
    }

    private static AuditAnchorOptions AnchorOptions() => new()
    {
        Enabled = true,
        IntervalMinutes = 5,
        WebhookUrl = "https://siem.example.test/audit",
        HmacKey = "anchor-test-key-0123456789",
        InstanceId = "api-1"
    };

    private static AuditAnchorPayload Payload() => new(
        "api-1",
        42,
        "abc123",
        new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 12, 10, 5, 0, DateTimeKind.Utc));

    private static AuditAnchorRunner CreateRunner(
        IAuditAnchorSource source,
        HttpMessageHandler handler,
        AuditAnchorOptions options) =>
        new(
            source,
            new AuditAnchorPublisher(
                new HttpClient(handler), Options.Create(options)),
            Options.Create(options),
            NullLogger<AuditAnchorRunner>.Instance);

    private sealed class FakeSource(AuditAnchorPayload? payload) : IAuditAnchorSource
    {
        public int Calls { get; private set; }

        public Task<AuditAnchorPayload?> ReadHeadAsync(
            string instanceId,
            DateTime checkedAt,
            CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(payload);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content?.Headers.ContentType?.MediaType ?? "",
                request.Headers.TryGetValues(
                    AuditAnchorPublisher.SignatureHeader, out var values)
                    ? values.Single()
                    : null,
                request.Content is null
                    ? []
                    : await request.Content.ReadAsByteArrayAsync(ct)));
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string ContentType,
        string? Signature,
        byte[] Body);
}

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Audit;

/// <summary>
/// POSTs an anchor sample as JSON, signed with HMAC-SHA256 over the exact bytes
/// sent. The webhook must be HTTPS; it should persist samples append-only so a
/// later rewrite of the local audit chain no longer matches the anchored heads.
/// </summary>
public sealed class AuditAnchorPublisher(
    HttpClient http,
    IOptions<AuditAnchorOptions> options)
{
    public const string SignatureHeader = "X-InfraOrgIT-Signature";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(AuditAnchorPayload payload, CancellationToken ct)
    {
        var anchor = options.Value;
        if (!Uri.TryCreate(anchor.WebhookUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "Audit:Anchor:WebhookUrl deve essere un URL HTTPS assoluto.");
        if (string.IsNullOrWhiteSpace(anchor.HmacKey))
            throw new InvalidOperationException("Audit:Anchor:HmacKey mancante.");

        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.Add(
            SignatureHeader, $"sha256={Sign(body, anchor.HmacKey)}");

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Ancoraggio audit rifiutato con HTTP {(int)response.StatusCode}.");
    }

    internal static string Sign(ReadOnlySpan<byte> body, string key) =>
        Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), body));
}

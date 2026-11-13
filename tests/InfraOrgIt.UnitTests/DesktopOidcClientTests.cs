using System.Net;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Features.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.UnitTests;

public sealed class DesktopOidcClientTests
{
    private static readonly OidcOptions OidcSettings = new()
    {
        Issuer = "https://public.test/realms/ds",
        InternalIssuer = "http://keycloak:8080/realms/ds",
        ClientId = "infraorgit",
        ClientSecret = "bff-secret",
        DesktopClientId = "infraorgit-mcp-desktop"
    };

    [Fact]
    public async Task Redeems_with_the_public_desktop_client_and_no_secret()
    {
        var handler = new StubHandler(HttpStatusCode.OK, TokenJson);
        var client = new DesktopOidcClient(
            new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(OidcSettings), NullLogger<DesktopOidcClient>.Instance);

        var result = await client.RedeemRefreshTokenAsync("device-refresh-token", CancellationToken.None);

        Assert.Equal("rotated-refresh-token", result.RefreshToken);
        Assert.Equal("header.payload.signature", result.IdToken);
        Assert.Equal("http://keycloak:8080/realms/ds/protocol/openid-connect/token", handler.RequestUri);
        var form = ParseForm(handler.Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("device-refresh-token", form["refresh_token"]);
        Assert.Equal("infraorgit-mcp-desktop", form["client_id"]);
        Assert.False(form.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task Rejected_refresh_token_maps_to_desktop_session_invalid()
    {
        var logger = new CapturingLogger<DesktopOidcClient>();
        var client = new DesktopOidcClient(
            new HttpClient(new StubHandler(HttpStatusCode.BadRequest, "{}")),
            Microsoft.Extensions.Options.Options.Create(OidcSettings), logger);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            client.RedeemRefreshTokenAsync("super-secret-refresh-token", CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
        Assert.Equal("desktop_session_invalid", exception.Code);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("super-secret-refresh-token"));
    }

    [Fact]
    public async Task Keycloak_server_error_maps_to_bad_gateway()
    {
        var client = new DesktopOidcClient(
            new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable, "{}")),
            Microsoft.Extensions.Options.Options.Create(OidcSettings), NullLogger<DesktopOidcClient>.Instance);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            client.RedeemRefreshTokenAsync("device-refresh-token", CancellationToken.None));

        Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
        Assert.Equal("oidc_error", exception.Code);
    }

    [Fact]
    public async Task Unreachable_keycloak_maps_to_bad_gateway_without_logging_the_token()
    {
        var logger = new CapturingLogger<DesktopOidcClient>();
        var client = new DesktopOidcClient(
            new HttpClient(new ThrowingHandler()),
            Microsoft.Extensions.Options.Options.Create(OidcSettings), logger);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            client.RedeemRefreshTokenAsync("super-secret-refresh-token", CancellationToken.None));

        Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
        Assert.Equal("oidc_error", exception.Code);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("super-secret-refresh-token"));
    }

    private const string TokenJson =
        """{"access_token":"unused","refresh_token":"rotated-refresh-token","id_token":"header.payload.signature","expires_in":300,"refresh_expires_in":1800}""";

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using MiniErp.Api;
using Xunit;

namespace MiniErp.Api.Tests;

public sealed class ApiAuthenticationTests
{
    private const string Issuer = "https://identity.example.test/realms/test";

    [Theory]
    [InlineData("valid", 200)]
    [InlineData("anonymous", 401)]
    [InlineData("malformed", 401)]
    [InlineData("issuer", 401)]
    [InlineData("audience", 401)]
    [InlineData("signature", 401)]
    [InlineData("expired", 401)]
    [InlineData("future", 401)]
    [InlineData("subject", 401)]
    [InlineData("algorithm", 401)]
    public async Task Real_bearer_pipeline_guards_business_handler(string kind, int expected)
    {
        using var rsa = RSA.Create(2048);
        using var wrongRsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-only" };
        await using var app = CreateApp(key);
        var writes = 0;
        app.MapPost("/api/business", () => { writes++; return Results.Ok(); });
        app.MapGet("/health/live", () => Results.Ok()).AllowAnonymous();
        await app.StartAsync();
        using var client = app.GetTestClient();
        if (kind != "anonymous")
        {
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(kind == "issuer" ? "https://other.example.test" : Issuer,
                kind == "audience" ? "other-api" : "minierp-api",
                kind == "subject" ? [] : [new Claim("sub", "synthetic-test-subject")],
                kind == "future" ? now.AddMinutes(10) : now.AddMinutes(-10),
                kind == "expired" ? now.AddMinutes(-5) : now.AddMinutes(20),
                new SigningCredentials(kind == "signature" ? new RsaSecurityKey(wrongRsa) : key,
                    kind == "algorithm" ? SecurityAlgorithms.RsaSha512 : SecurityAlgorithms.RsaSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                kind == "malformed" ? "invalid" : new JwtSecurityTokenHandler().WriteToken(token));
        }
        var response = await client.PostAsync("/api/business", null);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(expected == 200 ? 1 : 0, writes);
        if (expected == 401)
        {
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("authentication_required", body);
            Assert.DoesNotContain("IDX", body);
        }
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Auth_session_and_audit_require_token_and_validate_event()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-only" };
        await using var app = CreateApp(key);
        app.MapAuthenticationSession();
        await app.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/session")).StatusCode);
        var token = new JwtSecurityToken(Issuer, "minierp-api", [new Claim("sub", "synthetic")],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        var session = await client.GetAsync("/api/v1/auth/session");
        Assert.True(session.StatusCode == HttpStatusCode.OK,
            string.Join(";", session.Headers.TryGetValues("X-Test-AuthFailure", out var reasons) ? reasons : [session.StatusCode.ToString()]));
        Assert.True(session.Headers.CacheControl!.NoStore);
        foreach (var action in new[] { "login", "logout" })
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/events", new { @event = action })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/events", new { @event = "grant-role" })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://remote.example.test/realm")]
    public void Missing_or_insecure_remote_authority_fails_closed(string? authority)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Authority"] = authority });
        Assert.Throws<InvalidOperationException>(() => builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment));
    }

    private static WebApplication CreateApp(SecurityKey key)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Authority"] = Issuer });
        builder.Services.AddProblemDetails();
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            options.Configuration.SigningKeys.Add(key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}

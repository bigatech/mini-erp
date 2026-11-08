using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Cutting.Api;
using Cutting.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MiniErp.Api;
using Xunit;

namespace MiniErp.Api.Tests;

public sealed class CuttingAuthorizationTests
{
    private const string Issuer = "https://identity.example.test/realms/cutting";
    private const string Subject = "synthetic-cutting-user";

    [Theory]
    [InlineData("Development", "anonymous", 401, 401)]
    [InlineData("Production", "anonymous", 401, 401)]
    [InlineData("Development", "none", 403, 403)]
    [InlineData("Production", "none", 403, 403)]
    [InlineData("Development", "cutting.read", 200, 403)]
    [InlineData("Production", "cutting.read", 200, 403)]
    [InlineData("Development", "cutting.write", 403, 200)]
    [InlineData("Production", "cutting.write", 403, 200)]
    [InlineData("Development", "both", 200, 200)]
    [InlineData("Production", "both", 200, 200)]
    [InlineData("Development", "sales.orders.approve", 403, 403)]
    [InlineData("Production", "sales.orders.approve", 403, 403)]
    public async Task Existing_permissions_apply_in_every_environment(
        string environment, string permission, int readStatus, int writeStatus)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "cutting-test" };
        var store = new ObservedStore();
        await using var app = CreateApp(environment, key, store);
        await app.StartAsync();
        using var client = app.GetTestClient();
        Authenticate(client, key, permission);

        Assert.Equal(readStatus, (int)(await client.GetAsync("/api/v1/cutting/records")).StatusCode);
        Assert.Equal(readStatus == 200 ? 1 : 0, store.ReadCalls);
        Assert.Equal(0, store.WriteCalls);
        if (readStatus != 200)
        {
            // Options, details, event history and reports must not offer a second read path.
            foreach (var path in new[] { "options", $"records/{Guid.NewGuid()}",
                         $"records/{Guid.NewGuid()}/events", "report" })
                Assert.Equal(readStatus, (int)(await client.GetAsync("/api/v1/cutting/" + path)).StatusCode);
            Assert.Equal(0, store.ReadCalls);
        }

        using var request = SaveRequest();
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        Assert.Equal(writeStatus, (int)(await client.SendAsync(request)).StatusCode);
        Assert.Equal(writeStatus == 200 ? 1 : 0, store.WriteCalls);
        Assert.Equal(writeStatus == 200 ? Subject : null, store.Actor);
    }

    [Theory]
    [InlineData("Development", null, 428)]
    [InlineData("Production", null, 428)]
    [InlineData("Development", "invalid", 400)]
    [InlineData("Production", "invalid", 400)]
    public async Task Authorized_writes_still_require_valid_concurrency_preconditions(
        string environment, string? version, int expectedStatus)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "cutting-test" };
        var store = new ObservedStore();
        await using var app = CreateApp(environment, key, store);
        await app.StartAsync();
        using var client = app.GetTestClient();
        Authenticate(client, key, "cutting.write");
        using var request = SaveRequest();
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        Assert.Equal(expectedStatus, (int)(await client.SendAsync(request)).StatusCode);
        Assert.Equal(0, store.WriteCalls);
    }

    private static HttpRequestMessage SaveRequest() => new(HttpMethod.Put, $"/api/v1/cutting/records/{Guid.NewGuid()}")
    {
        Content = JsonContent.Create(new SaveCuttingDocument(Guid.NewGuid(), 1, "Draft", []))
    };

    private static void Authenticate(HttpClient client, SecurityKey key, string permission)
    {
        if (permission == "anonymous") return;
        var claims = new List<Claim> { new("sub", Subject) };
        if (permission == "both") claims.AddRange([new("permission", "cutting.read"), new("permission", "cutting.write")]);
        else if (permission != "none") claims.Add(new("permission", permission));
        var token = new JwtSecurityToken(Issuer, "minierp-api", claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static WebApplication CreateApp(string environment, SecurityKey key, ObservedStore store)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Authority"] = Issuer });
        builder.Services.AddProblemDetails();
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<ICuttingStore>(store);
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            options.Configuration.SigningKeys.Add(key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCuttingEndpoints();
        return app;
    }

    private sealed class ObservedStore : ICuttingStore
    {
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public string? Actor { get; private set; }
        public Task<object> OptionsAsync(CancellationToken ct) { ReadCalls++; return Task.FromResult<object>(new { }); }
        public Task<IReadOnlyList<CuttingDocument>> ListAsync(CancellationToken ct) { ReadCalls++; return Task.FromResult<IReadOnlyList<CuttingDocument>>([]); }
        public Task<CuttingDetail?> GetAsync(Guid id, CancellationToken ct) { ReadCalls++; return Task.FromResult<CuttingDetail?>(null); }
        public Task<IReadOnlyList<CuttingEvent>> EventsAsync(Guid id, CancellationToken ct) { ReadCalls++; return Task.FromResult<IReadOnlyList<CuttingEvent>>([]); }
        public Task<CuttingReport> ReportAsync(CuttingReportFilter filter, Guid? recordId, CancellationToken ct) { ReadCalls++; return Task.FromResult(EmptyReport()); }
        public Task<CuttingDetail> SaveAsync(Guid id, SaveCuttingDocument request, int? expectedVersion, bool create, string? actor, CancellationToken ct)
        {
            WriteCalls++; Actor = actor;
            var document = new CuttingDocument(id, request.OrderId, request.OrderVersion, "TEST", request.Status,
                1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, request.Groups);
            return Task.FromResult(new CuttingDetail(document, EmptyReport(), request.OrderVersion, false, "Draft", []));
        }
        private static CuttingReport EmptyReport() => CuttingCalculator.Calculate([], new Dictionary<Guid, IReadOnlyList<CuttingOrderLine>>(), new());
    }
}

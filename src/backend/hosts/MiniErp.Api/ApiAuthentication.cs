using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace MiniErp.Api;

public static class ApiAuthentication
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var authority = configuration["Auth:Authority"];
        if (string.IsNullOrWhiteSpace(authority) && environment.IsDevelopment())
            authority = "http://localhost:18090/realms/minierp-local";
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer) ||
            (issuer.Scheme != "https" && !(issuer.Scheme == "http" && issuer.IsLoopback)))
            throw new InvalidOperationException("Auth:Authority must be HTTPS (HTTP is permitted only for a loopback development issuer).");
        var audience = configuration["Auth:Audience"] ?? "minierp-api";
        if (string.IsNullOrWhiteSpace(audience)) throw new InvalidOperationException("Auth:Audience is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = issuer.AbsoluteUri.TrimEnd('/');
            options.Audience = audience;
            options.RequireHttpsMetadata = issuer.Scheme == "https";
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = options.Authority,
                ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, RequireExpirationTime = true,
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "sub"
            };
            if (configuration["Auth:BackchannelBaseUrl"] is { Length: > 0 } backchannel)
            {
                if (!issuer.IsLoopback || !Uri.TryCreate(backchannel, UriKind.Absolute, out var target) ||
                    target.Scheme is not ("http" or "https") || target.AbsolutePath != "/")
                    throw new InvalidOperationException("BackchannelBaseUrl is only for explicit loopback issuer container transport.");
                options.BackchannelHttpHandler = new LoopbackOidcTransport(issuer, target);
            }
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
                        context.Fail("A subject claim is required.");
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await Results.Problem(statusCode: 401, title: "Oturum açmanız gerekiyor.",
                        extensions: new Dictionary<string, object?> { ["code"] = "authentication_required" })
                        .ExecuteAsync(context.HttpContext);
                }
            };
        });
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().RequireClaim("sub").Build());
        return services;
    }

    public static void MapAuthenticationSession(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/auth/session", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { authenticated = true, subject = context.User.FindFirst("sub")!.Value });
        });
        endpoints.MapPost("/api/v1/auth/events", (SessionEvent request, HttpContext context, ILoggerFactory logs) =>
        {
            if (request.Event is not ("login" or "logout"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["login veya logout olmalı."] });
            // Logs never contain credentials, tokens, names, or raw subject identifiers.
            var actor = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                context.User.FindFirst("iss")!.Value + "|" + context.User.FindFirst("sub")!.Value)));
            logs.CreateLogger("MiniErp.Security.Audit").LogInformation(
                "Session {Action} ActorHash={ActorHash} CorrelationId={CorrelationId}", request.Event, actor, context.TraceIdentifier);
            return Results.NoContent();
        });
    }

    public sealed record SessionEvent(string? Event);

    // Changes only transport destination, never discovery issuer or token validation.
    private sealed class LoopbackOidcTransport(Uri issuer, Uri target) : HttpClientHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { } uri || uri.GetLeftPart(UriPartial.Authority) != issuer.GetLeftPart(UriPartial.Authority))
                throw new HttpRequestException("Unexpected OIDC backchannel origin.");
            request.RequestUri = new Uri(target, uri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

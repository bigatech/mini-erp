using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ProductLifecycle.Composition;
using DataIntakeAnalytics.Composition;
using MiniErp.Api;
using SharedKernel.Composition;
using SalesDrafts.Composition;
using Cutting.Composition;

var builder = WebApplication.CreateBuilder(args);
if(builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Media.json",optional:true,reloadOnChange:false);

// Emit structured JSON logs to stdout/stderr for local and container collection.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddHealthChecks();
builder.Services.AddProductLifecycle(builder.Configuration.GetConnectionString("Primary"));
builder.Services.AddDataIntakeAnalytics(builder.Configuration.GetConnectionString("Primary"));
builder.Services.AddSharedDefinitions();
builder.Services.AddSalesDrafts();
builder.Services.AddCutting();
if (builder.Environment.IsDevelopment())
{
    var webOrigin = builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:3000";
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("ETag")));
}

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ApiProblemDetails.Customize;
});

// Placeholder OpenTelemetry pipeline. Instrumentation and exporters are configured
// when their repository-level choices are implemented; telemetry remains best-effort.
builder.Services.AddOpenTelemetry()
    .WithMetrics(_ => { })
    .WithTracing(_ => { });

var app = builder.Build();

app.UseProductLifecycle();
app.UseSharedDefinitions();
app.UseSalesDrafts();
app.UseCutting();
app.UseDataIntakeAnalytics();

// Exception responses contain no exception details; the exception is kept in server logs.
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.UseCors();

app.MapGet("/", () => Results.Content("""
    <!doctype html>
    <html lang="tr">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>MiniErp API</title>
    </head>
    <body>
      <main>
        <h1>MiniErp API çalışıyor</h1>
        <p>Host başlangıç iskeleti hazır.</p>
        <ul>
          <li><a href="/health/live">Liveness kontrolü</a></li>
          <li><a href="/health/ready">Readiness kontrolü</a></li>
        </ul>
      </main>
    </body>
    </html>
    """, "text/html; charset=utf-8"));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

app.MapProductLifecycle();
app.MapSharedDefinitions();
app.MapSalesDrafts();
app.MapCutting();
app.MapDataIntakeAnalytics();

app.Map("/api/{**path}", (HttpContext context) =>
    Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        instance: context.Request.Path));

app.Run();

using DataIntakeAnalytics.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataIntakeAnalytics.Api;

public static class AnalyticsWorksheetEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsWorksheetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/data-intake/worksheet").WithTags("DataIntakeAnalytics");
        group.MapGet("", async (IAnalyticsWorksheetStore store, CancellationToken ct) =>
            await store.GetAsync(ct) is { } sheet ? Results.Ok(sheet) : Results.NotFound(new { title = "Belge henüz hazırlanmadı." }));
        group.MapPut("", async (SaveWorksheet request, IAnalyticsWorksheetStore store, CancellationToken ct) =>
        {
            if (request.Version < 1 || request.Inputs is null) return Results.BadRequest(new { title = "Sürüm ve giriş alanları gereklidir." });
            if (request.Inputs.Validate() is { } error) return Results.BadRequest(new { title = error });
            return await store.SaveAsync(request.Version, request.Inputs, ct) is { } sheet ? Results.Ok(sheet)
                : Results.Conflict(new { title = "Başka bir oturum veriyi değiştirdi. Güncel veriyi yükleyip tekrar deneyin." });
        });
        group.MapGet("/source.pdf", async (IAnalyticsWorksheetStore store, CancellationToken ct) =>
            await store.GetAssetAsync(false, ct) is { } file ? Results.File(file.Content, file.MediaType, file.FileName) : Results.NotFound());
        group.MapGet("/source.xlsx", async (IAnalyticsWorksheetStore store, CancellationToken ct) =>
            await store.GetAssetAsync(true, ct) is { } file ? Results.File(file.Content, file.MediaType, file.FileName) : Results.NotFound());
        return endpoints;
    }
}

using DataIntakeAnalytics.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataIntakeAnalytics.Api;

public static class DataIntakeEndpoints
{
    public static IEndpointRouteBuilder MapDataIntakeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/data-intake/imports").WithTags("DataIntakeAnalytics");
        endpoints.MapGet("/api/v1/data-intake/dashboard/validation", async (GetDashboardValidationHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct))).WithTags("DataIntakeAnalytics");
        group.MapPost("", async (IFormFile file, ImportFileHandler handler, CancellationToken ct) =>
        {
            if (file.Length == 0) return Results.BadRequest(new { title = "Dosya boş." });
            if (file.Length > 20 * 1024 * 1024) return Results.BadRequest(new { title = "Dosya boyutu 20 MB sınırını aşıyor." });
            try { await using var stream = file.OpenReadStream(); var result = await handler.HandleAsync(file.FileName, stream, ct); return Results.Created($"/api/v1/data-intake/imports/{result.Id}", result); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { title = ex.Message }); }
        }).DisableAntiforgery();
        group.MapGet("", async (ListStagedFilesHandler handler, CancellationToken ct) => Results.Ok(new { data = await handler.HandleAsync(ct) }));
        endpoints.MapGet("/api/v1/data-intake/dashboard", async (GetDashboardHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct))).WithTags("DataIntakeAnalytics");
        endpoints.MapGet("/api/v1/data-intake/dashboards/{process}", async (string process, DateOnly? from, DateOnly? to, string? material, GetProcessDashboardHandler handler, CancellationToken ct) =>
        {
            if (!ProcessAnalytics.Processes.Contains(process)) return Results.NotFound();
            if (from.HasValue && to.HasValue && from > to) return Results.BadRequest(new { title = "Başlangıç tarihi bitişten sonra olamaz." });
            return Results.Ok(await handler.HandleAsync(process, from, to, material, ct));
        }).WithTags("DataIntakeAnalytics");
        group.MapGet("/{id:guid}", async (Guid id, GetStagedFileHandler handler, CancellationToken ct) =>
        { var file = await handler.HandleAsync(id, ct); return file is null ? Results.NotFound() : Results.Ok(file); });
        return endpoints;
    }
}

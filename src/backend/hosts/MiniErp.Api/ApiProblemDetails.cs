using Microsoft.AspNetCore.Http;

namespace MiniErp.Api;

public static class ApiProblemDetails
{
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;
        problem.Status = status;
        problem.Type ??= $"https://httpstatuses.com/{status}";
        problem.Title ??= status switch
        {
            StatusCodes.Status404NotFound => "Kaynak bulunamadı",
            StatusCodes.Status500InternalServerError => "Beklenmeyen sunucu hatası",
            _ => "İstek işlenemedi"
        };

        // Dictionary indexer getters throw for absent keys. Defaults must handle
        // missing entries as well as explicitly null extension values.
        if (!problem.Extensions.TryGetValue("code", out var code) || code is null)
            problem.Extensions["code"] = status switch
            {
                StatusCodes.Status404NotFound => "resource_not_found",
                StatusCodes.Status500InternalServerError => "internal_error",
                _ => "request_failed"
            };
        if (!problem.Extensions.TryGetValue("correlationId", out var correlationId) || correlationId is null)
            problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProductLifecycle.Application;
using ProductLifecycle.Domain;
namespace ProductLifecycle.Api;

public static class RevisionLifecycleEndpoints
{
    public sealed record CloneRequest(string RevisionNumber);
    public static IEndpointRouteBuilder MapRevisionLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/plm/revisions").WithTags("ProductLifecycle");
        foreach(var action in new[]{"submit","approve","reject"})
        {
            var transition=action;
            group.MapPost($"/{{id:guid}}/{transition}",async(Guid id,RevisionLifecycleHandler handler,CancellationToken ct)=>await Run(async()=>Results.Ok(await handler.Transition(id,transition,ct))));
        }
        group.MapPost("/{id:guid}/clone",async(Guid id,CloneRequest request,RevisionLifecycleHandler handler,CancellationToken ct)=>await Run(async()=>Results.Created("/api/v1/plm/revisions",await handler.Clone(id,request.RevisionNumber,ct))));
        return endpoints;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"revision"]=[ex.Message]});}
        catch(PlmReferenceNotFoundException){return Results.NotFound(new{title="Model revizyonu bulunamadı.",code="revision_not_found"});}
        catch(RevisionStateConflictException){return Results.Conflict(new{title="Revizyon durumu bu işleme uygun değil. Klonlama yalnız onaylı revizyondan yapılır.",code="revision_state_conflict"});}
        catch(PlmDuplicateException){return Results.Conflict(new{title="Bu modelde revizyon numarası zaten mevcut.",code="duplicate_revision_number"});}
    }
}

using ProductLifecycle.Domain;
namespace ProductLifecycle.Application;
public sealed class BomHasChildrenException:Exception;
public sealed record UpdateBomLine(decimal Quantity,decimal WasteRate,string? Label=null,string? MaterialGroupCode=null,IReadOnlyDictionary<string,string>? Attributes=null);
public sealed class UpdateBomLineHandler(IPlmDraftRepository repository)
{
    public Task<BomLineDto> Handle(Guid revisionId,Guid lineId,UpdateBomLine request,CancellationToken ct)
    {
        PlmDraftRules.Bom(request.Quantity,request.WasteRate);
        return repository.UpdateBomAsync(revisionId,lineId,request.Quantity,request.WasteRate,BomLineEditRules.Label(request.Label),request.MaterialGroupCode,request.Attributes,ct);
    }
}

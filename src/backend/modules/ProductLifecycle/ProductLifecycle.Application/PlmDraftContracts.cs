using ProductLifecycle.Domain;
namespace ProductLifecycle.Application;
public sealed record ModelDto(Guid Id,string Code,string Name,string Status);
public sealed record RevisionDto(Guid Id,Guid ModelId,string RevisionNumber,string Status);
public sealed record BomLineDto(Guid Id,Guid RevisionId,Guid ComponentProductId,string ComponentCode,string ComponentName,string Unit,decimal Quantity,decimal WasteRate,Guid? ParentLineId=null,Guid? VariantOptionId=null,Guid? SizeOptionId=null,Guid? SetPartOptionId=null,string? Label=null,string? MaterialGroupCode=null,IReadOnlyDictionary<string,string>? Attributes=null,decimal? CalculatedNetQuantity=null,decimal? CalculatedQuantity=null);
public sealed class PlmReferenceNotFoundException:Exception {}
public sealed class PlmDuplicateException:Exception {}
public interface IPlmDraftRepository
{
    Task<IReadOnlyList<ModelDto>> ModelsAsync(CancellationToken ct);
    Task<ModelDto> CreateModelAsync(string code,string name,CancellationToken ct);
    Task<IReadOnlyList<RevisionDto>> RevisionsAsync(Guid modelId,CancellationToken ct);
    Task<RevisionDto> CreateRevisionAsync(Guid modelId,string number,CancellationToken ct);
    Task<IReadOnlyList<BomLineDto>> BomAsync(Guid revisionId,CancellationToken ct);
    Task<BomLineDto> AddBomAsync(Guid revisionId,Guid productId,decimal quantity,decimal waste,CancellationToken ct,Guid? parentLineId=null,Guid? variantOptionId=null,Guid? sizeOptionId=null,Guid? setPartOptionId=null,string? label=null,string? materialGroupCode=null,IReadOnlyDictionary<string,string>? attributes=null);
    Task<BomLineDto> UpdateBomAsync(Guid revisionId,Guid lineId,decimal quantity,decimal waste,string? label,string? materialGroupCode,IReadOnlyDictionary<string,string>? attributes,CancellationToken ct)=>throw new NotSupportedException();
    Task DeleteBomAsync(Guid revisionId,Guid lineId,CancellationToken ct)=>throw new NotSupportedException();
    Task<IReadOnlyList<ModelOptionDto>> OptionsAsync(Guid modelId,CancellationToken ct);
    Task<ModelOptionDto> CreateOptionAsync(Guid modelId,string kind,string code,string name,decimal quantity,CancellationToken ct);
}
public sealed class PlmDraftHandler(IPlmDraftRepository repository)
{
    public Task<ModelDto> CreateModel(string code,string name,CancellationToken ct)=>repository.CreateModelAsync(PlmDraftRules.Required(code,"code"),PlmDraftRules.Required(name,"name"),ct);
    public Task<RevisionDto> CreateRevision(Guid id,string number,CancellationToken ct)=>repository.CreateRevisionAsync(id,PlmDraftRules.Required(number,"revisionNumber"),ct);
    public Task<BomLineDto> AddBom(Guid revisionId,Guid productId,decimal quantity,decimal waste,CancellationToken ct,Guid? parentLineId=null,Guid? variantOptionId=null,Guid? sizeOptionId=null,Guid? setPartOptionId=null,string? label=null,string? materialGroupCode=null,IReadOnlyDictionary<string,string>? attributes=null)
    {
        PlmDraftRules.Bom(quantity,waste);
        if(productId==Guid.Empty) throw new ArgumentException("Bileşen ürün seçin.","componentProductId");
        return repository.AddBomAsync(revisionId,productId,quantity,waste,ct,parentLineId,variantOptionId,sizeOptionId,setPartOptionId,BomLineEditRules.Label(label),materialGroupCode,attributes);
    }
}

public sealed record ModelOptionDto(Guid Id,Guid ModelId,string Kind,string Code,string Name,decimal Quantity);
public interface IPlmConfigurationReader
{
    Task<bool> ExistsSelectionAsync(Guid revisionId,Guid variantOptionId,Guid sizeOptionId,CancellationToken ct);
    Task<bool> ExistsSetPartAsync(Guid revisionId,Guid setPartOptionId,CancellationToken ct)=>Task.FromResult(false);
    Task<bool> HasSetPartsAsync(Guid revisionId,CancellationToken ct)=>Task.FromResult(false);
}

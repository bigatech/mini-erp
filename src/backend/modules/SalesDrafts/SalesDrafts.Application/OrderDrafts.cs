using ProductLifecycle.Application;
namespace SalesDrafts.Application;

public sealed record OrderDraftCell(Guid VariantOptionId, Guid SizeOptionId, decimal Quantity, Guid? SetPartOptionId = null);
public sealed record CreateOrderDraft(string? Number, Guid RevisionId, IReadOnlyList<OrderDraftCell>? Lines);
public sealed record OrderDraftDto(Guid Id, string Number, Guid RevisionId, string Status, IReadOnlyList<OrderDraftCell> Lines, DateTimeOffset CreatedAt, int Version=1, OrderChangeApproval? PendingApproval=null, bool HasMaterialRequests=false);
public sealed class DuplicateOrderDraftException : Exception { }
public sealed class InvalidOrderDraftSelectionException : Exception { }
public interface IOrderDraftRepository
{
    Task<OrderDraftDto?> GetAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
    Task<OrderDraftDto> ReplaceAsync(Guid id,int expectedVersion,IReadOnlyList<OrderDraftCell> lines,CancellationToken ct)=>throw new NotSupportedException();
    Task<OrderDraftDto> CancelAsync(Guid id,int expectedVersion,CancellationToken ct)=>throw new NotSupportedException();
    Task<IReadOnlyList<OrderDraftEvent>> EventsAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
    Task<IReadOnlyList<OrderDraftDto>> ListAsync(CancellationToken ct);
    Task<OrderDraftDto> CreateAsync(string number, Guid revisionId, IReadOnlyList<OrderDraftCell> lines, CancellationToken ct);
}
public static class OrderDraftRules
{
    public static string Validate(CreateOrderDraft request)
    {
        // The caller cannot allocate document identities. Persistence uses a database sequence.
        if(request.RevisionId==Guid.Empty) throw new ArgumentException("Model revizyonu seçin.","revisionId");
        if(request.Lines is not {Count:>0}) throw new ArgumentException("En az bir pozitif matris hücresi girin.","lines");
        if(request.Lines.Count>10000) throw new ArgumentException("Bir taslak en fazla 10000 hücre içerebilir.","lines");
        var seen=new HashSet<(Guid,Guid,Guid?)>();
        foreach(var cell in request.Lines)
        {
            if(cell is null||cell.Quantity<=0||cell.VariantOptionId==Guid.Empty||cell.SizeOptionId==Guid.Empty||cell.SetPartOptionId==Guid.Empty) throw new ArgumentException("Hücre miktarı pozitif olmalı, varyant ve beden seçilmelidir.","lines");
            if(!seen.Add((cell.VariantOptionId,cell.SizeOptionId,cell.SetPartOptionId))) throw new ArgumentException("Aynı varyant/beden/parça hücresi tekrarlanamaz.","lines");
        }
        return string.Empty;
    }
}
public sealed class CreateOrderDraftHandler(IOrderDraftRepository repository, IPlmConfigurationReader configuration)
{
    public async Task<OrderDraftDto> Handle(CreateOrderDraft request,CancellationToken ct)
    {
        var number=OrderDraftRules.Validate(request);
        var lines=request.Lines!.ToArray();
        if(lines.Any(line=>line.SetPartOptionId is null) && await configuration.HasSetPartsAsync(request.RevisionId,ct))
            throw new ArgumentException("Setli modelde her matris hücresi için parça seçilmelidir.","lines");
        foreach(var line in lines)
        {
            if(!await configuration.ExistsSelectionAsync(request.RevisionId,line.VariantOptionId,line.SizeOptionId,ct)) throw new InvalidOrderDraftSelectionException();
            if(line.SetPartOptionId is {} part && !await configuration.ExistsSetPartAsync(request.RevisionId,part,ct)) throw new InvalidOrderDraftSelectionException();
        }
        return await repository.CreateAsync(number,request.RevisionId,lines,ct);
    }
}

using ProductLifecycle.Domain;
namespace ProductLifecycle.Application;
public sealed record BomRequirementsRequest(Guid RootLineId,decimal NetQuantity,Guid? VariantOptionId=null,Guid? SizeOptionId=null,Guid? SetPartOptionId=null);
public sealed record BomRequirementRow(Guid LineId,Guid? ParentLineId,int Level,string ComponentCode,string ComponentName,string Unit,decimal RecipeQuantity,decimal WasteRate,decimal? NetQuantity,decimal? GrossQuantity,decimal? PerTopUnit,bool IsLeaf);
public sealed record BomRequirementsResult(Guid RootLineId,decimal NetQuantity,string Method,bool Complete,IReadOnlyList<BomRequirementRow> Rows,IReadOnlyList<string> Warnings);
public interface IRevisionBomOptionsReader { Task<IReadOnlyList<ModelOptionDto>> ReadAsync(Guid revisionId,CancellationToken ct); }
public sealed class BomRequirementsHandler(IPlmDraftRepository repository,IRevisionBomOptionsReader options)
{
    public async Task<BomRequirementsResult> Handle(Guid revisionId,BomRequirementsRequest request,CancellationToken ct)
    {
        var lines=await repository.BomAsync(revisionId,ct);var catalogue=await options.ReadAsync(revisionId,ct);
        foreach(var axis in new[]{(request.VariantOptionId,"variant"),(request.SizeOptionId,"size"),(request.SetPartOptionId,"set-part")})
            if(axis.Item1.HasValue&&!catalogue.Any(o=>o.Id==axis.Item1.Value&&o.Kind==axis.Item2))throw new ArgumentException("Seçenek bu revizyonun modeline veya ilgili alana ait değil.","scope");
        return BomRequirementsCalculator.Calculate(lines,request);
    }
}
public static class BomRequirementsCalculator
{
    public static BomRequirementsResult Calculate(IReadOnlyList<BomLineDto> lines,BomRequirementsRequest request)
    {
        if(request.NetQuantity<=0)throw new ArgumentException("Üst malzeme net miktarı pozitif olmalıdır.","netQuantity");
        if(lines.Select(l=>l.Id).Distinct().Count()!=lines.Count)throw new ArgumentException("BOM kalem kimlikleri tekil olmalıdır.","bom");
        var byId=lines.ToDictionary(l=>l.Id);if(!byId.TryGetValue(request.RootLineId,out var root)||root.ParentLineId.HasValue)throw new ArgumentException("Aynı revizyonda kök bir üst malzeme seçin.","rootLineId");
        if(lines.Any(l=>l.RevisionId!=root.RevisionId))throw new ArgumentException("BOM farklı revizyonlar içeriyor.","bom");
        if(lines.Any(l=>l.ParentLineId.HasValue&&!byId.ContainsKey(l.ParentLineId.Value)))throw new ArgumentException("BOM üst bağlantısı eksik.","bom");
        var children=lines.Where(l=>l.ParentLineId.HasValue).GroupBy(l=>l.ParentLineId!.Value).ToDictionary(g=>g.Key,g=>g.ToList());
        var valid=new Queue<Guid>(lines.Where(l=>!l.ParentLineId.HasValue).Select(l=>l.Id));int visited=0;
        while(valid.TryDequeue(out var id)){visited++;if(children.TryGetValue(id,out var items))foreach(var item in items)valid.Enqueue(item.Id);}
        if(visited!=lines.Count)throw new ArgumentException("BOM döngüsü bulundu.","bom");
        var branch=new List<BomLineDto>();var pending=new Queue<BomLineDto>();pending.Enqueue(root);
        while(pending.TryDequeue(out var item)){branch.Add(item);if(children.TryGetValue(item.Id,out var items))foreach(var child in items)pending.Enqueue(child);}
        Guid? Scope(Guid? specified,Guid? fixedValue,Func<BomLineDto,Guid?> field,string name)
        {
            if(fixedValue.HasValue&&specified.HasValue&&fixedValue!=specified)throw new ArgumentException("Seçim üst malzemenin kapsamıyla çelişiyor.",name);
            var value=specified??fixedValue;
            if(!value.HasValue&&branch.Any(l=>field(l).HasValue))throw new ArgumentException("Bu dalı hesaplamak için ilgili varyant/beden/set parçasını seçin.",name);
            return value;
        }
        var variant=Scope(request.VariantOptionId,root.VariantOptionId,l=>l.VariantOptionId,"variantOptionId");
        var size=Scope(request.SizeOptionId,root.SizeOptionId,l=>l.SizeOptionId,"sizeOptionId");
        var part=Scope(request.SetPartOptionId,root.SetPartOptionId,l=>l.SetPartOptionId,"setPartOptionId");
        bool Matches(BomLineDto l)=>(!l.VariantOptionId.HasValue||l.VariantOptionId==variant)&&(!l.SizeOptionId.HasValue||l.SizeOptionId==size)&&(!l.SetPartOptionId.HasValue||l.SetPartOptionId==part);
        var rows=new List<BomRequirementRow>();var warnings=new List<string>();var queue=new Queue<(BomLineDto Line,int Level,decimal? ParentGross)>();queue.Enqueue((root,0,null));
        while(queue.TryDequeue(out var entry))
        {
            var line=entry.Line;if(!Matches(line))continue;
            decimal? net=null,gross=null,perUnit=null;
            try
            {
                if(line.Quantity<=0||line.WasteRate<0||line.WasteRate>1)throw new ArgumentException("Geçersiz reçete miktarı veya fire oranı.","bom");
                net=entry.Level==0?request.NetQuantity:entry.ParentGross.HasValue?checked(entry.ParentGross.Value*line.Quantity):null;
                if(net==0)throw new OverflowException(); // Positive operands must never silently become zero through decimal underflow.
                if(line.WasteRate==1)warnings.Add($"{line.ComponentCode}: %100 firede brüt ihtiyaç hesaplanamaz.");
                else if(net.HasValue){gross=BomYieldRules.Gross(net.Value,line.WasteRate);perUnit=checked(gross.Value/request.NetQuantity);if(perUnit==0)throw new OverflowException();}
            }
            catch(OverflowException){net=null;gross=null;perUnit=null;warnings.Add($"{line.ComponentCode}: miktar sayısal hesap sınırını aştı.");}
            var eligible=children.TryGetValue(line.Id,out var descendants)?descendants.Where(Matches).ToList():[];
            rows.Add(new(line.Id,line.ParentLineId,entry.Level,line.ComponentCode,line.ComponentName,line.Unit,line.Quantity,line.WasteRate,net,gross,perUnit,eligible.Count==0));
            foreach(var child in eligible)queue.Enqueue((child,entry.Level+1,gross));
        }
        foreach(var repeated in rows.GroupBy(r=>(r.ParentLineId,byId[r.LineId].ComponentProductId)).Where(g=>g.Count()>1))warnings.Add($"{repeated.First().ComponentCode}: aynı üst altında birden fazla reçete satırı vardır; otomatik birleştirme veya özel satırla değiştirme yapılmadı.");
        return new(root.Id,request.NetQuantity,"yield",rows.All(r=>r.GrossQuantity.HasValue),rows,warnings);
    }
}

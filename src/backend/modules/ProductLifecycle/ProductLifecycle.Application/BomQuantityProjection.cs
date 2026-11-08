namespace ProductLifecycle.Application;

/// <summary>Read-only row quantities. Stored Quantity remains the per-parent recipe ratio.</summary>
public static class BomQuantityProjection
{
    public static IReadOnlyList<BomLineDto> Apply(IReadOnlyList<BomLineDto> lines)
    {
        if(lines.Select(l=>l.Id).Distinct().Count()!=lines.Count)
            return lines.Select(l=>l with{CalculatedNetQuantity=null,CalculatedQuantity=null}).ToArray();
        var children=lines.Where(l=>l.ParentLineId.HasValue).GroupBy(l=>l.ParentLineId!.Value).ToDictionary(g=>g.Key,g=>g.ToArray());
        var projected=new Dictionary<Guid,BomLineDto>();
        var queue=new Queue<BomLineDto>(lines.Where(l=>l.ParentLineId is null));
        while(queue.TryDequeue(out var line))
        {
            if(projected.ContainsKey(line.Id))continue;
            decimal? net=null,gross=null;
            try
            {
                if(line.Quantity>0&&line.WasteRate>=0&&line.WasteRate<=1)
                {
                    if(line.ParentLineId is null)net=line.Quantity;
                    else if(projected.TryGetValue(line.ParentLineId.Value,out var parent)&&parent.RevisionId==line.RevisionId&&parent.CalculatedQuantity.HasValue)
                        net=checked(parent.CalculatedQuantity.Value*line.Quantity);
                    if(net==0)net=null; // Positive operands below decimal precision are not genuine zero consumption.
                    if(net.HasValue&&line.WasteRate<1)gross=checked(net.Value/(1-line.WasteRate));
                    if(gross==0)gross=null;
                }
            }
            catch(OverflowException){net=null;gross=null;}
            projected[line.Id]=line with{CalculatedNetQuantity=net,CalculatedQuantity=gross};
            if(children.TryGetValue(line.Id,out var descendants))foreach(var child in descendants)queue.Enqueue(child);
        }
        // Orphaned or cyclic components cannot be given a quantity without a valid root path.
        return lines.Select(l=>projected.TryGetValue(l.Id,out var value)?value:l with{CalculatedNetQuantity=null,CalculatedQuantity=null}).ToArray();
    }
}

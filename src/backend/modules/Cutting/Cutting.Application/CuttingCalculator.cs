namespace Cutting.Application;

/// <summary>Decimal arithmetic without intermediate rounding; display rounding belongs to the caller.</summary>
public static class CuttingCalculator
{
    public static IReadOnlyList<CuttingPlanReport> CalculatePlans(CuttingDocument document,
        IReadOnlyDictionary<Guid,IReadOnlyList<CuttingOrderLine>> orders)
        => document.Groups.SelectMany(group => group.Plans.Select(plan =>
        {
            var keys=plan.Lines.Select(Key).ToHashSet();
            var snapshot=(document.OrderSnapshot??(orders.TryGetValue(document.OrderId,out var source)?source:[]))
                .Where(line=>keys.Contains(Key(line))).ToArray();
            var report=Calculate([document with { Groups = [group with { Plans = [plan] }], OrderSnapshot=snapshot }],orders,new(Status:document.Status));
            // Explicit net weight is authoritative. Consumption is a fallback only in an established kg unit.
            var planned=PlannedWeight(group,plan);
            var net=Sum(plan.Actuals.Select(a=>ActualWeight(group,a)));
            var plannedGrams=Ratio(planned,Sum(plan.Lines.Select(line=>line.PiecesPerLayer*plan.Layers)),1000m);
            var cutGrams=Ratio(net,report.Totals.CutQuantity,1000m);
            return new CuttingPlanReport(group.Id,plan.Id,report,new(planned,net,plannedGrams,cutGrams,
                cutGrams-plannedGrams,Ratio(cutGrams-plannedGrams,plannedGrams)));
        })).ToArray();

    private static decimal? Sum(IEnumerable<decimal?> values)
    { var a=values.ToArray();return a.Length==0||a.Any(x=>!x.HasValue)?null:a.Sum(x=>x!.Value); }
    private static decimal? Ratio(decimal? numerator,decimal? denominator,decimal scale=100m)
    { try { return numerator.HasValue&&denominator>0?numerator.Value/denominator.Value*scale:null; } catch(OverflowException){return null;} }
    private static (Guid,Guid,Guid?) Key(CuttingOrderLine x)=>(x.VariantOptionId,x.SizeOptionId,x.SetPartOptionId);
    private static (Guid,Guid,Guid?) Key(CuttingPlanLine x)=>(x.VariantOptionId,x.SizeOptionId,x.SetPartOptionId);
    private static (Guid,Guid,Guid?) Key(CuttingActualLine x)=>(x.VariantOptionId,x.SizeOptionId,x.SetPartOptionId);
    private static decimal? PlanMinutes(CuttingPlan p)=>p.PlannedMinutes??(p.PlannedStart.HasValue&&p.PlannedEnd.HasValue?(decimal)(p.PlannedEnd.Value-p.PlannedStart.Value).TotalMinutes:null);
    private static decimal? NetMinutes(CuttingActual a)=>a.StartedAt.HasValue&&a.EndedAt.HasValue&&a.DowntimeMinutes.HasValue
        ?(decimal)(a.EndedAt.Value-a.StartedAt.Value).TotalMinutes-a.DowntimeMinutes.Value:null;
    private static decimal? NetConsumption(CuttingActual a)=>a.GrossConsumption-a.ReturnedConsumption-a.ReusableConsumption;
    private static bool IsKg(CuttingGroup group)=>string.Equals(group.Unit,"kg",StringComparison.OrdinalIgnoreCase)||string.Equals(group.Unit,"Kilogram",StringComparison.OrdinalIgnoreCase);
    private static decimal? PlannedWeight(CuttingGroup group,CuttingPlan plan)=>plan.PlannedWeightKg??(IsKg(group)?plan.PlannedConsumption:null);
    private static decimal? ActualWeight(CuttingGroup group,CuttingActual actual)=>actual.NetWeightKg??(IsKg(group)?NetConsumption(actual):null);
    private sealed record Selected(CuttingDocument Document,CuttingGroup Group,CuttingPlan Plan,IReadOnlyList<CuttingPlanLine> Lines,IReadOnlyList<CuttingActual> Actuals);
    public static CuttingReport Calculate(IReadOnlyList<CuttingDocument> documents,
        IReadOnlyDictionary<Guid,IReadOnlyList<CuttingOrderLine>> orders, CuttingReportFilter? filter=null)
    {
        filter??=new();
        if(filter.From>filter.To)throw new ArgumentException("Başlangıç tarihi bitişten sonra olamaz.",nameof(filter));
        var warnings=new List<string>();
        bool Match(Guid variant,Guid size)=>(filter.VariantOptionId is null||filter.VariantOptionId==variant)&&(filter.SizeOptionId is null||filter.SizeOptionId==size);
        bool DateMatch(DateOnly date)=>(filter.From is null||date>=filter.From)&&(filter.To is null||date<=filter.To);
        var filtered=documents.Where(d=>(filter.OrderId is null||d.OrderId==filter.OrderId)&&(filter.Status is null?d.Status!="Cancelled":d.Status==filter.Status)).GroupBy(x=>x.Id).Select(x=>x.First()).ToArray();
        var actualIds=new HashSet<Guid>();
        var selected=new List<Selected>();
        foreach(var d in filtered)
        foreach(var g in d.Groups.Where(g=>filter.MaterialId is null||g.MaterialId==filter.MaterialId))
        foreach(var p in g.Plans)
        {
            var lines=p.Lines.Where(x=>Match(x.VariantOptionId,x.SizeOptionId)).ToArray();
            if(lines.Length==0)continue;
            var actuals=new List<CuttingActual>();
            foreach(var a in p.Actuals.Where(a=>DateMatch(a.Date)))
            {
                if(!actualIds.Add(a.Id)){warnings.Add("Tekrarlayan gerçekleşme kimliği yalnız bir kez sayıldı.");continue;}
                actuals.Add(a with {Lines=a.Lines.Where(x=>Match(x.VariantOptionId,x.SizeOptionId)).ToArray()});
            }
            // A range selects a pastal by its planned interval or an included actual; the plan is counted once.
            if((filter.From is not null||filter.To is not null)&&actuals.Count==0&&
                !(p.PlannedStart.HasValue && (filter.To is null||DateOnly.FromDateTime(p.PlannedStart.Value.Date)<=filter.To)&&
                  (filter.From is null||DateOnly.FromDateTime((p.PlannedEnd??p.PlannedStart).Value.Date)>=filter.From)))continue;
            selected.Add(new(d,g,p,lines,actuals));
        }
        if(filter.From is not null||filter.To is not null)warnings.Add("Tarih filtresi gerçekleşme işlem tarihine uygulanır; seçilen pastalın planı bir kez tam kapsamıyla karşılaştırılır. Plansız tarihli pastal yalnız eşleşen gerçekleşmesi varsa dahildir.");
        var rows=new List<CuttingQuantityReport>();
        var selectedOrderIds=selected.Select(x=>x.Document.OrderId).Distinct().ToHashSet();
        // Include order-only rows for an unplanned document without inventing a pastal.
        foreach(var d in filtered.Where(d=>filter.MaterialId is null||d.Groups.Any(g=>g.MaterialId==filter.MaterialId)))
            if(filter.From is null&&filter.To is null)selectedOrderIds.Add(d.OrderId);
        foreach(var orderId in selectedOrderIds)
        {
            var snapshots=filtered.Where(d=>d.OrderId==orderId).Select(d=>d.OrderSnapshot??(orders.TryGetValue(orderId,out var source)?source:[])).ToArray();
            var keys=snapshots.SelectMany(x=>x).Where(x=>Match(x.VariantOptionId,x.SizeOptionId)).Select(Key)
                .Concat(selected.Where(x=>x.Document.OrderId==orderId).SelectMany(x=>x.Lines).Select(Key)).Distinct();
            foreach(var key in keys)
            {
                var quantities=snapshots.Select(snapshot=>snapshot.FirstOrDefault(x=>Key(x)==key)?.Quantity).Distinct().ToArray();
                decimal? orderQuantity=quantities.Length==1?quantities[0]:null;
                if(quantities.Length>1)warnings.Add("Aynı siparişin kesim kayıtlarında farklı sipariş sürümleri var; çelişen sipariş paydası hesaplanamadı. Kontrollü sipariş yenilemesi gerekir.");
                var plans=selected.Where(x=>x.Document.OrderId==orderId&&x.Lines.Any(l=>Key(l)==key)).ToArray();
                var planned=Sum(plans.Select(x=>x.Lines.First(l=>Key(l)==key).PiecesPerLayer*x.Plan.Layers));
                var actualLines=plans.SelectMany(x=>x.Actuals).SelectMany(x=>x.Lines).Where(x=>Key(x)==key).ToArray();
                rows.Add(Row(orderId,key.Item1,key.Item2,key.Item3,orderQuantity,planned,actualLines.Sum(x=>x.CutQuantity),Sum(actualLines.Select(x=>x.GoodQuantity)),Sum(actualLines.Select(x=>x.DefectQuantity))));
            }
        }
        var totals=Row(Guid.Empty,Guid.Empty,Guid.Empty,null,Sum(rows.Select(x=>x.OrderQuantity)),Sum(rows.Select(x=>x.PlannedQuantity)),rows.Sum(x=>x.CutQuantity),Sum(rows.Select(x=>x.GoodQuantity)),Sum(rows.Select(x=>x.DefectQuantity)));
        var allocationFiltered=filter.VariantOptionId is not null||filter.SizeOptionId is not null;
        if(allocationFiltered)warnings.Add("Beden/varyant filtresinde ortak pastal tüketimi, ağırlığı, birim ağırlığı, süre ve yerleşim alanı dağıtım anahtarı bulunmadığından hesaplanamıyor.");
        var materials=selected.GroupBy(x=>(x.Group.MaterialId,x.Group.Unit)).Select(g=>
        {
            decimal? planned=allocationFiltered?null:Sum(g.Select(x=>IsKg(x.Group)?PlannedWeight(x.Group,x.Plan):x.Plan.PlannedConsumption));
            decimal? net=allocationFiltered?null:Sum(g.SelectMany(x=>x.Actuals.Select(a=>IsKg(x.Group)?ActualWeight(x.Group,a):NetConsumption(a))));
            decimal? plannedWeight=allocationFiltered?null:Sum(g.Select(x=>PlannedWeight(x.Group,x.Plan)));
            decimal? netWeight=allocationFiltered?null:Sum(g.SelectMany(x=>x.Actuals.Select(a=>ActualWeight(x.Group,a))));
            var plannedQuantity=Sum(g.SelectMany(x=>x.Lines.Select(l=>l.PiecesPerLayer*x.Plan.Layers)));
            var cutQuantity=g.SelectMany(x=>x.Actuals).SelectMany(a=>a.Lines).Sum(l=>l.CutQuantity);
            var plannedGrams=Ratio(plannedWeight,plannedQuantity,1000m);
            var actualGrams=Ratio(netWeight,cutQuantity,1000m);
            return new CuttingMaterialReport(g.Key.MaterialId,g.Key.Unit,planned,net,net-planned,Ratio(net-planned,planned),
                plannedWeight,netWeight,plannedQuantity,cutQuantity,plannedGrams,actualGrams,actualGrams-plannedGrams,Ratio(actualGrams-plannedGrams,plannedGrams));
        }).ToArray();
        decimal? plannedMinutes=allocationFiltered?null:Sum(selected.Select(x=>PlanMinutes(x.Plan)));
        decimal? actualMinutes=allocationFiltered?null:Sum(selected.SelectMany(x=>x.Actuals).Select(NetMinutes));
        decimal? expectedMinutes=allocationFiltered?null:Sum(selected.Select(x=>PlanMinutes(x.Plan)*Ratio(Sum(x.Actuals.SelectMany(a=>a.Lines).Select(l=>l.GoodQuantity)),Sum(x.Lines.Select(l=>l.PiecesPerLayer*x.Plan.Layers)),1m)));
        decimal? pattern=allocationFiltered?null:Sum(selected.Select(x=>x.Plan.PatternAreaM2));
        decimal? usable=allocationFiltered?null:Sum(selected.Select(x=>x.Plan.UsableAreaM2));
        if(rows.Any(x=>x.ExcessQuantity>0))warnings.Add("Fazla sağlam kesim var; sağlam kesim sevkiyat onayı değildir.");
        return new(rows,totals,materials,new(plannedMinutes,actualMinutes,actualMinutes-plannedMinutes,Ratio(totals.GoodQuantity,actualMinutes,60m),Ratio(expectedMinutes,actualMinutes),Ratio(pattern,usable)),warnings.Distinct().ToArray());
    }
    private static CuttingQuantityReport Row(Guid order,Guid variant,Guid size,Guid? part,decimal? quantity,decimal? planned,decimal cut,decimal? good,decimal? defect)
    {
        var remainder=quantity-good;
        return new(order,variant,size,part,quantity,planned,cut,good,defect,cut-planned,Ratio(cut-planned,planned),Ratio(good,quantity),
            remainder,remainder.HasValue?Math.Max(0,-remainder.Value):null,Ratio(defect,cut));
    }
}

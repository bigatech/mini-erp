using Cutting.Application;
using Xunit;
namespace MiniErp.Cutting.Tests;

public sealed class CuttingTests
{
    private static readonly Guid Order=Guid.NewGuid(),Variant=Guid.NewGuid(),Size=Guid.NewGuid(),Material=Guid.NewGuid();
    private static readonly DateTimeOffset Start=new(2026,11,3,8,0,0,TimeSpan.Zero);
    private static CuttingPlan Plan()=>new(Guid.NewGuid(),"P1",10,5,1.5m,50,Start,Start.AddHours(1),60,6,7.5m,
        [new(Variant,Size,null,10)],
        [new(Guid.NewGuid(),new(2026,11,3),4,25,2,3,Start,Start.AddMinutes(40),10,null,[new(Variant,Size,null,40,36,4)]),
         new(Guid.NewGuid(),new(2026,11,4),6,35,3,2,Start.AddDays(1),Start.AddDays(1).AddMinutes(40),10,null,[new(Variant,Size,null,60,54,6)])]);
    private static CuttingDocument Doc(CuttingPlan? plan=null)=>new(Guid.NewGuid(),Order,1,"K1","Draft",1,Start,Start,
        [new(Guid.NewGuid(),Material,"kg",null,null,null,null,[plan??Plan()])],[new(Variant,Size,null,100)]);
    private static CuttingReport Report(params CuttingDocument[] documents)=>CuttingCalculator.Calculate(documents,new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>());
    private static SaveCuttingDocument Save(CuttingDocument d)=>new(d.OrderId,d.OrderVersion,d.Status,d.Groups);
    private static void Valid(CuttingDocument d)=>CuttingRules.Validate(Save(d),[new(Variant,Size,null,100)]);

    [Fact] public void Partial_actuals_sum_once_with_independent_expected_totals()
    {
        var result=Report(Doc());var r=result.Totals;
        Assert.Equal(100m,r.OrderQuantity);Assert.Equal(100m,r.PlannedQuantity);Assert.Equal(100m,r.CutQuantity);
        Assert.Equal(90m,r.GoodQuantity);Assert.Equal(10m,r.DefectQuantity);Assert.Equal(90m,r.FulfillmentPercent);
        Assert.Equal(10m,r.RemainingQuantity);Assert.Equal(0m,r.ExcessQuantity);Assert.Equal(10m,r.DefectPercent);
        var material=Assert.Single(result.Materials);Assert.Equal(50m,material.NetConsumption);Assert.Equal(0m,material.ConsumptionVariance);
        Assert.Equal(60m,result.Process.ActualNetMinutes);Assert.Equal(90m,result.Process.GoodPerHour);
        Assert.Equal(90m,result.Process.TimeEfficiencyPercent);Assert.Equal(80m,result.Process.LayoutEfficiencyPercent);
    }
    [Fact] public void Multiple_pastals_and_documents_do_not_multiply_order_denominator()
    {
        var first=Doc();var second=Doc(Plan() with{Number="P2"});
        var r=Report(first,second).Totals;
        Assert.Equal(100m,r.OrderQuantity);Assert.Equal(200m,r.PlannedQuantity);Assert.Equal(180m,r.GoodQuantity);
        Assert.Equal(-80m,r.RemainingQuantity);Assert.Equal(80m,r.ExcessQuantity);Assert.Equal(180m,r.FulfillmentPercent);
    }
    [Fact] public void Snapshot_conflict_does_not_invent_order_quantity()
    {
        var second=Doc() with{OrderVersion=2,OrderSnapshot=[new(Variant,Size,null,120)]};
        var r=Report(Doc(),second);Assert.Null(r.Totals.OrderQuantity);Assert.Null(r.Totals.FulfillmentPercent);Assert.NotEmpty(r.Warnings);
    }
    [Theory]
    [InlineData("Draft")]
    [InlineData("Cancelled")]
    public void Pastal_reports_isolate_groups_and_partial_actuals_with_snapshot_denominator(string status)
    {
        var first=Doc();var group=first.Groups[0];
        var second=Plan() with{Number="P1",Layers=4,PlannedConsumption=10,PlannedMinutes=30,PlannedStart=null,PlannedEnd=null,
            Actuals=[new(Guid.NewGuid(),new(2026,11,4),4,10,1,1,Start,Start.AddMinutes(25),5,null,[new(Variant,Size,null,38,35,3)])]};
        var otherGroup=group with{Id=Guid.NewGuid(),Unit="m",Plans=[second]};
        var document=first with{Status=status,Groups=[group,otherGroup]};
        // A newer live order must not replace this document's 100-piece snapshot.
        var reports=CuttingCalculator.CalculatePlans(document,new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>{[Order]=[new(Variant,Size,null,999)]});
        Assert.Equal(2,reports.Count);
        var a=Assert.Single(reports,x=>x.GroupId==group.Id&&x.PlanId==group.Plans[0].Id).Report;
        var b=Assert.Single(reports,x=>x.GroupId==otherGroup.Id&&x.PlanId==second.Id).Report;
        Assert.Equal(100m,a.Totals.OrderQuantity);Assert.Equal(100m,a.Totals.PlannedQuantity);Assert.Equal(100m,a.Totals.CutQuantity);
        Assert.Equal(90m,a.Totals.GoodQuantity);Assert.Equal(10m,a.Totals.DefectQuantity);Assert.Equal(0m,a.Totals.QuantityVariance);
        Assert.Equal(50m,Assert.Single(a.Materials).NetConsumption);Assert.Equal(60m,a.Process.ActualNetMinutes);
        Assert.Equal(100m,b.Totals.OrderQuantity);Assert.Equal(40m,b.Totals.PlannedQuantity);Assert.Equal(38m,b.Totals.CutQuantity);
        Assert.Equal(35m,b.Totals.GoodQuantity);Assert.Equal(3m,b.Totals.DefectQuantity);Assert.Equal(-2m,b.Totals.QuantityVariance);
        Assert.Equal(8m,Assert.Single(b.Materials).NetConsumption);Assert.Equal(-2m,Assert.Single(b.Materials).ConsumptionVariance);
        Assert.Equal(20m,b.Process.ActualNetMinutes);Assert.Equal(-10m,b.Process.DurationVarianceMinutes);
    }
    [Fact] public void Missing_values_are_not_zero_and_units_are_not_combined()
    {
        var p=Plan();p=p with{Layers=null,PatternAreaM2=null,Actuals=[p.Actuals[0] with{ReturnedConsumption=null,DowntimeMinutes=null,Lines=[new(Variant,Size,null,40,null,null)]}]};
        var a=Doc(p);var b=Doc(Plan()) with{Groups=[a.Groups[0] with{Id=Guid.NewGuid(),Unit="m",Plans=[Plan()]}]};
        var r=Report(a,b);Assert.Null(r.Totals.PlannedQuantity);Assert.Null(r.Totals.GoodQuantity);Assert.Null(r.Process.ActualNetMinutes);Assert.Null(r.Process.LayoutEfficiencyPercent);
        Assert.Equal(2,r.Materials.Count);Assert.Null(r.Materials.Single(x=>x.Unit=="kg").NetConsumption);
    }
    private static CuttingWeightReport Weight(CuttingDocument document)=>Assert.Single(CuttingCalculator.CalculatePlans(document,new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>())).Weight;
    [Fact] public void Explicit_weights_take_precedence_and_compare_grams_per_cut_piece()
    {
        var p=Plan();p=p with{PlannedWeightKg=20,Actuals=[p.Actuals[0] with{NetWeightKg=9},p.Actuals[1] with{NetWeightKg=13}]};
        var w=Weight(Doc(p));Assert.Equal(20m,w.PlannedWeightKg);Assert.Equal(22m,w.NetWeightKg);
        Assert.Equal(200m,w.PlannedGramsPerPiece);Assert.Equal(220m,w.ActualGramsPerCutPiece);
        Assert.Equal(20m,w.GramsVariance);Assert.Equal(10m,w.VariancePercent);
    }
    [Fact] public void Pastal_report_excludes_unrelated_order_cells_from_totals_and_weight_denominator()
    {
        var p=Plan() with{PlannedWeightKg=20};
        var d=Doc(p) with{OrderSnapshot=[new(Variant,Size,null,100),new(Variant,Guid.NewGuid(),null,50)]};
        var result=Assert.Single(CuttingCalculator.CalculatePlans(d,new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>()));
        Assert.Single(result.Report.Rows);
        Assert.Equal(100m,result.Report.Totals.OrderQuantity);Assert.Equal(100m,result.Report.Totals.PlannedQuantity);
        Assert.Equal(200m,result.Weight.PlannedGramsPerPiece);
    }
    [Theory]
    [InlineData("kg")]
    [InlineData("Kilogram")]
    public void Established_kg_consumption_is_weight_fallback(string unit)
    {
        var d=Doc();d=d with{Groups=[d.Groups[0] with{Unit=unit}]};var w=Weight(d);
        Assert.Equal(50m,w.PlannedWeightKg);Assert.Equal(50m,w.NetWeightKg);
        Assert.Equal(500m,w.PlannedGramsPerPiece);Assert.Equal(500m,w.ActualGramsPerCutPiece);
    }
    [Fact] public void Other_units_missing_partial_weight_and_zero_denominators_are_not_invented()
    {
        var d=Doc();d=d with{Groups=[d.Groups[0] with{Unit="m"}]};
        var w=Weight(d);Assert.Null(w.PlannedWeightKg);Assert.Null(w.NetWeightKg);Assert.Null(w.ActualGramsPerCutPiece);
        var p=Plan();p=p with{PlannedWeightKg=0,Actuals=[p.Actuals[0] with{NetWeightKg=0},p.Actuals[1]]};
        d=d with{Groups=[d.Groups[0] with{Plans=[p]}]};w=Weight(d);
        Assert.Equal(0m,w.PlannedGramsPerPiece);Assert.Null(w.NetWeightKg);Assert.Null(w.VariancePercent);
        p=p with{Actuals=[p.Actuals[0] with{Lines=[new(Variant,Size,null,0,0,0)]}]};
        d=d with{Groups=[d.Groups[0] with{Plans=[p]}]};w=Weight(d);
        Assert.Equal(0m,w.NetWeightKg);Assert.Null(w.ActualGramsPerCutPiece);
    }
    [Fact] public void Negative_explicit_weight_is_rejected()
    {
        var p=Plan();p=p with{PlannedWeightKg=-1,Actuals=[p.Actuals[0] with{NetWeightKg=-2}]};
        var error=Assert.Throws<CuttingValidationException>(()=>Valid(Doc(p)));
        Assert.Contains(error.Errors.Keys,x=>x.EndsWith("plannedWeightKg"));Assert.Contains(error.Errors.Keys,x=>x.EndsWith("netWeightKg"));
    }
    [Fact] public void Material_totals_combine_four_pastals_and_explicit_net_kg_without_gross_inputs()
    {
        var quantities=new[]{66m,24m,64m,10m};var plannedWeights=new[]{17.95m,6.41m,17.75m,3.12m};var netWeights=new[]{17.86m,6.18m,18.17m,2.92m};
        var plans=quantities.Select((qty,i)=>Plan() with{Id=Guid.NewGuid(),Number=$"P{i}",Layers=1,PlannedConsumption=null,PlannedWeightKg=plannedWeights[i],
            Lines=[new(Variant,Size,null,qty)],Actuals=[new(Guid.NewGuid(),new(2026,11,4),1,null,null,null,null,null,null,null,[new(Variant,Size,null,qty,qty,0)],netWeights[i])]}).ToArray();
        var d=Doc() with{OrderSnapshot=[new(Variant,Size,null,164)]};d=d with{Groups=[d.Groups[0] with{Unit="Kilogram",Plans=plans}]};
        var m=Assert.Single(Report(d).Materials);
        Assert.Equal(45.23m,m.PlannedConsumption);Assert.Equal(45.13m,m.NetConsumption);Assert.Equal(-.10m,m.ConsumptionVariance);
        Assert.Equal(45.23m,m.PlannedWeightKg);Assert.Equal(45.13m,m.NetWeightKg);Assert.Equal(164m,m.PlannedQuantity);Assert.Equal(164m,m.CutQuantity);
        Assert.InRange(Math.Abs(m.PlannedGramsPerPiece!.Value-45230m/164m),0m,.000000000000000001m);
        Assert.InRange(Math.Abs(m.ActualGramsPerCutPiece!.Value-45130m/164m),0m,.000000000000000001m);
        Assert.InRange(Math.Abs(m.GramsVariance!.Value-(-100m/164m)),0m,.000000000000000001m);
        Assert.InRange(Math.Abs(m.WeightVariancePercent!.Value-(-.10m/45.23m*100m)),0m,.000000000000000001m);
    }
    [Fact] public void Material_unit_grams_use_weighted_piece_totals_instead_of_pastal_average()
    {
        var small=Plan() with{Layers=1,PlannedWeightKg=1,Lines=[new(Variant,Size,null,10)],Actuals=[new(Guid.NewGuid(),new(2026,11,3),1,null,null,null,null,null,null,null,[new(Variant,Size,null,10,10,0)],2)]};
        var large=Plan() with{Layers=1,PlannedWeightKg=18,Lines=[new(Variant,Size,null,90)],Actuals=[new(Guid.NewGuid(),new(2026,11,4),1,null,null,null,null,null,null,null,[new(Variant,Size,null,90,90,0)],27)]};
        var d=Doc();d=d with{Groups=[d.Groups[0] with{Plans=[small,large]}]};var m=Assert.Single(Report(d).Materials);
        Assert.Equal(190m,m.PlannedGramsPerPiece);Assert.Equal(290m,m.ActualGramsPerCutPiece);Assert.Equal(100m,m.GramsVariance);
        var dated=CuttingCalculator.Calculate([d],new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>(),new(From:new(2026,11,4),To:new(2026,11,4)));
        var dm=Assert.Single(dated.Materials);Assert.Equal(90m,dm.CutQuantity);Assert.Equal(27m,dm.NetWeightKg);Assert.Equal(300m,dm.ActualGramsPerCutPiece);
    }
    [Fact] public void Non_kg_consumption_remains_separate_from_explicit_weight_and_materials_do_not_merge()
    {
        var p=Plan() with{PlannedWeightKg=20};p=p with{Actuals=[p.Actuals[0] with{NetWeightKg=9},p.Actuals[1] with{NetWeightKg=13}]};
        var d=Doc(p);var first=d.Groups[0] with{Unit="m"};d=d with{Groups=[first,first with{Id=Guid.NewGuid(),MaterialId=Guid.NewGuid(),Plans=[Plan()]}]};
        var r=Report(d);Assert.Equal(2,r.Materials.Count);var m=Assert.Single(r.Materials,x=>x.MaterialId==first.MaterialId);
        Assert.Equal(50m,m.PlannedConsumption);Assert.Equal(50m,m.NetConsumption);Assert.Equal(20m,m.PlannedWeightKg);Assert.Equal(22m,m.NetWeightKg);
        Assert.Equal(200m,m.PlannedGramsPerPiece);Assert.Equal(220m,m.ActualGramsPerCutPiece);
        var unknown=Assert.Single(r.Materials,x=>x.MaterialId!=first.MaterialId);Assert.Null(unknown.PlannedWeightKg);Assert.Null(unknown.NetWeightKg);Assert.Null(unknown.ActualGramsPerCutPiece);
    }
    [Fact] public void Material_weight_totals_preserve_missing_partials_and_zero_piece_denominators()
    {
        var p=Plan() with{PlannedWeightKg=null,PlannedConsumption=null};p=p with{Actuals=[p.Actuals[0] with{NetWeightKg=2},p.Actuals[1] with{GrossConsumption=null}]};
        var m=Assert.Single(Report(Doc(p)).Materials);Assert.Null(m.PlannedWeightKg);Assert.Null(m.NetWeightKg);Assert.Null(m.NetConsumption);Assert.Null(m.ActualGramsPerCutPiece);
        p=p with{Layers=0,PlannedWeightKg=0,Actuals=[p.Actuals[0] with{NetWeightKg=0,Lines=[new(Variant,Size,null,0,0,0)]}]};
        m=Assert.Single(Report(Doc(p)).Materials);Assert.Equal(0m,m.NetWeightKg);Assert.Equal(0m,m.NetConsumption);Assert.Null(m.PlannedGramsPerPiece);Assert.Null(m.ActualGramsPerCutPiece);Assert.Null(m.WeightVariancePercent);
    }
    [Fact] public void Material_allocation_filter_keeps_piece_totals_but_hides_shared_weight()
    {
        var p=Plan() with{PlannedWeightKg=20};p=p with{Actuals=[p.Actuals[0] with{NetWeightKg=9},p.Actuals[1] with{NetWeightKg=13}]};
        var r=CuttingCalculator.Calculate([Doc(p)],new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>(),new(SizeOptionId:Size));var m=Assert.Single(r.Materials);
        Assert.Equal(100m,m.PlannedQuantity);Assert.Equal(100m,m.CutQuantity);
        Assert.Null(m.PlannedConsumption);Assert.Null(m.NetConsumption);Assert.Null(m.PlannedWeightKg);Assert.Null(m.NetWeightKg);
        Assert.Null(m.PlannedGramsPerPiece);Assert.Null(m.ActualGramsPerCutPiece);Assert.Null(m.GramsVariance);Assert.Null(m.WeightVariancePercent);
    }
    [Fact] public void Zero_denominators_are_unavailable()
    {
        var p=Plan();p=p with{Layers=0,PlannedMinutes=0,Actuals=[p.Actuals[0] with{StartedAt=Start,EndedAt=Start,DowntimeMinutes=0,Lines=[new(Variant,Size,null,0,0,0)]}]};
        var r=Report(Doc(p));Assert.Null(r.Totals.QuantityVariancePercent);Assert.Null(r.Totals.DefectPercent);Assert.Null(r.Process.GoodPerHour);Assert.Null(r.Process.TimeEfficiencyPercent);
    }
    [Fact] public void No_actuals_do_not_report_observed_zero_quality_time_or_consumption()
    {
        var r=Report(Doc(Plan() with{Actuals=[]}));
        Assert.Null(r.Totals.GoodQuantity);Assert.Null(r.Totals.DefectQuantity);Assert.Null(r.Process.ActualNetMinutes);Assert.Null(Assert.Single(r.Materials).NetConsumption);
    }
    [Fact] public void Date_filter_selects_actuals_and_counts_selected_plan_once()
    {
        var r=CuttingCalculator.Calculate([Doc()],new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>(),new(From:new(2026,11,4),To:new(2026,11,4)));
        Assert.Equal(100m,r.Totals.PlannedQuantity);Assert.Equal(60m,r.Totals.CutQuantity);Assert.Equal(54m,r.Totals.GoodQuantity);Assert.Equal(100m,r.Totals.OrderQuantity);
    }
    [Fact] public void Cancelled_records_excluded_and_explicit_status_filter_supported()
    {
        var d=Doc() with{Status="Cancelled"};Assert.Empty(Report(d).Rows);
        Assert.Single(CuttingCalculator.Calculate([d],new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>(),new(Status:"Cancelled")).Rows);
    }
    [Fact] public void Variant_filter_does_not_allocate_shared_consumption_or_time()
    {
        var r=CuttingCalculator.Calculate([Doc()],new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>(),new(VariantOptionId:Variant));
        Assert.Equal(100m,r.Totals.CutQuantity);Assert.Null(r.Process.ActualNetMinutes);Assert.Null(Assert.Single(r.Materials).NetConsumption);
    }
    [Fact] public void Duplicate_actual_identity_is_defensively_counted_once()
    {
        var d=Doc();var p=d.Groups[0].Plans[0];p=p with{Actuals=[p.Actuals[0],p.Actuals[0]]};
        var r=Report(Doc(p));Assert.Equal(40m,r.Totals.CutQuantity);Assert.NotEmpty(r.Warnings);
    }
    [Theory]
    [InlineData(-1d,0d,0d)] [InlineData(10d,8d,1d)] [InlineData(10d,11d,null)] [InlineData(1.5d,1.5d,0d)]
    public void Invalid_classifications_are_rejected(double cut,double good,double? defect)
    {
        var p=Plan();var a=p.Actuals[0] with{Lines=[new(Variant,Size,null,(decimal)cut,(decimal)good,defect.HasValue?(decimal)defect:null)]};
        Assert.Throws<CuttingValidationException>(()=>Valid(Doc(p with{Actuals=[a]})));
    }
    [Fact] public void Valid_partial_classification_can_remain_unknown()
    {
        var p=Plan();var a=p.Actuals[0] with{DowntimeReason="Makine ayarı",Lines=[new(Variant,Size,null,40,36,null)]};
        Valid(Doc(p with{Actuals=[a]}));Assert.Null(Report(Doc(p with{Actuals=[a]})).Totals.DefectQuantity);
    }
    [Fact] public void Foreign_order_selection_negative_net_and_impossible_duration_rejected()
    {
        var p=Plan();var a=p.Actuals[0] with{GrossConsumption=1,ReturnedConsumption=2,DowntimeMinutes=100,Lines=[new(Guid.NewGuid(),Size,null,1,1,0)]};
        var ex=Assert.Throws<CuttingValidationException>(()=>Valid(Doc(p with{Actuals=[a]})));
        Assert.Contains(ex.Errors.Keys,x=>x.EndsWith("grossConsumption"));Assert.Contains(ex.Errors.Keys,x=>x.EndsWith("downtimeMinutes"));Assert.Contains(ex.Errors.Keys,x=>x.EndsWith("lines[0]"));
    }
    [Fact] public void Duplicate_id_and_conflicting_plan_duration_rejected()
    {
        var p=Plan();p=p with{PlannedMinutes=59,Actuals=[p.Actuals[0],p.Actuals[0]]};
        var ex=Assert.Throws<CuttingValidationException>(()=>Valid(Doc(p)));
        Assert.Contains(ex.Errors.Keys,x=>x.EndsWith(".id"));Assert.Contains(ex.Errors.Keys,x=>x.EndsWith("plannedMinutes"));
    }
    [Fact] public void Total_defect_ratio_uses_weighted_denominator()
    {
        var p=Plan();var other=Guid.NewGuid();p=p with{Lines=[new(Variant,Size,null,10),new(other,Size,null,1)],Actuals=[p.Actuals[0] with{Lines=[new(Variant,Size,null,100,90,10),new(other,Size,null,10,5,5)]}]};
        var d=Doc(p) with{OrderSnapshot=[new(Variant,Size,null,100),new(other,Size,null,10)]};
        Assert.Equal(15m/110m*100m,Report(d).Totals.DefectPercent);
    }
}

using ProductLifecycle.Application;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class BomRequirementsTests
{
    private static readonly Guid Revision=Guid.NewGuid();
    private static BomLineDto Node(Guid id,Guid? parent,decimal quantity,decimal waste,Guid? variant=null)=>new(id,Revision,Guid.NewGuid(),id.ToString("N"),"Material",parent is null?"Kilogram":"Metre",quantity,waste,parent,variant);
    [Fact] public void Three_levels_propagate_gross_parent_and_each_yield_without_recipe_mutation()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();var recipe=new[]{Node(a,null,7,.1m),Node(b,a,2,.2m),Node(c,b,3,0)};
        var result=BomRequirementsCalculator.Calculate(recipe,new(a,100));
        Assert.True(result.Complete);Assert.Equal(3,result.Rows.Count);
        Assert.InRange(result.Rows[1].GrossQuantity!.Value,277.7777777777777777777777m,277.7777777777777777777779m);
        Assert.InRange(result.Rows[2].GrossQuantity!.Value,833.3333333333333333333330m,833.3333333333333333333340m);
        Assert.InRange(result.Rows[2].PerTopUnit!.Value,8.3333333333333333333333m,8.3333333333333333333334m);
        Assert.Equal(7,recipe[0].Quantity);Assert.Equal(2,recipe[1].Quantity);Assert.True(result.Rows[2].IsLeaf);
    }
    [Fact] public void Hundred_percent_waste_is_uncomputable_and_propagates_null_not_zero()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var result=BomRequirementsCalculator.Calculate([Node(a,null,1,1),Node(b,a,2,0)],new(a,100));
        Assert.False(result.Complete);Assert.Null(result.Rows[0].GrossQuantity);Assert.Null(result.Rows[1].NetQuantity);Assert.Null(result.Rows[1].GrossQuantity);Assert.NotEmpty(result.Warnings);
    }
    [Fact] public void Decimal_overflow_returns_incomplete_null_requirements()
    {var a=Guid.NewGuid();var result=BomRequirementsCalculator.Calculate([Node(a,null,1,.5m)],new(a,decimal.MaxValue));Assert.False(result.Complete);Assert.Null(result.Rows[0].GrossQuantity);Assert.NotEmpty(result.Warnings);}
    [Fact] public void Positive_quantities_below_decimal_precision_are_not_reported_as_zero()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();const decimal tiny=.0000000000000000000000000001m;
        var result=BomRequirementsCalculator.Calculate([Node(a,null,1,0),Node(b,a,tiny,0)],new(a,tiny));
        Assert.False(result.Complete);Assert.Null(result.Rows[1].NetQuantity);Assert.Null(result.Rows[1].GrossQuantity);Assert.NotEmpty(result.Warnings);
    }
    [Fact] public void Scoped_children_require_explicit_axis_selection()
    {var a=Guid.NewGuid();var b=Guid.NewGuid();var v=Guid.NewGuid();Assert.Throws<ArgumentException>(()=>BomRequirementsCalculator.Calculate([Node(a,null,1,0),Node(b,a,2,0,v)],new(a,1)));}
    [Fact] public void Selected_scope_prunes_other_variant_and_never_mixes_branches()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();var v=Guid.NewGuid();
        var result=BomRequirementsCalculator.Calculate([Node(a,null,1,0),Node(b,a,2,0,v),Node(c,a,3,0,Guid.NewGuid())],new(a,10,v));
        Assert.Equal(2,result.Rows.Count);Assert.Equal(20,result.Rows.Single(r=>r.LineId==b).GrossQuantity);
    }
    [Fact] public void Root_fixed_scope_cannot_be_overridden()
    {var a=Guid.NewGuid();Assert.Throws<ArgumentException>(()=>BomRequirementsCalculator.Calculate([Node(a,null,1,0,Guid.NewGuid())],new(a,1,Guid.NewGuid())));}
    [Fact] public void Orphan_and_cycles_are_explicit_validation_errors()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
        Assert.Throws<ArgumentException>(()=>BomRequirementsCalculator.Calculate([Node(a,null,1,0),Node(b,Guid.NewGuid(),1,0)],new(a,1)));
        Assert.Throws<ArgumentException>(()=>BomRequirementsCalculator.Calculate([Node(a,null,1,0),Node(b,c,1,0),Node(c,b,1,0)],new(a,1)));
    }
    [Fact] public void Net_quantity_must_be_positive_and_root_must_be_top_level()
    {var a=Guid.NewGuid();var b=Guid.NewGuid();var rows=new[]{Node(a,null,1,0),Node(b,a,1,0)};Assert.Throws<ArgumentException>(()=>BomRequirementsCalculator.Calculate(rows,new(a,0)));Assert.Throws<ArgumentException>(()=>BomRequirementsCalculator.Calculate(rows,new(b,1)));}
}

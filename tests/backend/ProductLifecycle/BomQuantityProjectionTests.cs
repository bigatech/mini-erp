using ProductLifecycle.Application;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class BomQuantityProjectionTests
{
    private static readonly Guid Revision=Guid.NewGuid();
    private static BomLineDto Line(Guid id,Guid? parent,decimal quantity,decimal waste)=>new(id,Revision,Guid.NewGuid(),"MAT","Malzeme","Kilogram",quantity,waste,parent);
    [Fact]public void Child_and_grandchild_use_parent_gross_and_their_own_yield()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
        var input=new[]{Line(c,b,1,.2m),Line(a,null,.2m,0),Line(b,a,1,.1m)};
        var output=BomQuantityProjection.Apply(input);
        Assert.Equal(.2m,output.Single(l=>l.Id==a).CalculatedQuantity);
        Assert.InRange(output.Single(l=>l.Id==b).CalculatedQuantity!.Value,.2222222222222222222222222221m,.2222222222222222222222222223m);
        Assert.InRange(output.Single(l=>l.Id==c).CalculatedQuantity!.Value,.2777777777777777777777777775m,.2777777777777777777777777779m);
        Assert.All(input,l=>Assert.Null(l.CalculatedQuantity));Assert.Equal(1,output.Single(l=>l.Id==b).Quantity);
    }
    [Fact]public void Legacy_conversion_ratios_remain_effective()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var output=BomQuantityProjection.Apply([Line(a,null,.2m,0),Line(b,a,.8m,.1m)]);
        Assert.InRange(output[1].CalculatedQuantity!.Value,.1777777777777777777777777776m,.1777777777777777777777777779m);Assert.Equal(.8m,output[1].Quantity);
    }
    [Fact]public void Hundred_percent_waste_propagates_unknown_instead_of_zero()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var output=BomQuantityProjection.Apply([Line(a,null,1,1),Line(b,a,1,0)]);
        Assert.Equal(1m,output[0].CalculatedNetQuantity);Assert.Null(output[0].CalculatedQuantity);Assert.Null(output[1].CalculatedNetQuantity);Assert.Null(output[1].CalculatedQuantity);
    }
    [Fact]public void Orphans_and_cycles_have_no_projected_quantity()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var output=BomQuantityProjection.Apply([Line(a,b,1,0),Line(b,a,1,0),Line(Guid.NewGuid(),Guid.NewGuid(),1,0)]);
        Assert.All(output,l=>Assert.Null(l.CalculatedQuantity));
    }
    [Fact]public void Overflow_and_underflow_are_not_silently_numeric_results()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();const decimal tiny=.0000000000000000000000000001m;
        var overflow=BomQuantityProjection.Apply([Line(a,null,decimal.MaxValue,.5m)]);Assert.Null(overflow[0].CalculatedQuantity);
        var underflow=BomQuantityProjection.Apply([Line(a,null,tiny,0),Line(b,a,tiny,0)]);Assert.Null(underflow[1].CalculatedNetQuantity);Assert.Null(underflow[1].CalculatedQuantity);
    }
    [Fact]public void Independent_roots_are_never_aggregated()
    {
        var output=BomQuantityProjection.Apply([Line(Guid.NewGuid(),null,2,.5m),Line(Guid.NewGuid(),null,3,0)]);
        Assert.Equal(4m,output[0].CalculatedQuantity);Assert.Equal(3m,output[1].CalculatedQuantity);
    }
}

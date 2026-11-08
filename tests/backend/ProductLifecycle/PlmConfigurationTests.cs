using ProductLifecycle.Domain;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class PlmConfigurationTests
{
    [Theory][InlineData("variant")][InlineData("size")][InlineData("set-part")]
    public void Allowed_configuration_axes_are_kept(string kind)=>Assert.Equal(kind,PlmDraftRules.Option(kind,1));
    [Theory][InlineData("unknown",1)][InlineData("size",0)][InlineData("set-part",-1)]
    public void Unknown_axis_or_nonpositive_piece_quantity_is_rejected(string kind,int quantity)=>Assert.Throws<ArgumentException>(()=>PlmDraftRules.Option(kind,quantity));
}

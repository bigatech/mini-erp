using ProductLifecycle.Domain;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public class PlmDraftRulesTests
{
    [Theory]
    [InlineData(0,0)] [InlineData(-1,0)] [InlineData(1,-0.01)] [InlineData(1,1.01)]
    public void Invalid_bom_quantities_are_rejected(decimal quantity,decimal waste)=>Assert.Throws<ArgumentException>(()=>PlmDraftRules.Bom(quantity,waste));
    [Theory] [InlineData(1,0)] [InlineData(0.01,1)]
    public void Positive_quantities_and_boundary_waste_are_accepted(decimal quantity,decimal waste)=>PlmDraftRules.Bom(quantity,waste);
    [Theory] [InlineData("")] [InlineData("  ")] [InlineData("a\0b")]
    public void Invalid_identity_fields_rejected(string value)=>Assert.Throws<ArgumentException>(()=>PlmDraftRules.Required(value,"code"));
    [Fact] public void Identity_trim_preserves_code()=>Assert.Equal("M-1",PlmDraftRules.Required(" M-1 ","code"));
}

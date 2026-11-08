using ProductLifecycle.Domain;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;

public sealed class ProductionRouteRulesTests
{
    [Theory][InlineData("minute")][InlineData("second")]
    public void Zero_duration_is_allowed_with_explicit_supported_unit(string unit)=>Assert.Equal(unit,ProductionRouteRules.Duration(0,unit));
    [Fact]public void Negative_duration_is_rejected()=>Assert.Throws<ArgumentException>(()=>ProductionRouteRules.Duration(-1,"minute"));
    [Theory][InlineData(null)][InlineData("")][InlineData("hour")]
    public void Unknown_or_absent_unit_is_not_inferred(string? unit)=>Assert.Throws<ArgumentException>(()=>ProductionRouteRules.Duration(1,unit));
    [Theory][InlineData(0)][InlineData(-1)]
    public void Nonpositive_sequence_is_rejected(int sequence)=>Assert.Throws<ArgumentException>(()=>ProductionRouteRules.Sequence(sequence));
    [Fact]public void Positive_sequence_is_valid()=>ProductionRouteRules.Sequence(10);
}

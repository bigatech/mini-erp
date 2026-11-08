using ProductLifecycle.Domain;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class BomLineEditRulesTests
{
    [Theory][InlineData(null)][InlineData("")][InlineData("   ")]
    public void Blank_label_is_optional_for_legacy_rows(string? value)=>Assert.Null(BomLineEditRules.Label(value));
    [Fact]public void Description_is_trimmed_without_changing_meaning()=>Assert.Equal("Üst parça ana kumaş",BomLineEditRules.Label(" Üst parça ana kumaş "));
    [Fact]public void Description_rejects_database_null_character()=>Assert.Throws<ArgumentException>(()=>BomLineEditRules.Label("Kumaş\0"));
    [Fact]public void Description_length_is_bounded()=>Assert.Throws<ArgumentException>(()=>BomLineEditRules.Label(new string('a',201)));
}

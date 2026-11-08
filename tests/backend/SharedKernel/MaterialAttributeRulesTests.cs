using SharedKernel.Application;
using Xunit;
namespace MiniErp.SharedKernel.Tests;

public sealed class MaterialAttributeRulesTests
{
    private static readonly MaterialGroupDto Fabric=new("KUMAS","Kumaş",[new("gramaj","Gramaj","number","g/m²"),new("karisim","Karışım","text",null)]);
    [Fact] public void Legacy_missing_attributes_are_valid_and_not_zero(){MaterialAttributeRules.Validate(null,null,null);MaterialAttributeRules.Validate(Fabric,"KUMAS",new Dictionary<string,string>{{"gramaj",""}});}
    [Fact] public void Values_require_selected_group(){Assert.Throws<ArgumentException>(()=>MaterialAttributeRules.Validate(null,null,new Dictionary<string,string>{{"gramaj","180"}}));}
    [Fact] public void Unknown_group_and_field_are_rejected(){Assert.Throws<ArgumentException>(()=>MaterialAttributeRules.Validate(null,"UNKNOWN",null));Assert.Throws<ArgumentException>(()=>MaterialAttributeRules.Validate(Fabric,"KUMAS",new Dictionary<string,string>{{"ebat","10x20"}}));}
    [Theory][InlineData("0")][InlineData("-1")][InlineData("1,5")][InlineData("abc")]
    public void Numeric_attribute_has_explicit_positive_invariant_value(string value){Assert.Throws<ArgumentException>(()=>MaterialAttributeRules.Validate(Fabric,"KUMAS",new Dictionary<string,string>{{"gramaj",value}}));}
    [Fact] public void User_values_are_preserved_without_default_textile_measurements(){var values=new Dictionary<string,string>{{"gramaj","180.5"},{"karisim","%80 pamuk / %20 polyester"}};MaterialAttributeRules.Validate(Fabric,"KUMAS",values);Assert.Equal("180.5",values["gramaj"]);Assert.Equal("%80 pamuk / %20 polyester",values["karisim"]);}
    [Fact] public void Null_characters_are_rejected(){Assert.Throws<ArgumentException>(()=>MaterialAttributeRules.Validate(Fabric,"KUMAS",new Dictionary<string,string>{{"karisim","bad\0value"}}));}
}

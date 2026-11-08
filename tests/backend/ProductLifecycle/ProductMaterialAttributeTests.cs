using ProductLifecycle.Domain;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class ProductMaterialAttributeTests
{
    [Fact] public void Product_attributes_copy_the_caller_dictionary()
    {
        var values=new Dictionary<string,string>{{"gramaj","180"}};
        var product=Product.Create("DEMO","Demo","Hammadde","Kilogram","KUMAS",values);
        values["gramaj"]="999";Assert.Equal("180",product.Attributes["gramaj"]);Assert.Equal("KUMAS",product.MaterialGroupCode);
        Assert.Throws<NotSupportedException>(()=>((IDictionary<string,string>)product.Attributes).Add("en","150"));
    }
    [Fact] public void Rehydrate_preserves_identity_status_and_attributes()
    {
        var id=Guid.NewGuid();var product=Product.Rehydrate(id,"D","Demo","Hammadde","Metre",false,"KUMAS",new Dictionary<string,string>{{"en","150"}});
        Assert.Equal(id,product.Id);Assert.False(product.IsActive);Assert.Equal("150",product.Attributes["en"]);
    }
    [Fact] public void Missing_group_is_not_invented_for_attribute_values()
    {Assert.Throws<ArgumentException>(()=>Product.Create("D","Demo","Hammadde","Metre",attributes:new Dictionary<string,string>{{"gramaj","180"}}));}
    [Fact] public void Legacy_product_constructor_remains_compatible()
    {var product=Product.Create("D","Demo","Mamül","Adet");Assert.Null(product.MaterialGroupCode);Assert.Empty(product.Attributes);}
}

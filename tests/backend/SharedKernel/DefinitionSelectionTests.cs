using SharedKernel.Application;
using Xunit;

namespace MiniErp.SharedKernel.Tests;

public sealed class DefinitionSelectionTests
{
    private sealed class Repository : IDefinitionRepository
    {
        public IReadOnlyList<DefinitionCategory> Categories {get;init;}=[new("custom-variants","Varyantlar",true,"product-lifecycle","model-variant")];
        public IReadOnlyList<DefinitionItem> Items {get;init;}=[new("custom-variants","NAVY","Lacivert",true)];
        public string? RequestedCategory {get;private set;}
        public Task<IReadOnlyList<DefinitionCategory>> CategoriesAsync(CancellationToken ct)=>Task.FromResult(Categories);
        public Task<IReadOnlyList<DefinitionItem>> ItemsAsync(string category,CancellationToken ct){RequestedCategory=category;return Task.FromResult(Items);}
        public Task<DefinitionCategory> CreateCategoryAsync(string code,string name,string module,string feature,CancellationToken ct)=>throw new NotSupportedException();
        public Task<DefinitionItem> CreateItemAsync(string category,string code,string name,bool active,CancellationToken ct)=>throw new NotSupportedException();
    }
    [Fact] public async Task Resolves_custom_bound_category_and_returns_canonical_name()
    {
        var repository=new Repository();
        var selected=await DefinitionSelection.ResolveAsync(repository,"product-lifecycle","model-variant"," NAVY ",default);
        Assert.Equal("custom-variants",repository.RequestedCategory);Assert.Equal("Lacivert",selected.Name);
    }
    [Fact] public async Task Inactive_item_cannot_be_attached_to_a_model()
    {
        var repository=new Repository{Items=[new("custom-variants","NAVY","Lacivert",false)]};
        await Assert.ThrowsAsync<ArgumentException>(()=>DefinitionSelection.ResolveAsync(repository,"product-lifecycle","model-variant","NAVY",default));
    }
    [Fact] public async Task Other_feature_catalogue_is_not_used_as_fallback()
    {
        await Assert.ThrowsAsync<ArgumentException>(()=>DefinitionSelection.ResolveAsync(new Repository(),"product-lifecycle","model-size","NAVY",default));
    }
    [Fact] public async Task Undefined_code_is_rejected_even_if_a_category_exists()
    {
        await Assert.ThrowsAsync<ArgumentException>(()=>DefinitionSelection.ResolveAsync(new Repository(),"product-lifecycle","model-variant","MISSING",default));
    }
    [Fact] public void Both_model_axes_are_permitted_definition_bindings()
    {
        Assert.Equal(("product-lifecycle","model-variant"),DefinitionTargets.Validate("product-lifecycle","model-variant"));
        Assert.Equal(("product-lifecycle","model-size"),DefinitionTargets.Validate("product-lifecycle","model-size"));
    }
}

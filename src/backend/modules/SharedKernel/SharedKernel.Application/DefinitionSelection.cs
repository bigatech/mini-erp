namespace SharedKernel.Application;

/// <summary>Resolves an active catalogue value by its module/feature binding, not by a UI category name.</summary>
public static class DefinitionSelection
{
    public static async Task<DefinitionItem> ResolveAsync(IDefinitionRepository repository,string module,string feature,string? code,CancellationToken ct)
    {
        code=DefinitionInput.Required(code,"code");
        var category=(await repository.CategoriesAsync(ct)).SingleOrDefault(c=>c.IsActive&&c.ModuleCode==module&&c.FeatureCode==feature)
            ?? throw new ArgumentException("İlgili alanın tanım kategorisi bulunamadı. Önce Tanımlar ekranından tanımlayın.","code");
        return (await repository.ItemsAsync(category.Code,ct)).SingleOrDefault(i=>i.IsActive&&i.Code==code)
            ?? throw new ArgumentException("Aktif tanımlı listeden bir değer seçin veya yeni değeri önce Tanımlar'a ekleyin.","code");
    }
}

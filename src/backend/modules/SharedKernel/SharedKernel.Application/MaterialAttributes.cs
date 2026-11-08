using System.Globalization;
namespace SharedKernel.Application;

public sealed record MaterialAttributeDefinition(string Code,string Name,string DataType,string? Unit);
public sealed record MaterialGroupDto(string Code,string Name,IReadOnlyList<MaterialAttributeDefinition> Fields);
public interface IMaterialAttributeCatalog
{
    Task<IReadOnlyList<MaterialGroupDto>> ListAsync(CancellationToken ct);
    Task ValidateAsync(string? groupCode,IReadOnlyDictionary<string,string>? attributes,CancellationToken ct);
}
public interface IMaterialAttributeWriter
{
    Task<MaterialGroupDto> UpdateGroupAsync(string code,string name,CancellationToken ct)=>throw new NotSupportedException();
    Task DeleteGroupAsync(string code,CancellationToken ct)=>throw new NotSupportedException();
    Task<MaterialAttributeDefinition> UpdateFieldAsync(string group,string code,string name,string? unit,CancellationToken ct)=>throw new NotSupportedException();
    Task DeleteFieldAsync(string group,string code,CancellationToken ct)=>throw new NotSupportedException();
    Task<MaterialGroupDto> CreateGroupAsync(string code,string name,CancellationToken ct);
    Task<MaterialAttributeDefinition> CreateFieldAsync(string group,string code,string name,string dataType,string? unit,CancellationToken ct);
}
public static class MaterialAttributeRules
{
    public static string Code(string? code,string field="code")
    {
        var value=DefinitionInput.Required(code,field);
        if(value.Length>80||!value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'))throw new ArgumentException("Kod yalnız harf, rakam, tire ve alt çizgi içermelidir.",field);
        return value;
    }
    public static void Validate(MaterialGroupDto? group,string? groupCode,IReadOnlyDictionary<string,string>? attributes)
    {
        if(string.IsNullOrWhiteSpace(groupCode))
        {
            if(attributes?.Any(a=>!string.IsNullOrWhiteSpace(a.Value))==true)throw new ArgumentException("Öznitelik girmek için malzeme grubu seçin.","materialGroupCode");
            return;
        }
        if(group is null)throw new ArgumentException("Malzeme grubu bulunamadı.","materialGroupCode");
        foreach(var entry in attributes??new Dictionary<string,string>())
        {
            var field=group.Fields.SingleOrDefault(f=>f.Code==entry.Key)??throw new ArgumentException("Bu grupta tanımlı olmayan öznitelik gönderildi.",entry.Key);
            var text=entry.Value?.Trim();
            if(string.IsNullOrEmpty(text))continue; // Missing is not zero.
            if(text.Length>2000||text.Contains('\0'))throw new ArgumentException("Öznitelik değeri geçersiz veya 2000 karakterden uzun.",entry.Key);
            if(field.DataType=="number"&&(!decimal.TryParse(text,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var number)||number<=0))throw new ArgumentException("Sayısal öznitelik pozitif olmalı; ondalık ayracı nokta kullanın.",entry.Key);
        }
    }
}

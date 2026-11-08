using Npgsql;
using NpgsqlTypes;
using ProductLifecycle.Application;
using ProductLifecycle.Domain;
namespace ProductLifecycle.Infrastructure;

public sealed partial class PostgresPlmDraftRepository
{
    public async Task<IReadOnlyList<ModelOptionDto>> OptionsAsync(Guid modelId,CancellationToken ct)
    {
        await Check("model","model_id",modelId,ct);
        await using var c=source.CreateCommand("SELECT option_id,model_id,kind,code,name,quantity FROM product_lifecycle.model_configuration_option WHERE model_id=@id ORDER BY kind,code");
        c.Parameters.AddWithValue("id",modelId);
        await using var r=await c.ExecuteReaderAsync(ct);var result=new List<ModelOptionDto>();
        while(await r.ReadAsync(ct))result.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDecimal(5)));
        return result;
    }
    public async Task<ModelOptionDto> CreateOptionAsync(Guid modelId,string kind,string code,string name,decimal quantity,CancellationToken ct)
    {
        kind=PlmDraftRules.Option(kind,quantity);code=PlmDraftRules.Required(code,"code");name=PlmDraftRules.Required(name,"name");
        var id=Guid.NewGuid();await using var c=source.CreateCommand("INSERT INTO product_lifecycle.model_configuration_option(option_id,model_id,kind,code,name,quantity) VALUES(@id,@model,@kind,@code,@name,@quantity)");
        c.Parameters.AddWithValue("id",id);c.Parameters.AddWithValue("model",modelId);c.Parameters.AddWithValue("kind",kind);c.Parameters.AddWithValue("code",code);c.Parameters.AddWithValue("name",name);c.Parameters.AddWithValue("quantity",quantity);
        await Execute(c,ct);return new(id,modelId,kind,code,name,quantity);
    }
    public async Task<bool> ExistsSelectionAsync(Guid revisionId,Guid variantOptionId,Guid sizeOptionId,CancellationToken ct)
    {
        await using var c=source.CreateCommand("""
        SELECT EXISTS(SELECT 1 FROM product_lifecycle.model_revizyon r
        JOIN product_lifecycle.model_configuration_option v ON v.model_id=r.model_id AND v.kind='variant' AND v.option_id=@variant
        JOIN product_lifecycle.model_configuration_option s ON s.model_id=r.model_id AND s.kind='size' AND s.option_id=@size
        WHERE r.model_revizyon_id=@revision)
        """);
        c.Parameters.AddWithValue("revision",revisionId);c.Parameters.AddWithValue("variant",variantOptionId);c.Parameters.AddWithValue("size",sizeOptionId);
        return (bool)(await c.ExecuteScalarAsync(ct))!;
    }
    public async Task<bool> ExistsSetPartAsync(Guid revisionId,Guid setPartOptionId,CancellationToken ct)
    {
        await using var c=source.CreateCommand("""
        SELECT EXISTS(SELECT 1 FROM product_lifecycle.model_revizyon r
        JOIN product_lifecycle.model_configuration_option p ON p.model_id=r.model_id AND p.kind='set-part' AND p.option_id=@part
        WHERE r.model_revizyon_id=@revision)
        """);
        c.Parameters.AddWithValue("revision",revisionId);c.Parameters.AddWithValue("part",setPartOptionId);
        return (bool)(await c.ExecuteScalarAsync(ct))!;
    }
    public async Task<bool> HasSetPartsAsync(Guid revisionId,CancellationToken ct)
    {
        await using var c=source.CreateCommand("""
        SELECT EXISTS(SELECT 1 FROM product_lifecycle.model_revizyon r
        JOIN product_lifecycle.model_configuration_option p ON p.model_id=r.model_id AND p.kind='set-part'
        WHERE r.model_revizyon_id=@revision)
        """);
        c.Parameters.AddWithValue("revision",revisionId);
        return (bool)(await c.ExecuteScalarAsync(ct))!;
    }
    private static Guid? OptionalGuid(NpgsqlDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetGuid(index);
    private static void AddScope(NpgsqlCommand c,Guid? parent,Guid? variant,Guid? size,Guid? part)
    {
        c.Parameters.Add("parent",NpgsqlDbType.Uuid).Value=(object?)parent??DBNull.Value;
        c.Parameters.Add("variant",NpgsqlDbType.Uuid).Value=(object?)variant??DBNull.Value;
        c.Parameters.Add("size",NpgsqlDbType.Uuid).Value=(object?)size??DBNull.Value;
        c.Parameters.Add("part",NpgsqlDbType.Uuid).Value=(object?)part??DBNull.Value;
    }
    private static async Task<(Guid? Variant,Guid? Size,Guid? Part)> ValidateScopeAsync(NpgsqlConnection connection,NpgsqlTransaction tx,Guid revision,Guid product,Guid? parent,Guid? variant,Guid? size,Guid? part,CancellationToken ct)
    {
        if(parent is not null)
        {
            await using var p=new NpgsqlCommand("SELECT l.variant_option_id,l.size_option_id,l.set_part_option_id FROM product_lifecycle.urun_agaci_kalem l JOIN product_lifecycle.urun_agaci b ON b.urun_agaci_id=l.urun_agaci_id WHERE l.urun_agaci_kalem_id=@parent AND b.model_revizyon_id=@revision",connection,tx);
            p.Parameters.AddWithValue("parent",parent.Value);p.Parameters.AddWithValue("revision",revision);
            await using var r=await p.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new ArgumentException("Üst bileşen aynı revizyona ait olmalıdır.","parentLineId");
            variant=Inherit(variant,OptionalGuid(r,0),"variantOptionId");size=Inherit(size,OptionalGuid(r,1),"sizeOptionId");part=Inherit(part,OptionalGuid(r,2),"setPartOptionId");
        }
        // A BOM scope must belong to this revision's model and have the requested axis kind.
        foreach(var axis in new[]{(Id:variant,Kind:"variant"),(Id:size,Kind:"size"),(Id:part,Kind:"set-part")})
        {
            if(axis.Id is null)continue;
            await using var option=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM product_lifecycle.model_configuration_option o JOIN product_lifecycle.model_revizyon r ON r.model_id=o.model_id WHERE r.model_revizyon_id=@revision AND o.option_id=@option AND o.kind=@kind)",connection,tx);
            option.Parameters.AddWithValue("revision",revision);option.Parameters.AddWithValue("option",axis.Id.Value);option.Parameters.AddWithValue("kind",axis.Kind);
            if(!(bool)(await option.ExecuteScalarAsync(ct))!)throw new ArgumentException("Seçenek bu modelin ilgili varyant/beden/set alanına ait değil.",axis.Kind);
        }
        if(parent is null)return (variant,size,part);
        await using var cycle=new NpgsqlCommand("""
        WITH RECURSIVE ancestors AS(
          SELECT urun_agaci_kalem_id,parent_line_id,bilesen_urun_id FROM product_lifecycle.urun_agaci_kalem WHERE urun_agaci_kalem_id=@parent
          UNION
          SELECT l.urun_agaci_kalem_id,l.parent_line_id,l.bilesen_urun_id FROM product_lifecycle.urun_agaci_kalem l JOIN ancestors a ON a.parent_line_id=l.urun_agaci_kalem_id)
        SELECT EXISTS(SELECT 1 FROM ancestors WHERE bilesen_urun_id=@product)
        """,connection,tx);
        cycle.Parameters.AddWithValue("parent",parent.Value);cycle.Parameters.AddWithValue("product",product);
        if((bool)(await cycle.ExecuteScalarAsync(ct))!)throw new ArgumentException("Aynı ürün kendi alt ağacına eklenemez; BOM döngüsü engellendi.","componentProductId");
        return (variant,size,part);
    }
    private static Guid? Inherit(Guid? child,Guid? parent,string field)
    {
        if(parent.HasValue && child.HasValue && child!=parent)throw new ArgumentException("Alt bileşenin kapsamı üst bileşenle çelişemez.",field);
        return child??parent;
    }
}

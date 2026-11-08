using System.Text.Json;
using NpgsqlTypes;
using Npgsql;
using ProductLifecycle.Application;
namespace ProductLifecycle.Infrastructure;
public sealed partial class PostgresPlmDraftRepository(NpgsqlDataSource source):IPlmDraftRepository,IPlmConfigurationReader
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var c=source.CreateCommand("""
        CREATE TABLE IF NOT EXISTS product_lifecycle.model(model_id uuid PRIMARY KEY,model_kodu text NOT NULL UNIQUE,model_adi text NOT NULL,durum text NOT NULL DEFAULT 'Draft');
        CREATE TABLE IF NOT EXISTS product_lifecycle.model_revizyon(model_revizyon_id uuid PRIMARY KEY,model_id uuid NOT NULL REFERENCES product_lifecycle.model(model_id),revizyon_no text NOT NULL,onay_durumu text NOT NULL DEFAULT 'Draft',UNIQUE(model_id,revizyon_no));
        CREATE TABLE IF NOT EXISTS product_lifecycle.urun_agaci(urun_agaci_id uuid PRIMARY KEY,model_revizyon_id uuid NOT NULL REFERENCES product_lifecycle.model_revizyon(model_revizyon_id),revizyon_no text NOT NULL,durum text NOT NULL DEFAULT 'Draft',UNIQUE(model_revizyon_id,revizyon_no));
        CREATE TABLE IF NOT EXISTS product_lifecycle.urun_agaci_kalem(urun_agaci_kalem_id uuid PRIMARY KEY,urun_agaci_id uuid NOT NULL REFERENCES product_lifecycle.urun_agaci(urun_agaci_id),bilesen_urun_id uuid NOT NULL REFERENCES product_lifecycle.urun(urun_id),miktar numeric NOT NULL CHECK(miktar>0),birim text NOT NULL,fire_orani numeric NOT NULL CHECK(fire_orani BETWEEN 0 AND 1));
        CREATE TABLE IF NOT EXISTS product_lifecycle.model_configuration_option(
          option_id uuid PRIMARY KEY,model_id uuid NOT NULL REFERENCES product_lifecycle.model(model_id),
          kind text NOT NULL CHECK(kind IN ('variant','size','set-part')),code text NOT NULL,name text NOT NULL,
          quantity numeric NOT NULL DEFAULT 1 CHECK(quantity>0),UNIQUE(model_id,kind,code));
        ALTER TABLE product_lifecycle.urun_agaci_kalem ADD COLUMN IF NOT EXISTS parent_line_id uuid REFERENCES product_lifecycle.urun_agaci_kalem(urun_agaci_kalem_id);
        ALTER TABLE product_lifecycle.urun_agaci_kalem ADD COLUMN IF NOT EXISTS variant_option_id uuid REFERENCES product_lifecycle.model_configuration_option(option_id);
        ALTER TABLE product_lifecycle.urun_agaci_kalem ADD COLUMN IF NOT EXISTS size_option_id uuid REFERENCES product_lifecycle.model_configuration_option(option_id);
        ALTER TABLE product_lifecycle.urun_agaci_kalem ADD COLUMN IF NOT EXISTS set_part_option_id uuid REFERENCES product_lifecycle.model_configuration_option(option_id);
        ALTER TABLE product_lifecycle.urun_agaci_kalem ADD COLUMN IF NOT EXISTS line_label text;
        ALTER TABLE product_lifecycle.urun_agaci_kalem ADD COLUMN IF NOT EXISTS material_group_code text;
        ALTER TABLE product_lifecycle.urun_agaci_kalem ADD COLUMN IF NOT EXISTS attributes jsonb NOT NULL DEFAULT '{}'::jsonb;
        CREATE TABLE IF NOT EXISTS product_lifecycle.bom_line_event(event_id uuid PRIMARY KEY,revision_id uuid NOT NULL REFERENCES product_lifecycle.model_revizyon(model_revizyon_id),line_id uuid NOT NULL,event_type text NOT NULL,previous_line jsonb NOT NULL,occurred_at timestamptz NOT NULL DEFAULT now());
        CREATE INDEX IF NOT EXISTS ix_bom_parent_line ON product_lifecycle.urun_agaci_kalem(parent_line_id);
        CREATE INDEX IF NOT EXISTS ix_bom_revision ON product_lifecycle.urun_agaci(model_revizyon_id);
        CREATE INDEX IF NOT EXISTS ix_bom_lines_parent ON product_lifecycle.urun_agaci_kalem(urun_agaci_id);
        CREATE INDEX IF NOT EXISTS ix_bom_lines_component ON product_lifecycle.urun_agaci_kalem(bilesen_urun_id);
        """);
        await c.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<ModelDto>> ModelsAsync(CancellationToken ct)
    {
        await using var c=source.CreateCommand("SELECT model_id,model_kodu,model_adi,durum FROM product_lifecycle.model ORDER BY model_kodu");
        await using var r=await c.ExecuteReaderAsync(ct);var rows=new List<ModelDto>();while(await r.ReadAsync(ct)) rows.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3)));return rows;
    }
    public async Task<ModelDto> CreateModelAsync(string code,string name,CancellationToken ct)
    {
        var id=Guid.NewGuid();await using var c=source.CreateCommand("INSERT INTO product_lifecycle.model(model_id,model_kodu,model_adi) VALUES(@id,@code,@name)");c.Parameters.AddWithValue("id",id);c.Parameters.AddWithValue("code",code);c.Parameters.AddWithValue("name",name);await Execute(c,ct);return new(id,code,name,"Draft");
    }
    public async Task<IReadOnlyList<RevisionDto>> RevisionsAsync(Guid modelId,CancellationToken ct)
    {
        await Check("model","model_id",modelId,ct);
        await using var c=source.CreateCommand("SELECT model_revizyon_id,model_id,revizyon_no,onay_durumu FROM product_lifecycle.model_revizyon WHERE model_id=@id ORDER BY revizyon_no");c.Parameters.AddWithValue("id",modelId);await using var r=await c.ExecuteReaderAsync(ct);var rows=new List<RevisionDto>();while(await r.ReadAsync(ct))rows.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3)));return rows;
    }
    public async Task<RevisionDto> CreateRevisionAsync(Guid modelId,string number,CancellationToken ct)
    {
        var id=Guid.NewGuid();await using var c=source.CreateCommand("INSERT INTO product_lifecycle.model_revizyon(model_revizyon_id,model_id,revizyon_no) VALUES(@id,@model,@number)");c.Parameters.AddWithValue("id",id);c.Parameters.AddWithValue("model",modelId);c.Parameters.AddWithValue("number",number);await Execute(c,ct);return new(id,modelId,number,"Draft");
    }
    public async Task<IReadOnlyList<BomLineDto>> BomAsync(Guid revisionId,CancellationToken ct)
    {
        await Check("model_revizyon","model_revizyon_id",revisionId,ct);
        await using var c=source.CreateCommand("SELECT l.urun_agaci_kalem_id,b.model_revizyon_id,u.urun_id,u.urun_kodu,u.urun_adi,l.birim,l.miktar,l.fire_orani,l.parent_line_id,l.variant_option_id,l.size_option_id,l.set_part_option_id,l.line_label,l.material_group_code,l.attributes::text FROM product_lifecycle.urun_agaci_kalem l JOIN product_lifecycle.urun_agaci b ON b.urun_agaci_id=l.urun_agaci_id JOIN product_lifecycle.urun u ON u.urun_id=l.bilesen_urun_id WHERE b.model_revizyon_id=@id ORDER BY u.urun_kodu,l.urun_agaci_kalem_id");c.Parameters.AddWithValue("id",revisionId);await using var r=await c.ExecuteReaderAsync(ct);var rows=new List<BomLineDto>();while(await r.ReadAsync(ct))rows.Add(Line(r));return BomQuantityProjection.Apply(rows);
    }
    public async Task<BomLineDto> AddBomAsync(Guid revisionId,Guid productId,decimal quantity,decimal waste,CancellationToken ct,Guid? parentLineId=null,Guid? variantOptionId=null,Guid? sizeOptionId=null,Guid? setPartOptionId=null,string? label=null,string? materialGroupCode=null,IReadOnlyDictionary<string,string>? attributes=null)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        await using(var check=new NpgsqlCommand("SELECT onay_durumu FROM product_lifecycle.model_revizyon WHERE model_revizyon_id=@id FOR UPDATE",connection,tx))
        {check.Parameters.AddWithValue("id",revisionId);var state=await check.ExecuteScalarAsync(ct) as string;if(state is null)throw new PlmReferenceNotFoundException();if(state!="Draft")throw new InvalidOperationException("Yalnız taslak revizyon değiştirilebilir.");}
        var scope=await ValidateScopeAsync(connection,tx,revisionId,productId,parentLineId,variantOptionId,sizeOptionId,setPartOptionId,ct);
        variantOptionId=scope.Variant;sizeOptionId=scope.Size;setPartOptionId=scope.Part;
        var bomId=Guid.NewGuid();await using(var head=new NpgsqlCommand("INSERT INTO product_lifecycle.urun_agaci(urun_agaci_id,model_revizyon_id,revizyon_no) SELECT @id,model_revizyon_id,revizyon_no FROM product_lifecycle.model_revizyon WHERE model_revizyon_id=@revision ON CONFLICT(model_revizyon_id,revizyon_no) DO UPDATE SET revizyon_no=excluded.revizyon_no RETURNING urun_agaci_id",connection,tx))
        {head.Parameters.AddWithValue("id",bomId);head.Parameters.AddWithValue("revision",revisionId);bomId=(Guid)(await head.ExecuteScalarAsync(ct))!;}
        var lineId=Guid.NewGuid();await using var c=new NpgsqlCommand("""
        WITH line AS(INSERT INTO product_lifecycle.urun_agaci_kalem(urun_agaci_kalem_id,urun_agaci_id,bilesen_urun_id,miktar,birim,fire_orani,parent_line_id,variant_option_id,size_option_id,set_part_option_id,line_label,material_group_code,attributes)
        SELECT @id,@bom,urun_id,@quantity,stok_birimi,@waste,@parent,@variant,@size,@part,@label,CASE WHEN @inherit THEN material_group_code ELSE @group END,CASE WHEN @inherit THEN attributes ELSE @attributes END FROM product_lifecycle.urun WHERE urun_id=@product AND aktif_mi RETURNING *)
        SELECT l.urun_agaci_kalem_id,@revision,u.urun_id,u.urun_kodu,u.urun_adi,l.birim,l.miktar,l.fire_orani,l.parent_line_id,l.variant_option_id,l.size_option_id,l.set_part_option_id,l.line_label,l.material_group_code,l.attributes::text FROM line l JOIN product_lifecycle.urun u ON u.urun_id=l.bilesen_urun_id
        """,connection,tx);AddDetails(c,label,materialGroupCode,attributes);c.Parameters.AddWithValue("inherit",materialGroupCode is null && attributes is null);AddScope(c,parentLineId,variantOptionId,sizeOptionId,setPartOptionId);c.Parameters.AddWithValue("id",lineId);c.Parameters.AddWithValue("bom",bomId);c.Parameters.AddWithValue("product",productId);c.Parameters.AddWithValue("revision",revisionId);c.Parameters.AddWithValue("quantity",quantity);c.Parameters.AddWithValue("waste",waste);await using var reader=await c.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new PlmReferenceNotFoundException();var result=Line(reader);await reader.CloseAsync();await tx.CommitAsync(ct);return result;
    }
    private static BomLineDto Line(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDecimal(6),r.GetDecimal(7),OptionalGuid(r,8),OptionalGuid(r,9),OptionalGuid(r,10),OptionalGuid(r,11),r.IsDBNull(12)?null:r.GetString(12),r.IsDBNull(13)?null:r.GetString(13),JsonSerializer.Deserialize<Dictionary<string,string>>(r.GetString(14)));
    private async Task Check(string table,string column,Guid id,CancellationToken ct)
    {await using var c=source.CreateCommand($"SELECT EXISTS(SELECT 1 FROM product_lifecycle.{table} WHERE {column}=@id)");c.Parameters.AddWithValue("id",id);if(!(bool)(await c.ExecuteScalarAsync(ct))!)throw new PlmReferenceNotFoundException();}
    private static async Task Execute(NpgsqlCommand c,CancellationToken ct)
    {try{await c.ExecuteNonQueryAsync(ct);}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.UniqueViolation){throw new PlmDuplicateException();}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.ForeignKeyViolation){throw new PlmReferenceNotFoundException();}}
}

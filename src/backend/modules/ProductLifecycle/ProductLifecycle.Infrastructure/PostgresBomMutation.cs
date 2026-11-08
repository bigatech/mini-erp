using Npgsql;
using NpgsqlTypes;
using ProductLifecycle.Application;
using ProductLifecycle.Domain;
using System.Text.Json;
namespace ProductLifecycle.Infrastructure;
public sealed partial class PostgresPlmDraftRepository
{
    private static void AddDetails(NpgsqlCommand command,string? label,string? group,IReadOnlyDictionary<string,string>? attributes)
    {
        command.Parameters.Add("label",NpgsqlDbType.Text).Value=(object?)BomLineEditRules.Label(label)??DBNull.Value;
        command.Parameters.Add("group",NpgsqlDbType.Text).Value=(object?)group??DBNull.Value;
        command.Parameters.Add("attributes",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(attributes??new Dictionary<string,string>());
    }
    private static async Task LockBomRevision(NpgsqlConnection connection,NpgsqlTransaction tx,Guid revision,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("SELECT onay_durumu FROM product_lifecycle.model_revizyon WHERE model_revizyon_id=@id FOR UPDATE",connection,tx);
        command.Parameters.AddWithValue("id",revision);var status=await command.ExecuteScalarAsync(ct) as string;
        if(status is null)throw new PlmReferenceNotFoundException();
        if(status!="Draft")throw new InvalidOperationException("Yalnız taslak revizyon değiştirilebilir.");
    }
    private static async Task<BomLineDto> MutableLine(NpgsqlConnection connection,NpgsqlTransaction tx,Guid revision,Guid line,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("""
          SELECT l.urun_agaci_kalem_id,b.model_revizyon_id,u.urun_id,u.urun_kodu,u.urun_adi,l.birim,l.miktar,l.fire_orani,l.parent_line_id,l.variant_option_id,l.size_option_id,l.set_part_option_id,l.line_label,l.material_group_code,l.attributes::text
          FROM product_lifecycle.urun_agaci_kalem l JOIN product_lifecycle.urun_agaci b ON b.urun_agaci_id=l.urun_agaci_id JOIN product_lifecycle.urun u ON u.urun_id=l.bilesen_urun_id
          WHERE l.urun_agaci_kalem_id=@line AND b.model_revizyon_id=@revision
          """,connection,tx);
        command.Parameters.AddWithValue("line",line);command.Parameters.AddWithValue("revision",revision);
        await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new PlmReferenceNotFoundException();return Line(reader);
    }
    private static async Task AuditLine(NpgsqlConnection connection,NpgsqlTransaction tx,BomLineDto line,string action,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("INSERT INTO product_lifecycle.bom_line_event(event_id,revision_id,line_id,event_type,previous_line) VALUES(@id,@revision,@line,@action,@previous)",connection,tx);
        command.Parameters.AddWithValue("id",Guid.NewGuid());command.Parameters.AddWithValue("revision",line.RevisionId);command.Parameters.AddWithValue("line",line.Id);command.Parameters.AddWithValue("action",action);command.Parameters.Add("previous",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(line);await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<BomLineDto> UpdateBomAsync(Guid revisionId,Guid lineId,decimal quantity,decimal waste,string? label,string? materialGroupCode,IReadOnlyDictionary<string,string>? attributes,CancellationToken ct)
    {
        PlmDraftRules.Bom(quantity,waste);label=BomLineEditRules.Label(label);
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        await LockBomRevision(connection,tx,revisionId,ct);var old=await MutableLine(connection,tx,revisionId,lineId,ct);
        await AuditLine(connection,tx,old,"Updated",ct);
        await using(var command=new NpgsqlCommand("UPDATE product_lifecycle.urun_agaci_kalem SET miktar=@quantity,fire_orani=@waste,line_label=@label,material_group_code=@group,attributes=@attributes WHERE urun_agaci_kalem_id=@id",connection,tx))
        {command.Parameters.AddWithValue("id",lineId);command.Parameters.AddWithValue("quantity",quantity);command.Parameters.AddWithValue("waste",waste);AddDetails(command,label,materialGroupCode,attributes);await command.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);return old with{Quantity=quantity,WasteRate=waste,Label=label,MaterialGroupCode=materialGroupCode,Attributes=attributes??new Dictionary<string,string>()};
    }
    public async Task DeleteBomAsync(Guid revisionId,Guid lineId,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        await LockBomRevision(connection,tx,revisionId,ct);var old=await MutableLine(connection,tx,revisionId,lineId,ct);
        await using(var children=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM product_lifecycle.urun_agaci_kalem WHERE parent_line_id=@id)",connection,tx))
        {children.Parameters.AddWithValue("id",lineId);if((bool)(await children.ExecuteScalarAsync(ct))!)throw new BomHasChildrenException();}
        await AuditLine(connection,tx,old,"Deleted",ct);
        await using(var remove=new NpgsqlCommand("DELETE FROM product_lifecycle.urun_agaci_kalem WHERE urun_agaci_kalem_id=@id",connection,tx))
        {remove.Parameters.AddWithValue("id",lineId);await remove.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);
    }
}

using Npgsql;
using NpgsqlTypes;
using ProductLifecycle.Application;
using ProductLifecycle.Domain;
namespace ProductLifecycle.Infrastructure;

public sealed class PostgresRevisionLifecycleRepository(NpgsqlDataSource source):IRevisionLifecycleRepository
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command=source.CreateCommand("""
          CREATE TABLE IF NOT EXISTS product_lifecycle.revision_lifecycle_event(
            event_id uuid PRIMARY KEY,revision_id uuid NOT NULL REFERENCES product_lifecycle.model_revizyon(model_revizyon_id),
            from_status text,to_status text NOT NULL,source_revision_id uuid REFERENCES product_lifecycle.model_revizyon(model_revizyon_id),
            occurred_at timestamptz NOT NULL DEFAULT now());
          CREATE INDEX IF NOT EXISTS ix_revision_lifecycle_event ON product_lifecycle.revision_lifecycle_event(revision_id,occurred_at);
          """);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<RevisionDto> TransitionAsync(Guid revisionId,string action,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        var revision=await Lock(connection,tx,revisionId,ct);var target=RevisionLifecycleRules.Next(revision.Status,action);
        await using(var update=new NpgsqlCommand("UPDATE product_lifecycle.model_revizyon SET onay_durumu=@state WHERE model_revizyon_id=@id; UPDATE product_lifecycle.urun_agaci SET durum=@state WHERE model_revizyon_id=@id",connection,tx))
        {
            update.Parameters.AddWithValue("id",revisionId);update.Parameters.AddWithValue("state",target);await update.ExecuteNonQueryAsync(ct);
        }
        await Event(connection,tx,revisionId,revision.Status,target,null,ct);await tx.CommitAsync(ct);
        return revision with{Status=target};
    }
    public async Task<RevisionDto> CloneAsync(Guid revisionId,string newNumber,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        var sourceRevision=await Lock(connection,tx,revisionId,ct);RevisionLifecycleRules.Clone(sourceRevision.Status);
        var newId=Guid.NewGuid();
        try
        {
            await using(var create=new NpgsqlCommand("INSERT INTO product_lifecycle.model_revizyon(model_revizyon_id,model_id,revizyon_no,onay_durumu) VALUES(@id,@model,@number,'Draft')",connection,tx))
            {
                create.Parameters.AddWithValue("id",newId);create.Parameters.AddWithValue("model",sourceRevision.ModelId);create.Parameters.AddWithValue("number",newNumber);await create.ExecuteNonQueryAsync(ct);
            }
            var headers=new Dictionary<Guid,Guid>();
            await using(var read=new NpgsqlCommand("SELECT urun_agaci_id FROM product_lifecycle.urun_agaci WHERE model_revizyon_id=@id",connection,tx))
            {
                read.Parameters.AddWithValue("id",revisionId);await using var reader=await read.ExecuteReaderAsync(ct);
                while(await reader.ReadAsync(ct))headers.Add(reader.GetGuid(0),Guid.NewGuid());
            }
            foreach(var header in headers)
            {
                await using var create=new NpgsqlCommand("INSERT INTO product_lifecycle.urun_agaci(urun_agaci_id,model_revizyon_id,revizyon_no,durum) VALUES(@id,@revision,@number,'Draft')",connection,tx);
                create.Parameters.AddWithValue("id",header.Value);create.Parameters.AddWithValue("revision",newId);create.Parameters.AddWithValue("number",newNumber);await create.ExecuteNonQueryAsync(ct);
            }
            var lines=new List<CloneLine>();
            await using(var read=new NpgsqlCommand("""
              SELECT l.urun_agaci_kalem_id,l.urun_agaci_id,l.bilesen_urun_id,l.miktar,l.birim,l.fire_orani,l.parent_line_id,l.variant_option_id,l.size_option_id,l.set_part_option_id,l.line_label,l.material_group_code,l.attributes::text
              FROM product_lifecycle.urun_agaci_kalem l JOIN product_lifecycle.urun_agaci b ON b.urun_agaci_id=l.urun_agaci_id WHERE b.model_revizyon_id=@id
              """,connection,tx))
            {
                read.Parameters.AddWithValue("id",revisionId);await using var reader=await read.ExecuteReaderAsync(ct);
                while(await reader.ReadAsync(ct))lines.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetDecimal(3),reader.GetString(4),reader.GetDecimal(5),Optional(reader,6),Optional(reader,7),Optional(reader,8),Optional(reader,9),reader.IsDBNull(10)?null:reader.GetString(10),reader.IsDBNull(11)?null:reader.GetString(11),reader.GetString(12)));
            }
            var lineIds=lines.ToDictionary(l=>l.Id,_=>Guid.NewGuid());
            // First insert independent rows. A second pass remaps every parent, so arbitrary read order cannot leave source-revision links.
            foreach(var line in lines)
            {
                await using var insert=new NpgsqlCommand("""
                  INSERT INTO product_lifecycle.urun_agaci_kalem(urun_agaci_kalem_id,urun_agaci_id,bilesen_urun_id,miktar,birim,fire_orani,variant_option_id,size_option_id,set_part_option_id,line_label,material_group_code,attributes)
                  VALUES(@id,@header,@product,@quantity,@unit,@waste,@variant,@size,@part,@label,@group,@attributes)
                  """,connection,tx);
                insert.Parameters.AddWithValue("id",lineIds[line.Id]);insert.Parameters.AddWithValue("header",headers[line.Header]);insert.Parameters.AddWithValue("product",line.Product);
                insert.Parameters.AddWithValue("quantity",line.Quantity);insert.Parameters.AddWithValue("unit",line.Unit);insert.Parameters.AddWithValue("waste",line.Waste);
                Parameter(insert,"variant",line.Variant);Parameter(insert,"size",line.Size);Parameter(insert,"part",line.Part);insert.Parameters.Add("label",NpgsqlDbType.Text).Value=(object?)line.Label??DBNull.Value;insert.Parameters.Add("group",NpgsqlDbType.Text).Value=(object?)line.Group??DBNull.Value;insert.Parameters.Add("attributes",NpgsqlDbType.Jsonb).Value=line.Attributes;await insert.ExecuteNonQueryAsync(ct);
            }
            foreach(var line in lines.Where(l=>l.Parent.HasValue))
            {
                if(!lineIds.TryGetValue(line.Parent!.Value,out var parentId))throw new InvalidDataException("BOM üst kalemi kaynak revizyonun dışında.");
                await using var update=new NpgsqlCommand("UPDATE product_lifecycle.urun_agaci_kalem SET parent_line_id=@parent WHERE urun_agaci_kalem_id=@id",connection,tx);
                update.Parameters.AddWithValue("parent",parentId);update.Parameters.AddWithValue("id",lineIds[line.Id]);await update.ExecuteNonQueryAsync(ct);
            }
            await using(var route=new NpgsqlCommand("""
              INSERT INTO product_lifecycle.operasyon_rotasi(rota_id,model_revizyon_id,operasyon_id,sira_no)
              SELECT gen_random_uuid(),@target,operasyon_id,sira_no FROM product_lifecycle.operasyon_rotasi WHERE model_revizyon_id=@source
              """,connection,tx))
            {route.Parameters.AddWithValue("target",newId);route.Parameters.AddWithValue("source",revisionId);await route.ExecuteNonQueryAsync(ct);}
            await Event(connection,tx,newId,null,"Draft",revisionId,ct);await tx.CommitAsync(ct);
            return new(newId,sourceRevision.ModelId,newNumber,"Draft");
        }
        catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.UniqueViolation){throw new PlmDuplicateException();}
    }
    private static async Task<RevisionDto> Lock(NpgsqlConnection connection,NpgsqlTransaction tx,Guid id,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("SELECT model_id,revizyon_no,onay_durumu FROM product_lifecycle.model_revizyon WHERE model_revizyon_id=@id FOR UPDATE",connection,tx);
        command.Parameters.AddWithValue("id",id);await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))throw new PlmReferenceNotFoundException();return new(id,reader.GetGuid(0),reader.GetString(1),reader.GetString(2));
    }
    private static async Task Event(NpgsqlConnection connection,NpgsqlTransaction tx,Guid id,string? from,string to,Guid? sourceId,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("INSERT INTO product_lifecycle.revision_lifecycle_event(event_id,revision_id,from_status,to_status,source_revision_id) VALUES(@event,@id,@from,@to,@source)",connection,tx);
        command.Parameters.AddWithValue("event",Guid.NewGuid());command.Parameters.AddWithValue("id",id);command.Parameters.Add("from",NpgsqlDbType.Text).Value=(object?)from??DBNull.Value;
        command.Parameters.AddWithValue("to",to);Parameter(command,"source",sourceId);await command.ExecuteNonQueryAsync(ct);
    }
    private static Guid? Optional(NpgsqlDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetGuid(index);
    private static void Parameter(NpgsqlCommand command,string name,Guid? value)=>command.Parameters.Add(name,NpgsqlDbType.Uuid).Value=(object?)value??DBNull.Value;
    private sealed record CloneLine(Guid Id,Guid Header,Guid Product,decimal Quantity,string Unit,decimal Waste,Guid? Parent,Guid? Variant,Guid? Size,Guid? Part,string? Label,string? Group,string Attributes);
}

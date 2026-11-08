using System.Text.Json;
using Cutting.Application;
using Npgsql;
using NpgsqlTypes;
using ProductLifecycle.Application;
using SalesDrafts.Application;
namespace Cutting.Infrastructure;

public sealed class PostgresCuttingStore(NpgsqlDataSource source, IOrderDraftRepository orders, ListProductsHandler products, IPlmDraftRepository plm) : ICuttingStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var cmd=source.CreateCommand("""
          CREATE SCHEMA IF NOT EXISTS cutting;
          CREATE SEQUENCE IF NOT EXISTS cutting.record_number_seq;
          CREATE TABLE IF NOT EXISTS cutting.record(
            id uuid PRIMARY KEY, order_id uuid NOT NULL REFERENCES sales_drafts.order_draft(id),
            number text NOT NULL UNIQUE, version integer NOT NULL CHECK(version>0),
            status text NOT NULL CHECK(status IN ('Draft','Planned','InProgress','Completed','Cancelled')), document jsonb NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
          CREATE TABLE IF NOT EXISTS cutting.material_group(
            id uuid PRIMARY KEY, record_id uuid NOT NULL REFERENCES cutting.record(id),
            material_id uuid NOT NULL REFERENCES product_lifecycle.urun(urun_id), unit text NOT NULL);
          CREATE TABLE IF NOT EXISTS cutting.plan(
            id uuid PRIMARY KEY, group_id uuid NOT NULL REFERENCES cutting.material_group(id), number text NOT NULL);
          CREATE TABLE IF NOT EXISTS cutting.plan_line(
            plan_id uuid NOT NULL REFERENCES cutting.plan(id),
            variant_option_id uuid NOT NULL REFERENCES product_lifecycle.model_configuration_option(option_id),
            size_option_id uuid NOT NULL REFERENCES product_lifecycle.model_configuration_option(option_id),
            set_part_option_id uuid REFERENCES product_lifecycle.model_configuration_option(option_id), pieces_per_layer numeric NOT NULL CHECK(pieces_per_layer>0 AND pieces_per_layer=trunc(pieces_per_layer)));
          CREATE TABLE IF NOT EXISTS cutting.actual(
            id uuid PRIMARY KEY, plan_id uuid NOT NULL REFERENCES cutting.plan(id), date date NOT NULL);
          CREATE TABLE IF NOT EXISTS cutting.actual_line(
            actual_id uuid NOT NULL REFERENCES cutting.actual(id),
            variant_option_id uuid NOT NULL REFERENCES product_lifecycle.model_configuration_option(option_id),
            size_option_id uuid NOT NULL REFERENCES product_lifecycle.model_configuration_option(option_id),
            set_part_option_id uuid REFERENCES product_lifecycle.model_configuration_option(option_id),
            cut_quantity numeric NOT NULL CHECK(cut_quantity>=0 AND cut_quantity=trunc(cut_quantity)), good_quantity numeric CHECK(good_quantity>=0 AND good_quantity=trunc(good_quantity)), defect_quantity numeric CHECK(defect_quantity>=0 AND defect_quantity=trunc(defect_quantity)),
            CHECK(coalesce(good_quantity,0)+coalesce(defect_quantity,0)<=cut_quantity),
            CHECK(good_quantity IS NULL OR defect_quantity IS NULL OR good_quantity+defect_quantity=cut_quantity));
          CREATE TABLE IF NOT EXISTS cutting.record_history(
            record_id uuid NOT NULL REFERENCES cutting.record(id), version integer NOT NULL,
            saved_at timestamptz NOT NULL, actor text, type text NOT NULL, document jsonb NOT NULL,
            PRIMARY KEY(record_id,version));
          ALTER TABLE cutting.plan ADD COLUMN IF NOT EXISTS planned_weight_kg numeric CHECK(planned_weight_kg>=0);
          ALTER TABLE cutting.actual ADD COLUMN IF NOT EXISTS net_weight_kg numeric CHECK(net_weight_kg>=0);
          CREATE UNIQUE INDEX IF NOT EXISTS ix_cutting_plan_line ON cutting.plan_line(plan_id,variant_option_id,size_option_id,set_part_option_id) NULLS NOT DISTINCT;
          CREATE UNIQUE INDEX IF NOT EXISTS ix_cutting_actual_line ON cutting.actual_line(actual_id,variant_option_id,size_option_id,set_part_option_id) NULLS NOT DISTINCT;
          CREATE INDEX IF NOT EXISTS ix_cutting_order ON cutting.record(order_id);
          """);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<object> OptionsAsync(CancellationToken ct)
    {
        var orderList=await orders.ListAsync(ct);
        var names=new Dictionary<Guid,string>();
        foreach(var model in await plm.ModelsAsync(ct))
            foreach(var option in await plm.OptionsAsync(model.Id,ct)) names[option.Id]=$"{option.Code} · {option.Name}";
        string Label(Guid id)=>names.GetValueOrDefault(id,id.ToString());
        return new { orders=orderList.Select(o=>new { o.Id,o.Number,o.Version,o.Status,o.RevisionId,
            lines=o.Lines.Select(l=>new{l.VariantOptionId,l.SizeOptionId,l.SetPartOptionId,l.Quantity,
                variantName=Label(l.VariantOptionId),sizeName=Label(l.SizeOptionId),setPartName=l.SetPartOptionId is {} p?Label(p):null}) }),
            materials=await products.Handle(ct) };
    }

    public async Task<IReadOnlyList<CuttingDocument>> ListAsync(CancellationToken ct)
    {
        await using var cmd=source.CreateCommand("SELECT document::text FROM cutting.record ORDER BY updated_at DESC,id");
        await using var r=await cmd.ExecuteReaderAsync(ct);var result=new List<CuttingDocument>();
        while(await r.ReadAsync(ct))result.Add(JsonSerializer.Deserialize<CuttingDocument>(r.GetString(0),Json)!);
        return result;
    }
    public async Task<CuttingDetail?> GetAsync(Guid id,CancellationToken ct)
    {
        await using var cmd=source.CreateCommand("SELECT document::text FROM cutting.record WHERE id=@id");
        cmd.Parameters.AddWithValue("id",id);
        if(await cmd.ExecuteScalarAsync(ct) is not string text)return null;
        return await DetailAsync(JsonSerializer.Deserialize<CuttingDocument>(text,Json)!,ct);
    }
    public async Task<IReadOnlyList<CuttingEvent>> EventsAsync(Guid id,CancellationToken ct)
    {
        await using var cmd=source.CreateCommand("SELECT version,saved_at,actor,type FROM cutting.record_history WHERE record_id=@id ORDER BY version");
        cmd.Parameters.AddWithValue("id",id);await using var r=await cmd.ExecuteReaderAsync(ct);var result=new List<CuttingEvent>();
        while(await r.ReadAsync(ct))result.Add(new(r.GetInt32(0),r.GetFieldValue<DateTimeOffset>(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3)));
        return result;
    }
    private async Task<CuttingDetail> DetailAsync(CuttingDocument document,CancellationToken ct)
    {
        var order=await orders.GetAsync(document.OrderId,ct);
        var sourceOrders=new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>();
        var report=CuttingCalculator.Calculate([document],sourceOrders,new(Status:document.Status));
        var planReports=CuttingCalculator.CalculatePlans(document,sourceOrders);
        return new(document,report,order?.Version??0,order is null||order.Version!=document.OrderVersion||order.Status=="Cancelled",order?.Status??"Missing",planReports);
    }
    public async Task<CuttingReport> ReportAsync(CuttingReportFilter filter,Guid? recordId,CancellationToken ct)
    {
        if(filter.From>filter.To)throw new CuttingStoreException(400,"Başlangıç tarihi bitişten sonra olamaz.");
        var documents=await ListAsync(ct);
        if(recordId is {} id)documents=documents.Where(d=>d.Id==id).ToArray();
        return CuttingCalculator.Calculate(documents,new Dictionary<Guid,IReadOnlyList<CuttingOrderLine>>(),filter);
    }

    public async Task<CuttingDetail> SaveAsync(Guid id,SaveCuttingDocument request,int? expectedVersion,bool create,string? actor,CancellationToken ct)
    {
        if(id==Guid.Empty||request.OrderId==Guid.Empty)throw new CuttingStoreException(400,"Kayıt ve sipariş kimliği gereklidir.");
        if(request.Groups is null||request.Groups.Any(g=>g is null))
            throw new CuttingValidationException(new Dictionary<string,string[]>{["groups"]=["Malzeme grubu listesi gereklidir; boş grup öğesi içeremez."]});
        await using var conn=await source.OpenConnectionAsync(ct);await using var tx=await conn.BeginTransactionAsync(ct);
        // Serializes same client-generated identity even before the first INSERT.
        await using(var mutex=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id,0))",conn,tx))
        { mutex.Parameters.AddWithValue("id",id.ToString());await mutex.ExecuteNonQueryAsync(ct); }
        CuttingDocument? current;
        await using(var cmd=new NpgsqlCommand("SELECT document::text FROM cutting.record WHERE id=@id FOR UPDATE",conn,tx))
        {cmd.Parameters.AddWithValue("id",id);current=await cmd.ExecuteScalarAsync(ct) is string text?JsonSerializer.Deserialize<CuttingDocument>(text,Json):null;}
        if(current is not null && create)
        {
            var original=new SaveCuttingDocument(current.OrderId,current.OrderVersion,current.Status,current.Groups);
            if(JsonSerializer.SerializeToElement(original,Json).GetRawText()!=JsonSerializer.SerializeToElement(request with{AcceptOrderChanges=false},Json).GetRawText())
                throw new CuttingStoreException(409,"Aynı kayıt kimliği farklı içerikle kullanılmış. Kaydı yeniden açın.");
            await tx.CommitAsync(ct);return await DetailAsync(current,ct);
        }
        if(current is null&&!create)throw new CuttingStoreException(404,"Kesim kaydı bulunamadı.");
        if(current is not null)
        {
            if(expectedVersion!=current.Version)throw new CuttingStoreException(412,"Kesim kaydı başka bir işlemde değişti. Yeniden yükleyin.");
            if(current.Status is "Completed" or "Cancelled")throw new CuttingStoreException(409,"Tamamlanmış veya iptal edilmiş kayıt değiştirilemez.");
            if(current.OrderId!=request.OrderId)throw new CuttingStoreException(409,"Mevcut kesim kaydının siparişi değiştirilemez. Yeni kayıt açın.");
            foreach(var old in current.Groups)
            {
                var next=request.Groups?.FirstOrDefault(g=>g is not null&&g.Id==old.Id);
                if(next is not null && old.MaterialId!=next.MaterialId && old.Plans.Count>0)
                    throw new CuttingStoreException(409,"Planı olan grubun malzemesi değiştirilemez. Yeni malzeme grubu ekleyin.");
            }
        }
        // Hold the existing Sales parent lock while reading through its published repository.
        await using(var orderLock=new NpgsqlCommand("SELECT id FROM sales_drafts.order_draft WHERE id=@id FOR UPDATE",conn,tx))
        {orderLock.Parameters.AddWithValue("id",request.OrderId);if(await orderLock.ExecuteScalarAsync(ct) is null)throw new CuttingStoreException(404,"Sipariş bulunamadı.");}
        var order=await orders.GetAsync(request.OrderId,ct)??throw new CuttingStoreException(404,"Sipariş bulunamadı.");
        // Closing an existing operational record must remain possible after Sales cancels
        // its source. Cancellation preserves every saved input and the historical snapshot.
        if(order.Status=="Cancelled"&&current is not null&&request.Status=="Cancelled")
        {
            if(JsonSerializer.Serialize(current.Groups,Json)!=JsonSerializer.Serialize(request.Groups,Json))
                throw new CuttingStoreException(409,"İptal edilen siparişe bağlı kesim kapatılırken kayıtlı gruplar ve alt satırlar değiştirilemez.");
            var cancelled=current with{Status="Cancelled",Version=current.Version+1,UpdatedAt=DateTimeOffset.UtcNow};
            await WriteAsync(conn,tx,cancelled,true,actor,ct);
            await tx.CommitAsync(ct);
            return await DetailAsync(cancelled,ct);
        }
        if(order.Status=="Cancelled")throw new CuttingStoreException(409,"İptal edilmiş sipariş için kesim kaydedilemez.");
        if(order.Version!=request.OrderVersion)throw new CuttingStoreException(409,"Sipariş sürümü değişti. Güncel siparişi inceleyin.");
        if(current is not null&&current.OrderVersion!=order.Version&&!request.AcceptOrderChanges)
            throw new CuttingStoreException(409,"Sipariş değişti. Kesim satırlarını inceleyip sipariş güncellemesini açıkça kabul edin.");
        var snapshot=order.Lines.Select(l=>new CuttingOrderLine(l.VariantOptionId,l.SizeOptionId,l.SetPartOptionId,l.Quantity)).ToArray();
        CuttingRules.Validate(request,snapshot);
        foreach(var group in request.Groups!)
        {
            await using(var materialLock=new NpgsqlCommand("SELECT urun_id FROM product_lifecycle.urun WHERE urun_id=@id FOR KEY SHARE",conn,tx))
            {materialLock.Parameters.AddWithValue("id",group.MaterialId);if(await materialLock.ExecuteScalarAsync(ct) is null)throw new CuttingStoreException(400,"Malzeme kartı bulunamadı.");}
        }
        // Read the public catalog only after every referenced product is locked; concurrent
        // ProductLifecycle mutations take the same parent lock before changing active/unit fields.
        var catalog=await products.Handle(ct);
        foreach(var group in request.Groups!)
        {
            var material=catalog.FirstOrDefault(p=>p.Id==group.MaterialId);
            if(material is null||!material.IsActive)throw new CuttingStoreException(400,"Aktif bir malzeme kartı seçin.");
            if(!string.Equals(material.StockUnit,group.Unit,StringComparison.Ordinal))throw new CuttingStoreException(400,"Tüketim birimi malzeme kartının stok birimiyle aynı olmalıdır. Otomatik birim dönüşümü yapılmaz.");
        }
        var now=DateTimeOffset.UtcNow;string number;
        if(current is null)
        {await using var seq=new NpgsqlCommand("SELECT 'KES-'||lpad(nextval('cutting.record_number_seq')::text,8,'0')",conn,tx);number=(string)(await seq.ExecuteScalarAsync(ct))!;}
        else number=current.Number;
        var doc=new CuttingDocument(id,request.OrderId,request.OrderVersion,number,request.Status,(current?.Version??0)+1,current?.CreatedAt??now,now,request.Groups!,snapshot);
        try
        {
            await WriteAsync(conn,tx,doc,current is not null,actor,ct);
            await tx.CommitAsync(ct);
        }
        catch(PostgresException ex) when(ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {throw new CuttingStoreException(409,"Bir grup, plan veya gerçekleşme kimliği başka kayıtta kullanılıyor ya da ilişkili kayıt değişmiş. Kaydı yeniden yükleyin.");}
        return await DetailAsync(doc,ct);
    }

    private static async Task WriteAsync(NpgsqlConnection conn,NpgsqlTransaction tx,CuttingDocument doc,bool replace,string? actor,CancellationToken ct)
    {
        var json=JsonSerializer.Serialize(doc,Json);
        await using(var header=new NpgsqlCommand("""
          INSERT INTO cutting.record(id,order_id,number,version,status,document,created_at,updated_at)
          VALUES(@id,@order,@number,@version,@status,@document,@created,@updated)
          ON CONFLICT(id) DO UPDATE SET version=excluded.version,status=excluded.status,document=excluded.document,updated_at=excluded.updated_at
          """,conn,tx))
        {
            header.Parameters.AddWithValue("id",doc.Id);header.Parameters.AddWithValue("order",doc.OrderId);header.Parameters.AddWithValue("number",doc.Number);
            header.Parameters.AddWithValue("version",doc.Version);header.Parameters.AddWithValue("status",doc.Status);header.Parameters.Add("document",NpgsqlDbType.Jsonb).Value=json;
            header.Parameters.AddWithValue("created",doc.CreatedAt);header.Parameters.AddWithValue("updated",doc.UpdatedAt);await header.ExecuteNonQueryAsync(ct);
        }
        if(replace)
        {
            await using var delete=new NpgsqlCommand("""
              DELETE FROM cutting.actual_line WHERE actual_id IN(SELECT a.id FROM cutting.actual a JOIN cutting.plan p ON p.id=a.plan_id JOIN cutting.material_group g ON g.id=p.group_id WHERE g.record_id=@id);
              DELETE FROM cutting.actual WHERE plan_id IN(SELECT p.id FROM cutting.plan p JOIN cutting.material_group g ON g.id=p.group_id WHERE g.record_id=@id);
              DELETE FROM cutting.plan_line WHERE plan_id IN(SELECT p.id FROM cutting.plan p JOIN cutting.material_group g ON g.id=p.group_id WHERE g.record_id=@id);
              DELETE FROM cutting.plan WHERE group_id IN(SELECT id FROM cutting.material_group WHERE record_id=@id);
              DELETE FROM cutting.material_group WHERE record_id=@id;
              """,conn,tx);delete.Parameters.AddWithValue("id",doc.Id);await delete.ExecuteNonQueryAsync(ct);
        }
        foreach(var g in doc.Groups)
        {
            await Execute("INSERT INTO cutting.material_group(id,record_id,material_id,unit) VALUES(@id,@parent,@material,@unit)",new(){["id"]=g.Id,["parent"]=doc.Id,["material"]=g.MaterialId,["unit"]=g.Unit});
            foreach(var p in g.Plans)
            {
                await Execute("INSERT INTO cutting.plan(id,group_id,number,planned_weight_kg) VALUES(@id,@parent,@number,@weight)",new(){["id"]=p.Id,["parent"]=g.Id,["number"]=p.Number,["weight"]=p.PlannedWeightKg});
                foreach(var l in p.Lines)
                    await Execute("INSERT INTO cutting.plan_line(plan_id,variant_option_id,size_option_id,set_part_option_id,pieces_per_layer) VALUES(@id,@variant,@size,@part,@qty)",new(){["id"]=p.Id,["variant"]=l.VariantOptionId,["size"]=l.SizeOptionId,["part"]=l.SetPartOptionId,["qty"]=l.PiecesPerLayer});
                foreach(var a in p.Actuals)
                {
                    await Execute("INSERT INTO cutting.actual(id,plan_id,date,net_weight_kg) VALUES(@id,@parent,@date,@weight)",new(){["id"]=a.Id,["parent"]=p.Id,["date"]=a.Date,["weight"]=a.NetWeightKg});
                    foreach(var l in a.Lines)
                        await Execute("INSERT INTO cutting.actual_line(actual_id,variant_option_id,size_option_id,set_part_option_id,cut_quantity,good_quantity,defect_quantity) VALUES(@id,@variant,@size,@part,@qty,@good,@defect)",new(){["id"]=a.Id,["variant"]=l.VariantOptionId,["size"]=l.SizeOptionId,["part"]=l.SetPartOptionId,["qty"]=l.CutQuantity,["good"]=l.GoodQuantity,["defect"]=l.DefectQuantity});
                }
            }
        }
        await using var audit=new NpgsqlCommand("INSERT INTO cutting.record_history(record_id,version,saved_at,actor,type,document) VALUES(@id,@version,@at,@actor,@type,@document)",conn,tx);
        audit.Parameters.AddWithValue("id",doc.Id);audit.Parameters.AddWithValue("version",doc.Version);audit.Parameters.AddWithValue("at",doc.UpdatedAt);
        audit.Parameters.Add("actor",NpgsqlDbType.Text).Value=(object?)actor??DBNull.Value;audit.Parameters.AddWithValue("type",replace?"Updated":"Created");
        audit.Parameters.Add("document",NpgsqlDbType.Jsonb).Value=json;await audit.ExecuteNonQueryAsync(ct);
        async Task Execute(string sql,Dictionary<string,object?> values)
        {
            await using var cmd=new NpgsqlCommand(sql,conn,tx);
            foreach(var (key,value) in values)
            {
                if(value is not null)cmd.Parameters.AddWithValue(key,value);
                else cmd.Parameters.Add(key,key=="part"?NpgsqlDbType.Uuid:NpgsqlDbType.Numeric).Value=DBNull.Value;
            }
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}

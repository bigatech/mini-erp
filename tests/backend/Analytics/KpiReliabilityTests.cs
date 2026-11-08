using DataIntakeAnalytics.Application;
using DataIntakeAnalytics.Infrastructure;
using Xunit;
using System.Text.Json;

namespace MiniErp.Analytics.Tests;
public sealed class KpiReliabilityTests
{
    static StagedFile File(string[] columns, params string[][] rows) => new(Guid.NewGuid(), "source.xlsx", columns, rows, DateTimeOffset.UtcNow);
    static decimal? Value(ProcessDashboard dashboard, string key) => dashboard.Metrics.Single(m => m.Key == key).Value;

    [Fact] public void Sizes_and_order_lines_separate_snapshots_but_order_is_not_repeated_per_operation()
    {
        var f = File(["__sheet","Tarih","Malzeme No","İş Emri No","Sipariş No","Kalem No","Beden","Yapılan İş","Sipariş Miktarı","Toplam Kesim Adedi"],
            ["KESİM","2026-10-01","A","W1","O1","10","S","Kesim","100","40"],
            ["KESİM","2026-10-01","A","W1","O1","10","M","Kesim","200","60"],
            ["KESİM","2026-10-01","A","W1","O1","10","S","Lazer Kesim","100","20"]);
        var d = ProcessAnalytics.Calculate([f],"kesim");
        Assert.Equal(300,Value(d,"order")); Assert.Equal(120,Value(d,"total"));
    }
    [Fact] public void Only_unambiguous_work_order_relations_are_inherited_and_material_alone_never_joins()
    {
        var f = File(["__sheet","Tarih","Malzeme No","İş Emri No","Sipariş No","Kalem No","Beden","Kesim Adet"],
            ["Emirler","2026-10-01","A","W1","O1","10","S",""],
            ["KESİM","2026-10-02","A","W1","","","","5"],
            ["KESİM","2026-10-02","A","","","","","8"]);
        var d = ProcessAnalytics.Calculate([f],"kesim");
        Assert.Contains(d.Rows,r => r.WorkOrder == "W1" && r.Order == "O1" && r.Size == "S" && r.MatchSource.Length > 0);
        Assert.Contains(d.Rows,r => r.WorkOrder == "" && r.Order == "");
        var conflict = f with { Rows = f.Rows.Append(["Emirler","2026-10-01","A","W1","O2","20","M",""]).ToArray() };
        Assert.All(ProcessAnalytics.Calculate([conflict],"kesim").Rows,r => Assert.Equal("",r.Order));
    }
    [Fact] public void Product_size_is_extracted_without_guessing_from_free_text()
    {
        var f = File(["__sheet","Tarih","Malzeme No","Malzeme Adı","Toplam Kesim Adedi"],
            ["KESİM","2026-10-01","A","TİŞÖRT(S)","10"],["KESİM","2026-10-01","A","TİŞÖRT(XL)","20"]);
        var d = ProcessAnalytics.Calculate([f],"kesim"); Assert.Equal(30,Value(d,"total")); Assert.Contains(d.Rows,r => r.Size == "XL");
    }
    [Fact] public void Quality_denominator_is_counted_once_per_inspection_and_unmatched_defects_block_rate()
    {
        var f = File(["__sheet","Tarih","Malzeme No","Kontrol No","Kontrol Miktarı","Hata Adedi","Hata Kodu"],
            ["kalite","2026-10-01","A","Q1","100","2","X"],["kalite","2026-10-01","A","Q1","100","3","Y"]);
        Assert.Equal(5,Value(ProcessAnalytics.Calculate([f],"kalite"),"rate"));
        var incomplete = f with { Rows = f.Rows.Append(["kalite","2026-10-01","B","Q2","","20","X"]).ToArray() };
        Assert.Null(Value(ProcessAnalytics.Calculate([incomplete],"kalite"),"rate"));
        var conflicting = f with { Rows = [f.Rows[0],["kalite","2026-10-01","A","Q1","50","3","Y"]] };
        Assert.Null(Value(ProcessAnalytics.Calculate([conflicting],"kalite"),"rate"));
    }
    [Fact] public void Explicit_zero_defects_produces_zero_rate()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kontrol Miktarı","Hata Adedi"],["kalite","2026-10-01","A","100","0"]);
        Assert.Equal(0,Value(ProcessAnalytics.Calculate([f],"kalite"),"rate"));
    }
    [Fact] public void Currency_aliases_are_normalized_and_missing_money_is_never_summed_or_charted()
    {
        var f=File(["Tarih","Malzeme No","Toplam Gelir","Toplam Gider","Para Birimi"],
            ["2026-10-01","A","100","80","TL"],["2026-10-02","B","20","10","TRY"]);
        Assert.Equal(30,Value(ProcessAnalytics.Calculate([f],"maliyet"),"profit"));
        var unknown=f with { Rows=[f.Rows[0],["2026-10-02","B","20","10",""]] };
        var d=ProcessAnalytics.Calculate([unknown],"maliyet");Assert.Null(Value(d,"revenue"));Assert.Empty(d.Daily);Assert.Empty(d.Breakdown);
    }
    [Fact] public void Subcontract_opening_is_not_added_repeatedly_and_period_filter_retains_prior_movements()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İş Emri No","Açılış Tarihi","Açılış Bakiyesi","Giden","Gelen"],
            ["FASON_ADET","2026-10-01","A","W1","2026-10-01","5","10","0"],
            ["FASON_ADET","2026-10-02","A","W1","2026-10-01","5","0","3"],
            ["FASON_ADET","2026-10-03","A","W1","","","2","0"]);
        var d=ProcessAnalytics.Calculate([f],"fason",from:new(2026,10,3));
        Assert.Equal(14,Value(d,"balance"));Assert.Equal(2,Value(d,"net"));
        Assert.Equal(14,Value(ProcessAnalytics.Calculate([f],"fason"),"balance"));
    }
    [Fact] public void Missing_opening_in_any_supplier_group_blocks_aggregate_balance()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İş Emri No","Üretim Yeri","Açılış Bakiyesi","Giden"],
            ["FASON_ADET","2026-10-01","A","W1","S1","5","10"],["FASON_ADET","2026-10-01","A","W1","S2","","10"]);
        Assert.Null(Value(ProcessAnalytics.Calculate([f],"fason"),"balance"));
    }
    [Fact] public void Confirmations_preserve_workcenter_and_connect_approved_packing_to_same_order_shipments()
    {
        var f=File(["__sheet","Onay Tarihi","Malzeme","Emir No","İş Merk.","İş Merk. Adı","Üretim AD","Onaylı Paket Adedi"],
            ["konf merkezi","2026-10-01","A","W1","DK001","Konfeksiyon Dikim","100",""],
            ["konf merkezi","2026-10-01","A","W1","PA005","Paketleme","100","100"],
            ["konf merkezi","2026-10-02","A","W1","SV001","Sevkiyat Konfeksiyon","60",""]);
        var sewing=ProcessAnalytics.Calculate([f],"dikim"); Assert.Equal(100,Value(sewing,"performance"));Assert.Equal("DK001",sewing.Breakdown.Single().Name);
        Assert.Equal(40,Value(ProcessAnalytics.Calculate([f],"paket",from:new(2026,10,2)),"ready"));
        var unapproved=f with { Rows=[f.Rows[0],["konf merkezi","2026-10-01","A","W1","PA005","Paketleme","100",""],f.Rows[2]] };
        Assert.Null(Value(ProcessAnalytics.Calculate([unapproved],"paket"),"ready"));
    }
    [Fact] public void Cross_file_format_duplicates_use_canonical_dates_and_preserve_row_multiplicity()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet"],["KESİM","2026-10-01T00:00:00","A","10"],["KESİM","2026-10-01T00:00:00","A","10"]);
        var copy=f with { Id=Guid.NewGuid(),Rows=[["KESİM","46296","A","10"],["KESİM","46296","A","10"],["KESİM","46296","A","10"]] };
        // Use the actual Excel serial, independent of host culture.
        copy=copy with { Rows=copy.Rows.Select(r => new[]{r[0],new DateTime(2026,10,1).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture),r[2],r[3]}).ToArray() };
        Assert.Equal(30,Value(ProcessAnalytics.Calculate([f,copy],"kesim"),"daily"));
    }
    [Fact] public void Shared_effort_is_not_reused_for_multiple_outputs_and_coverage_is_reported()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İş Emri No","Kesim Adet","Çalışan Sayısı","Çalışma Saati"],
            ["KESİM","2026-10-01","A","W1","100","",""],["KESİM","2026-10-01","A","W1","50","",""],
            ["KESİM","2026-10-01","A","W1","","2","1"]);
        Assert.Null(Value(ProcessAnalytics.Calculate([f],"kesim"),"efficiency"));
        var one=f with { Rows=[f.Rows[0],f.Rows[2]] };
        Assert.Equal(50,Value(ProcessAnalytics.Calculate([one],"kesim"),"efficiency"));
    }

    [Fact] public void Conflicting_copy_staffing_does_not_duplicate_output_or_publish_efficiency()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet","Çalışan Sayısı","Çalışma Saati"],
            ["KESİM","2026-10-01","A","100","2","1"]);
        var copy=f with { Id=Guid.NewGuid(),Rows=[["KESİM","2026-10-01","A","100","3","1"]] };
        var d=ProcessAnalytics.Calculate([f,copy],"kesim");
        Assert.Equal(100,Value(d,"daily"));Assert.Null(Value(d,"efficiency"));
    }

    [Fact] public void Pdf_reference_templates_extract_relations_without_counting_form_quantities()
    {
        var quality=PdfReader.ExtractReferences("KALİTE RAPORU\nMalz. Kodu: 5070237201\nİş Emri Tipi/No: P1 00528411\nSatış Siparişi: O2 26060023 170\nHatalı/İade Oranı: 5,5");
        var reference=Assert.Single(quality);Assert.Equal("170",reference.Line);Assert.Equal("00528411",reference.WorkOrder);
        var work=Assert.Single(PdfReader.ExtractReferences("İş Emri Kontrol Kartı\nSipariş No: ON - 26060455\nMalzeme No: 5070237201\nN100272366\nBeden:116 Desen:116"));
        Assert.Equal("116",work.Size);Assert.Equal("",work.Line);
        Assert.Empty(PdfReader.ExtractReferences("Malzeme No: 12345678 N100272366 Beden:116"));
    }
    [Fact] public void Events_after_explicit_reporting_date_are_filtered()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet"],["KESİM","2026-10-01","A","10"],["KESİM","2026-12-10","A","100"]);
        Assert.Equal(10,Value(ProcessAnalytics.Calculate([f],"kesim",to:new(2026,11,1)),"daily"));
    }

    [Fact] public void Return_document_number_does_not_become_sales_order_but_order_export_can_link()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İş Emri No","Belge No","Kalem No","Kesim Adet"],
            ["kalite rapor ekranı","2026-10-01","A","W1","RETURN1","10",""],
            ["KESİM","2026-10-01","A","W1","","","10"]);
        Assert.Equal("",Assert.Single(ProcessAnalytics.Calculate([f],"kesim").Rows).Order);
        var order=f with { Rows=[["sip listesi ekranı","2026-10-01","A","W1","O1","10",""],f.Rows[1]] };
        Assert.Equal("O1",Assert.Single(ProcessAnalytics.Calculate([order],"kesim").Rows).Order);
    }
    [Fact] public void Zero_identifier_is_missing_rather_than_shared_work_order()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İş Emri No","Kesim Adet"],["KESİM","2026-10-01","A","00000000","10"]);
        var d=ProcessAnalytics.Calculate([f],"kesim");Assert.Equal("",Assert.Single(d.Rows).WorkOrder);
        Assert.Contains(d.Warnings,w=>w.Contains("ilişkisi eksik"));
    }

    [Theory]
    [InlineData("3021-03-23")]
    [InlineData("01.01.1975")]
    public void Invalid_source_date_years_are_rejected(string date) => Assert.Null(ProcessAnalytics.ParseDate(date));

    [Fact] public void One_order_line_split_across_workorders_does_not_repeat_order_quantity()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İş Emri No","Sipariş No","Kalem No","Sipariş Miktarı"],
            ["KESİM","2026-10-01","A","W1","O1","10","100"],["KESİM","2026-10-01","A","W2","O1","10","100"]);
        Assert.Equal(100,Value(ProcessAnalytics.Calculate([f],"kesim"),"order"));
    }
    [Fact] public void Cumulative_snapshots_include_history_before_from_date()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Toplam Kesim Adedi"],["KESİM","2026-10-01","A","10"]);
        var d=ProcessAnalytics.Calculate([f],"kesim",from:new(2026,10,2),to:new(2026,10,3));
        Assert.Equal(10,Value(d,"total")); Assert.Null(Value(d,"daily"));
    }

    [Fact] public async Task Available_real_sources_can_be_read_and_audited_without_database_mutation()
    {
        var directory=Environment.GetEnvironmentVariable("MINIERP_SOURCE_AUDIT");
        if (string.IsNullOrWhiteSpace(directory)) return; // Opt-in local source verification, not a CI fixture.
        var files=new List<StagedFile>(); var manifest=new List<object>();
        foreach(var path in Directory.EnumerateFiles(directory).Where(p => new[]{".xlsx",".xls",".ods"}.Contains(Path.GetExtension(p))))
        {
            await using var stream=System.IO.File.OpenRead(path);
            var data=await new SpreadsheetReader().ReadAsync(stream,Path.GetExtension(path),default);
            Assert.NotEmpty(data.Columns); Assert.NotEmpty(data.Rows);
            files.Add(new(Guid.NewGuid(),Path.GetFileName(path),data.Columns,data.Rows,DateTimeOffset.UtcNow));
            manifest.Add(new { File=Path.GetFileName(path), Sha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await System.IO.File.ReadAllBytesAsync(path))), Count=data.Rows.Count, Sheets=data.Rows.Select(r=>r[0]).Distinct().ToArray() });
        }
        Assert.Equal(12,files.Count);
        foreach (var path in Directory.EnumerateFiles(directory,"*.pdf"))
        {
            await using var stream=System.IO.File.OpenRead(path);
            var data=await new PdfReader().ReadAsync(stream,default);
            files.Add(new(Guid.NewGuid(),Path.GetFileName(path),data.Columns,data.Rows,DateTimeOffset.UtcNow, data.Rows.Any(r=>r[1].Contains("OCR incelemesi gerekli")) ? "NeedsReview" : "Completed"));
            var referenceRows=data.Rows.Where(r=>r[4]=="reference").ToArray();
            if (Path.GetFileName(path) is "iş emri formu.pdf" or "kalite formu.pdf") Assert.NotEmpty(referenceRows);
            manifest.Add(new { File=Path.GetFileName(path), Pages=data.Rows.Count(r=>r[4]=="document"), NeedsOcr=data.Rows.Count(r=>r[1].Contains("OCR incelemesi gerekli")), ReferenceRows=referenceRows.Length, StructuredKpiMapping=false });
        }
        var dashboards=ProcessAnalytics.Processes.Select(p=>ProcessAnalytics.Calculate(files,p)).ToArray();
        Assert.All(dashboards,d=>Assert.True(d.RecordCount>0,$"{d.Process}: no records"));
        var report=Environment.GetEnvironmentVariable("MINIERP_AUDIT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(report)) await System.IO.File.WriteAllTextAsync(report,JsonSerializer.Serialize(new { Manifest=manifest, Dashboards=dashboards.Select(d=>new { d.Process,d.LatestDate,d.RecordCount,d.ExcludedRows,d.Metrics,d.MissingFields,d.Warnings }) },new JsonSerializerOptions { WriteIndented=true }));
    }
}

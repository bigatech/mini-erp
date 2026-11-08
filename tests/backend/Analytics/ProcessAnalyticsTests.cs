using DataIntakeAnalytics.Application;
using DataIntakeAnalytics.Infrastructure;
using ClosedXML.Excel;
using System.Text;
using Xunit;

namespace MiniErp.Analytics.Tests;
public sealed class ProcessAnalyticsTests
{
    static StagedFile File(string[] columns, params string[][] rows) => new(Guid.NewGuid(), "source.xlsx",columns,rows,DateTimeOffset.UtcNow);
    static decimal? Metric(ProcessDashboard d,string key) => d.Metrics.Single(m=>m.Key==key).Value;
    [Fact] public void Daily_and_cumulative_are_distinct_and_latest_snapshots_are_not_added_across_dates()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet","Toplam Kesim Adedi","Sipariş Kesim Adeti"],
          ["KESİM","2026-10-01","A","10","10","100"],["KESİM","2026-10-02","A","20","30","100"]);
        var d=ProcessAnalytics.Calculate([f],"kesim");
        Assert.Equal(20,Metric(d,"daily")); Assert.Equal(30,Metric(d,"total")); Assert.Equal(100,Metric(d,"order"));
    }
    [Fact] public void Missing_is_not_zero_and_conflicting_same_day_snapshots_are_withheld()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Toplam Kesim Adedi"],["KESİM","2026-10-01","A","10"],["KESİM","2026-10-01","A","20"]);
        var d=ProcessAnalytics.Calculate([f],"kesim"); Assert.Null(Metric(d,"daily"));Assert.Null(Metric(d,"total"));Assert.NotEmpty(d.Warnings);
    }
    [Fact] public void Derived_and_compilation_rows_do_not_inflate_daily_production()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet"],["KESİM","2026-10-01","A","10"],["Kesim Özet","2026-10-01","A","1000"]);
        var compilation=f with { Id=Guid.NewGuid(),FileName="ÜRETİM RAPORU Derleme.xlsx" };
        Assert.Equal(10,Metric(ProcessAnalytics.Calculate([f,compilation],"kesim"),"daily"));
    }
    [Fact] public void Duplicate_rows_in_different_files_are_excluded_but_same_file_rows_are_preserved()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet"],["KESİM","2026-10-01","A","10"],["KESİM","2026-10-01","A","10"]);
        var copy=f with {Id=Guid.NewGuid()}; var d=ProcessAnalytics.Calculate([f,copy],"kesim"); Assert.Equal(20,Metric(d,"daily"));Assert.Equal(2,d.ExcludedRows);
    }
    [Fact] public void Cost_reports_are_not_production_and_group_totals_are_not_repeated()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İşlem","Adet","Toplam Gelir","Toplam Gider","Normal Gider","Mesai Gideri","Para Birimi"],
        ["kesim","2026-10-19","A","Kesim","10","100","80","60","20","TRY"],["kesim","","B","","20","","","",""]);
        var d=ProcessAnalytics.Calculate([f],"maliyet");Assert.Equal(100,Metric(d,"revenue"));Assert.Equal(80,Metric(d,"expense"));Assert.Equal(20,Metric(d,"profit"));Assert.Equal(80,Metric(d,"labor")); Assert.Null(Metric(ProcessAnalytics.Calculate([f],"kesim"),"daily"));
    }
    [Fact] public void Quality_rate_requires_explicit_denominator_and_shipping_requires_approval()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Paket Adedi","2.KALİTE","BASKI HATASI"],["KALİTE-PAKET","2026-10-01","A","10","2","1"]);
        Assert.Null(Metric(ProcessAnalytics.Calculate([f],"kalite"),"rate"));Assert.Null(Metric(ProcessAnalytics.Calculate([f],"paket"),"ready"));
    }
    [Fact] public void Subcontract_net_does_not_claim_open_balance_without_opening()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Fason Giden Adet","Fason Gelen Adet"],["FASON_ADET","2026-10-01","A","10","12"]);
        var d=ProcessAnalytics.Calculate([f],"fason");Assert.Equal(-2,Metric(d,"net"));Assert.Null(Metric(d,"balance"));Assert.Null(Metric(d,"overdue"));
    }
    [Fact] public void Subcontract_opening_and_due_date_enable_balance_and_overdue()
    {
        var f=File(["__sheet","Tarih","Malzeme No","İş Emri No","Termin Tarihi","Açılış Bakiyesi","Giden","Gelen"],["FASON_ADET","2026-10-01","A","WO1","2026-10-10","5","10","3"]);
        var d=ProcessAnalytics.Calculate([f],"fason",to:new DateOnly(2026,11,1));Assert.Equal(12,Metric(d,"balance"));Assert.Equal(12,Metric(d,"overdue"));
    }
    [Fact] public void Date_and_material_filters_apply_before_metrics()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet"],["KESİM","2026-10-01","A","10"],["KESİM","2026-10-02","B","20"]);
        var d=ProcessAnalytics.Calculate([f],"kesim",to:new DateOnly(2026,10,1),material:"A");Assert.Equal(10,Metric(d,"daily"));Assert.Single(d.Rows);
    }
    [Fact] public void Empty_and_sentinel_dates_are_excluded()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet"],["KESİM","01.01.1975","A","10"],["KESİM","","B","20"]);
        Assert.Null(Metric(ProcessAnalytics.Calculate([f],"kesim"),"daily"));
    }
    [Fact] public void Employee_efficiency_uses_matched_row_person_hours()
    {
        var f=File(["__sheet","Tarih","Malzeme No","Kesim Adet","Çalışan Sayısı","Süre (DK)"],["KESİM","2026-10-01","A","100","2","30"]);
        Assert.Equal(100,Metric(ProcessAnalytics.Calculate([f],"kesim"),"efficiency"));
    }
    [Fact] public void Different_currencies_are_not_added()
    {
        var f=File(["Tarih","Malzeme No","Toplam Gelir","Para Birimi"],["2026-10-01","A","10","TRY"],["2026-10-01","B","20","EUR"]);
        Assert.Null(Metric(ProcessAnalytics.Calculate([f],"maliyet"),"revenue"));
    }
    [Fact] public async Task Xlsx_reads_all_sheets_preserves_source_coordinates_and_ignores_formatting_only_ranges()
    {
        using var book=new XLWorkbook();
        foreach(var name in new[]{"KESİM","DİKİM-BANT"}) {var s=book.AddWorksheet(name);s.Cell("A3").Value="Tarih";s.Cell("B3").Value="Malzeme No";s.Cell("C3").Value="Kesim Adet";s.Cell("A4").Value="2026-10-01";s.Cell("B4").Value="A";s.Cell("C4").Value=10;s.Cell("A100000").Style.Fill.BackgroundColor=XLColor.Red;}
        using var stream=new MemoryStream();book.SaveAs(stream);stream.Position=0;
        var data=await new SpreadsheetReader().ReadAsync(stream,".xlsx",default);Assert.Equal(2,data.Rows.Count);Assert.Equal("__sheet",data.Columns[0]);Assert.All(data.Rows,r=>Assert.Equal("4",r[1]));
    }
    [Fact] public async Task Csv_handles_Turkish_decimals_quoted_fields_and_semicolon_separator()
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes("Tarih;Malzeme No;Gelir\n2026-10-01;A;\"1.234,50\"\n"));
        var data=await new SpreadsheetReader().ReadAsync(stream,".csv",default);Assert.Equal(1234.5m,ProcessAnalytics.Number(data.Rows[0][2]));
    }
    [Fact] public async Task Ods_preserves_repeated_empty_cells_and_all_process_sheets()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Create,true))
        {
            var entry=zip.CreateEntry("content.xml");using var writer=new StreamWriter(entry.Open());
            writer.Write("""<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"><office:body><office:spreadsheet><table:table table:name="KESİM"><table:table-row><table:table-cell><text:p>Tarih</text:p></table:table-cell><table:table-cell table:number-columns-repeated="2"/><table:table-cell><text:p>Kesim Adet</text:p></table:table-cell></table:table-row><table:table-row><table:table-cell office:date-value="2026-10-01"/><table:table-cell table:number-columns-repeated="2"/><table:table-cell office:value="10"/></table:table-row></table:table></office:spreadsheet></office:body></office:document-content>""");
        }
        stream.Position=0;var result=await new SpreadsheetReader().ReadAsync(stream,".ods",default);
        Assert.Single(result.Rows);Assert.Equal("10",result.Rows[0][Array.IndexOf(result.Columns,"Kesim Adet")]);Assert.Equal("KESİM",result.Rows[0][0]);
    }
    [Fact] public void Uppercase_Turkish_headers_and_detailed_defect_types_are_mapped()
    {
        var f=File(["__sheet","TARİH","MALZEME NO","1.KALİTE","2.KALİTE","DOKUMA HATASI"],["KALİTE-PAKET","2026-10-01","A","90","10","2"]);
        var d=ProcessAnalytics.Calculate([f],"kalite");Assert.Equal(90,Metric(d,"first"));Assert.Contains(d.Breakdown,b=>b.Name=="dokumahatasi"&&b.Quantity==2);
    }
    [Fact] public async Task Csv_rejects_malformed_rows()
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2,3"));await Assert.ThrowsAsync<InvalidDataException>(()=>new SpreadsheetReader().ReadAsync(stream,".csv",default));
    }
}

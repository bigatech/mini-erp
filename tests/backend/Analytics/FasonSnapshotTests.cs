using DataIntakeAnalytics.Application;
using Xunit;

namespace MiniErp.Analytics.Tests;

public sealed class FasonSnapshotTests
{
    private static StagedFile Report(string date, params string[][] rows) => new(Guid.NewGuid(), $"ÜRETİM RAPORU {date}.ods",
        ["__sheet", "__row", "Tarih", "Malzeme No", "Fason Giden Adet", "Fason Gelen Adet"], rows, DateTimeOffset.UtcNow);
    private static decimal? Value(ProcessDashboard result, string key) => result.Metrics.Single(m => m.Key == key).Value;

    [Fact] public void Latest_complete_report_replaces_changed_and_removed_movements_but_preserves_row_multiplicity()
    {
        var old = Report("19.10.2026", ["FASON_ADET","2","2026-10-18","REMOVED","500","0"], ["FASON_ADET","3","2026-10-19","A","100","20"]);
        var latest = Report("20.10.2026", ["FASON_ADET","2","2026-10-19","A","100","80"], ["FASON_ADET","3","2026-10-19","A","100","80"]);
        var result = ProcessAnalytics.Calculate([old,latest], "fason", to:new(2026,11,1));
        Assert.Equal(200, Value(result,"sent")); Assert.Equal(160, Value(result,"received")); Assert.Equal(40, Value(result,"net"));
        Assert.Equal(2,result.Rows.Count); Assert.All(result.Rows,r => Assert.Contains(latest.FileName,r.Source));
        Assert.Equal(2,old.Rows.Count); Assert.Equal("Completed",old.Status);
        Assert.Null(Value(result,"balance"));
    }

    [Fact] public void Movement_filters_use_latest_revised_snapshot_and_ignore_upload_order()
    {
        var old = Report("19.10.2026", ["FASON_ADET","2","2026-10-18","A","10","2"]) with {UploadedAt=DateTimeOffset.UtcNow.AddHours(1)};
        var later = Report("20.10.2026", ["FASON_ADET","2","2026-10-18","A","100","20"]);
        Assert.Equal(100,Value(ProcessAnalytics.Calculate([old,later],"fason",from:new(2026,10,18),to:new(2026,10,19),material:"A"),"sent"));
        Assert.Null(Value(ProcessAnalytics.Calculate([old,later],"fason",to:new(2026,10,17)),"sent"));
        Assert.Null(Value(ProcessAnalytics.Calculate([old,later],"fason",from:new(2026,10,19),to:new(2026,10,20)),"sent"));
        Assert.Equal(100,Value(ProcessAnalytics.Calculate([old,later],"fason",to:new(2026,10,20)),"sent"));
    }

    [Fact] public void Future_dated_report_cannot_replace_the_current_snapshot_even_with_future_movement_cutoff()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Europe/Istanbul").DateTime);
        var current = Report("20.10.2026", ["FASON_ADET","2","2026-10-18","A","10","2"]);
        var future = Report(today.AddDays(1).ToString("dd.MM.yyyy"), ["FASON_ADET","2","2026-10-18","A","100","20"]);
        Assert.Equal(10,Value(ProcessAnalytics.Calculate([current,future],"fason",to:today.AddDays(2)),"sent"));
    }

    [Fact] public void Inactive_report_is_not_selected_and_separate_ledgers_remain_available()
    {
        var old = Report("19.10.2026", ["FASON_ADET","2","2026-10-18","A","10","2"]);
        var later = Report("20.10.2026", ["FASON_ADET","2","2026-10-18","A","100","20"]) with {Status="Superseded"};
        var ledger = Report("18.10.2026", ["FASON_ADET","2","2026-10-18","OTHER","3","1"]) with {FileName="separate-ledger.csv"};
        Assert.Equal(13,Value(ProcessAnalytics.Calculate([old,later,ledger],"fason",to:new(2026,11,1)),"sent"));
    }

    [Fact] public void Same_date_competing_snapshots_are_withheld_instead_of_picking_a_filename()
    {
        var first = Report("20.10.2026", ["FASON_ADET","2","2026-10-19","A","10","2"]);
        var other = Report("20.10.2026", ["FASON_ADET","2","2026-10-19","A","100","20"]) with {FileName="ÜRETİM RAPORU 20.10.2026-rev.ods"};
        var result = ProcessAnalytics.Calculate([first,other],"fason",to:new(2026,11,1));
        Assert.Null(Value(result,"sent")); Assert.Contains(result.Warnings,w=>w.Contains("sürüm önceliği belirsiz"));
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace DataIntakeAnalytics.Application;

public sealed record SourceKpiValue(decimal? Value, IReadOnlyList<string> Sources, string Scope);

// Independent arithmetic reference for the supplied report layouts. Do not call
// ProcessAnalytics.Calculate here: comparing that engine with itself hides errors.
public static class SourceKpiReference
{
    private static string N(string value) => ProcessAnalytics.Normalize(value);
    private static decimal? Number(string value) => decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static DateOnly? Date(string value)
    {
        if (double.TryParse(value, CultureInfo.InvariantCulture, out var serial) && serial is > 30000 and < 80000)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return DateTime.TryParse(value, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var date) && date.Year is >= 2000 and <= 2100 ? DateOnly.FromDateTime(date) : null;
    }
    private static IEnumerable<Dictionary<string,string>> Rows(StagedFile file)
    {
        var columns = file.Columns.Select(N).ToArray();
        foreach (var row in file.Rows)
        {
            var cells = new Dictionary<string,string>();
            for (int i = 0; i < columns.Length && i < row.Length; i++)
                if (!string.IsNullOrWhiteSpace(row[i])) cells.TryAdd(columns[i], row[i]);
            yield return cells;
        }
    }
    private static string Cell(Dictionary<string,string> row, params string[] columns) => columns.Select(N).Select(c => row.GetValueOrDefault(c, "").Trim()).FirstOrDefault(v => v.Length > 0) ?? "";

    public static IReadOnlyDictionary<string, SourceKpiValue> Calculate(IReadOnlyList<StagedFile> files, DateOnly asOf)
    {
        var result = new Dictionary<string, SourceKpiValue>();
        var productionDefects = new HashSet<string>();
        var reports = files.Where(f => f.Status == "Completed" && N(f.FileName).StartsWith("uretimraporu") && !N(f.FileName).Contains("derleme"))
            .Select(f => new { File = f, Match = Regex.Match(f.FileName, @"\d{2}\.\d{2}\.\d{4}") })
            .Where(f => f.Match.Success).Select(f => new { f.File, Date = Date(f.Match.Value) }).Where(f => f.Date <= asOf).ToArray();
        var latest = reports.OrderByDescending(f => f.Date).ThenBy(f => f.File.FileName, StringComparer.Ordinal).FirstOrDefault();
        if (latest is not null)
        {
            var rows = Rows(latest.File).Where(r => Date(Cell(r,"Tarih")) is { } date && date <= asOf && Cell(r,"Malzeme No","Malzeme Numarası").Length > 0).ToArray();
            Dictionary<string,string>[] Select(string sheet) => rows.Where(r => N(Cell(r,"__sheet")) == N(sheet)).ToArray();
            decimal? Sum(IEnumerable<Dictionary<string,string>> selected, string field)
            {
                var numbers = selected.Select(r => Number(Cell(r,field))).Where(n => n.HasValue).ToArray();
                return numbers.Length == 0 ? null : numbers.Sum(n => n!.Value);
            }
            void Add(string process, string key, decimal? value, string sheet, string scope) => result[process+"/"+key] = new(value,[latest.File.FileName + " / " + sheet],scope);
            foreach (var (process,sheet,column) in new[] { ("kesim","KESİM","Kesim Adet"), ("dikim","DİKİM-BANT","Günlük Dikim Adet"), ("paket","KALİTE-PAKET","Paket Adedi") })
            {
                var selected = Select(sheet); var lastDay = selected.Select(r => Date(Cell(r,"Tarih"))).Max();
                Add(process,"daily",Sum(selected.Where(r => Date(Cell(r,"Tarih")) == lastDay),column),sheet,$"Son tarihli üretim raporu, son hareket günü {lastDay:yyyy-MM-dd}; satır miktarları doğrudan toplanır.");
            }
            Add("kalite","first",Sum(Select("KALİTE-PAKET"),"1. Kalite"),"KALİTE-PAKET","Son raporun açık 1. kalite sütunu toplamı.");
            Add("kalite","second",Sum(Select("KALİTE-PAKET"),"2. Kalite"),"KALİTE-PAKET","Son raporun açık 2. kalite sütunu toplamı.");
            foreach (var row in Select("KALİTE-PAKET"))
                foreach (var cell in row.Where(c => Regex.IsMatch(c.Key, @"^hata\d+$") || new[] { "baskihatasi", "dokumahatasi", "kesik", "telkacigi", "olcu", "tamir" }.Contains(c.Key)))
                    if (Number(cell.Value) > 0) productionDefects.Add(cell.Key);
            var fason = rows.Where(r => N(Cell(r,"__sheet")).StartsWith("fason")).ToArray();
            var sent = Sum(fason,"Fason Giden Adet"); var received = Sum(fason,"Fason Gelen Adet");
            const string scope = "Son raporun fason sayfaları doğrudan toplanır. Eski raporlarla birleşim daha yüksekse revizyon/silinen hareket kapsamı uzlaştırılmadan KPI güvenilir sayılmaz.";
            Add("fason","sent",sent,"FASON_*",scope); Add("fason","received",received,"FASON_*",scope); Add("fason","net",sent-received,"FASON_*",scope);
        }
        var qualityFiles = files.Where(f => f.Status == "Completed" && Path.GetExtension(f.FileName).Equals(".xls",StringComparison.OrdinalIgnoreCase)).ToArray();
        var defects = qualityFiles.SelectMany(f => Rows(f).Where(r => N(Cell(r,"__sheet")) == "kaliteraporekrani" &&
                Date(Cell(r,"Belge Tarihi")) is { } date && date <= asOf && Cell(r,"Malzeme","Malzeme No","Malz. No").Length > 0 && Number(Cell(r,"Hatalı Miktar")) > 0))
            .Select(r => Cell(r,"Hata Açıklama","Hata Açıklaması","Hata Kodu")).Where(v => v.Length > 0).Concat(productionDefects).Distinct().Count();
        if (qualityFiles.Length > 0) result["kalite/types"] = new(defects,qualityFiles.Select(f => f.FileName + " / kalite rapor ekranı").Concat(latest is null ? [] : new[] { latest.File.FileName + " / KALİTE-PAKET" }).ToArray(),"XLS kalite dökümünde pozitif hatalı miktarı olan farklı açıklama/kodlar ve son üretim raporundaki pozitif hata sütunları. Kontrol paydası doğrulanmaz.");
        return result;
    }
}

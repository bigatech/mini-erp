using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DataIntakeAnalytics.Application;

public sealed record ProcessMetric(string Key, string Label, decimal? Value, string Unit, string Formula);
public sealed record ProcessRow(string Date, string Material, string Product, string WorkCenter, string Supplier, string Source, int SourceRow, IReadOnlyDictionary<string, decimal?> Values)
{
    public string Order { get; init; } = "";
    public string OrderLine { get; init; } = "";
    public string WorkOrder { get; init; } = "";
    public string Size { get; init; } = "";
    public string MatchSource { get; init; } = "";
}
public sealed record ProcessDashboard(string Process, string? LatestDate, int RecordCount, int ExcludedRows, IReadOnlyList<ProcessMetric> Metrics, IReadOnlyList<CategoryKpi> Breakdown, IReadOnlyList<CategoryKpi> Daily, IReadOnlyList<ProcessRow> Rows, IReadOnlyList<string> MissingFields, IReadOnlyList<string> Warnings);

public interface IProcessDashboardCache
{
    Task<ProcessDashboard> GetOrCreateAsync(IReadOnlyList<StagedFile> manifest, string process, DateOnly? from, DateOnly? to, string? material, Func<Task<ProcessDashboard>> calculate, CancellationToken ct);
}

public sealed class GetProcessDashboardHandler(IStagingRepository repository, IProcessDashboardCache? cache = null)
{
    public async Task<ProcessDashboard> HandleAsync(string process, DateOnly? from, DateOnly? to, string? material, CancellationToken ct)
    {
        if (!ProcessAnalytics.Processes.Contains(process)) throw new ArgumentException("Bilinmeyen süreç.");
        var manifest = await repository.ListAsync(ct);
        async Task<ProcessDashboard> Calculate()
        {
            var files = await repository.LoadCompletedAsync(manifest, ct);
            ct.ThrowIfCancellationRequested();
            return ProcessAnalytics.Calculate(files, process, from, to, material);
        }
        return cache is null ? await Calculate() : await cache.GetOrCreateAsync(manifest, process, from, to, material, Calculate, ct);
    }
}

public static class ProcessAnalytics
{
    public static readonly string[] Processes = ["kesim", "dikim", "kalite", "paket", "fason", "maliyet"];
    public static string Normalize(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        Span<char> buffer = decomposed.Length <= 256 ? stackalloc char[decomposed.Length] : new char[decomposed.Length];
        var length = 0;
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                buffer[length++] = c == 'ı' ? 'i' : char.ToLowerInvariant(c);
        return new string(buffer[..length]);
    }
    private static readonly Dictionary<string, string[]> Fields = new()
    {
        ["date"] = ["Tarih", "Onay Tarihi", "Belge Tarihi", "Üretim Tarihi", "Belge Trh"], ["material"] = ["Malzeme No", "Malzeme Numarası", "Ürün Kodu", "Malzeme", "Malz. No", "M.No"],
        ["product"] = ["Malzeme Detay", "Malzeme Adı", "Ürün", "Ürün Adı", "Malzeme Açıklaması", "M.Adı"], ["operation"] = ["Yapılan İş", "İşlem", "Operasyon", "Süreç", "İş Merk. Adı", "Kapasite Grup."],
        ["order"] = ["Sipariş No", "SipNum.", "Sat. Sip. No", "Belge No"], ["line"] = ["Sipariş Kalem No", "Sip.Kal.Num.", "Kal. No", "KalemNo"], ["workorder"] = ["İş Emri No", "Emir No", "__context_workorder"],
        ["orderType"] = ["Sipariş Tipi", "SipTip.", "Satış S. Tipi", "BTipi"], ["workorderType"] = ["İş Emri Tipi", "Tip", "__context_workorder_type"],
        ["size"] = ["Beden", "Beden Kodu"], ["variant"] = ["Varyant", "Seçenek açıklaması"], ["shift"] = ["Vardiya"], ["employee"] = ["Personel Num", "Personel No"],
        ["inspectionId"] = ["Kontrol No", "Kayıt No"], ["unit"] = ["Miktar Br", "OnayBr.", "Birim"],
        ["shipment"] = ["Sevk Adedi", "Sevk Edilen Adet"], ["approvedPack"] = ["Onaylı Paket Adedi"],
        ["openingDate"] = ["Açılış Tarihi", "Devir Tarihi"], ["personHours"] = ["Kişi Saat", "Çalışan Saat"],
        ["center"] = ["Bant", "Bant No", "İş Merkezi", "İş Merkezi Kodu", "İş Merk.", "__context_center"], ["supplier"] = ["Üretim Yeri", "Tedarikçi"], ["due"] = ["Revize Tarihi", "Termin Tarihi", "Sipariş Termin Tarihi", "İşEm.Ter. Tar."],
        ["orderQty"] = ["Sipariş Kesim Adeti", "Sipariş Kesim Adedi", "Sipariş Miktarı", "Sipariş Adeti", "Sip.Mik."],
        ["cut"] = ["Kesim Adet", "Günlük Kesim", "Kesim Adedi"], ["cutTotal"] = ["Toplam Kesim Adedi"],
        ["sew"] = ["Günlük Dikim Adet", "Günlük Dikim Adedi", "Günlük Dikim", "Üretim Adedi"], ["sewTotal"] = ["Top Dikim Adedi", "Toplam Dikim", "Toplam Dikim Adedi"],
        ["pack"] = ["Paket Adedi", "Günlük Paket Adedi"], ["packTotal"] = ["Top Paket Adedi", "Toplam Paket", "Toplam Paket Adedi"],
        ["first"] = ["1 Kalite", "1. Kalite"], ["second"] = ["2 Kalite", "2. Kalite"],
        ["inspection"] = ["Kontrol Miktarı", "Kontrol Adedi", "Kaliteye Gelen Miktar"], ["defectQty"] = ["Hata Adedi", "Hata Miktarı", "Hatalı Miktar"], ["defectName"] = ["Hata Açıklama", "Hata Açıklaması", "Hata Kodu"],
        ["ready"] = ["Sevkiyata Hazır Ürün", "Sevkiyata Hazır Adet", "Onaylı Hazır Adet"],
        ["opening"] = ["Açılış Bakiyesi", "Devir Bakiyesi"], ["sent"] = ["Fason Giden Adet", "Giden"], ["received"] = ["Fason Gelen Adet", "Gelen"],
        ["employees"] = ["Çalışan Sayısı", "Toplam Çalışan", "Banttaki Çalışan Sayı", "Çalışan Kişi"],
        ["hours"] = ["Çalışma Saati", "Normal Saat"], ["minutes"] = ["Süre DK", "Süre (DK)"],
        ["overtime"] = ["Mesai Saati", "Mesai Saatı"], ["overtimeEmployees"] = ["Mesai Çalışan"],
        ["revenue"] = ["Toplam Gelir", "Gelir"], ["expense"] = ["Toplam Gider", "Gider"], ["normalCost"] = ["Normal Gider"], ["overtimeCost"] = ["Mesai Gideri"], ["currency"] = ["Para Birimi", "Döviz"]
    };
    private static readonly Dictionary<string, string[]> NormalizedFields = Fields.ToDictionary(p => p.Key, p => p.Value.Select(Normalize).ToArray());
    private sealed record Row(Dictionary<string,string> Raw, StagedFile File, Func<string, string> Normalize)
    {
        private readonly Dictionary<string,string> rawValues = new();
        private readonly Dictionary<string,decimal?> numbers = new();
        private string? cachedProcess;
        private DateOnly? cachedDate;
        private bool dateRead;
        private string? normalizedSheet;
        private string? frozenIdentity;
        private string? productSize;
        public Dictionary<string,string> Matches { get; } = new();
        public string MatchSource { get; set; } = "";
        public string Get(string field)
        {
            if (!rawValues.TryGetValue(field, out var value))
            {
                value = "";
                foreach (var alias in NormalizedFields[field])
                    if (Raw.TryGetValue(alias, out var source) && !string.IsNullOrWhiteSpace(source))
                    { value = source.Trim(); break; }
                // Resolve immutable source fields once; Matches remains a separate,
                // mutable fallback during relationship/center inference.
                if (field == "order" && !NormalizedFields[field].Where(a => a != "belgeno").Any(a => !string.IsNullOrWhiteSpace(Raw.GetValueOrDefault(a))) &&
                    !NormalizedSheet.Contains("sip") && !NormalizedSheet.Contains("temsilci")) value = "";
                if (field is "order" or "line" or "workorder" or "material" && value.Length > 0 && value.All(c => c == '0')) value = "";
                rawValues[field] = value;
            }
            // Belge No is an order only in an order export. In quality reports it
            // can identify a return/invoice, and must not become an order relation.
            if (value.Length == 0) value = Matches.GetValueOrDefault(field, "");
            if (field == "size" && value.Length == 0)
            {
                productSize ??= Regex.Match(Get("product"), @"\((XXS|XS|S|M|L|XL|XXL|2XL|3XL)\)", RegexOptions.IgnoreCase).Groups[1].Value;
                if (productSize.Length > 0) { value = productSize; rawValues[field] = value; }
            }
            return field == "size" ? value.ToUpperInvariant().Replace("XXL", "2XL") : value;
        }
        public decimal? Num(string field)
        {
            var value = NumRaw(field);
            // Duplicate worker headings in later daily templates represent separate groups.
            if (field == "employees" && Fields[field].Any(a => Raw.ContainsKey(Normalize(a + " [2]")) && !string.IsNullOrWhiteSpace(Raw[Normalize(a + " [2]")]))) return null;
            if (value == null && !Cost && Raw.ContainsKey("uretimad"))
            {
                if (field == "sew" && Process == "dikim" || field == "cut" && Process == "kesim" || field == "pack" && Process == "paket" || field == "shipment" && Process == "sevkiyat")
                    return Number(Raw["uretimad"]);
            }
            return value;
        }
        public DateOnly? Date { get { if (!dateRead) { cachedDate = ParseDate(Get("date")); dateRead = true; } return cachedDate; } }
        public bool Linked => Get("workorder").Length > 0 || Get("order").Length > 0 && Get("line").Length > 0;
        public string Identity => frozenIdentity ??= string.Join("|", new[] { "orderType", "order", "line", "workorderType", "workorder", "material", "variant", "size" }.Select(Get));
        public void SetMatch(string field, string value)
        {
            Matches[field] = value;
            frozenIdentity = null;
        }
        public void FreezeIdentity() => frozenIdentity = Identity;
        public string Key => Identity + "|" + Normalize(Get("operation")) + "|" + Get("center") + "|" + Get("supplier") + "|" + (Process == "fason" ? Normalize(Sheet) : "");
        public string Sheet => Raw.GetValueOrDefault("sheet", "");
        private string NormalizedSheet => normalizedSheet ??= Normalize(Sheet);
        public bool Cost => NumRaw("revenue") != null || NumRaw("expense") != null || NumRaw("normalCost") != null || NumRaw("overtimeCost") != null || Raw.GetValueOrDefault("family") == "cost";
        private decimal? NumRaw(string field)
        {
            if (!numbers.TryGetValue(field, out var value)) { value = Number(Get(field)); numbers[field] = value; }
            return value;
        }
        public bool Derived => new[] { "ozet", "chart", "derleme", "eslestirme", "tespit", "malzemedetay", "notlar", "yonetim" }.Any(NormalizedSheet.Contains) || Normalize(File.FileName).Contains("derleme");
        public string Process => cachedProcess ??= Classify();
        private string Classify()
        {
                if (Cost) return "maliyet";
                var sheet = NormalizedSheet; var operation = Normalize(Get("operation")); var center = Get("center").ToUpperInvariant();
                if (sheet.Contains("fason") || operation.Contains("fason")) return "fason";
                if (sheet.Contains("kalitepaket")) return "kalitepaket";
                if (sheet.Contains("kalite")) return "kalite";
                if (center == "SV001" || operation.Contains("sevkiyat") || sheet.Contains("sevkiyat")) return "sevkiyat";
                if (center == "PA005" || operation.Contains("paket") || sheet == "paket") return "paket";
                if (operation.Contains("kesim") || sheet.Contains("kesim")) return "kesim";
                if (operation.Contains("dikim") || sheet.Contains("dikim")) return "dikim";
                return "";
        }
    }
    private static bool Relevant(Row row, string process) => row.Process == process || row.Process == "kalitepaket" && process is "kalite" or "paket";
    private static bool IsDefectColumn(string key) => Regex.IsMatch(key, @"^hata\d+$") || new[] { "baskihatasi", "dokumahatasi", "kesik", "telkacigi", "olcu", "tamir" }.Contains(key);
    private static decimal? Defects(Row row)
    {
        if (row.Num("defectQty") is { } explicitQuantity) return explicitQuantity;
        var values = row.Raw.Where(v => IsDefectColumn(v.Key)).Select(v => Number(v.Value)).Where(v => v.HasValue).ToArray();
        return values.Length == 0 ? null : values.Sum(v => v!.Value);
    }
    private static string Currency(string text) => text.Trim().ToUpperInvariant() switch
    { "TL" or "₺" or "TRY" => "TRY", "EURO" or "€" or "EUR" => "EUR", "DOLAR" or "$" or "USD" => "USD", "GBP" or "£" => "GBP", _ => "" };
    public static decimal? Number(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-") return null;
        var match = Regex.Match(text.Replace("\u00a0", "").Trim(), @"^([-+]?\d[\d.,]*)(?:\s*(?:KİŞİ|KISI|SAAT|DK|ADET))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var value = match.Groups[1].Value;
        if (value.Contains(',')) value = value.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
    public static DateOnly? ParseDate(string text)
    {
        if (double.TryParse(text, CultureInfo.InvariantCulture, out var serial) && serial is > 30000 and < 80000)
        { var serialDate = DateOnly.FromDateTime(DateTime.FromOADate(serial)); return serialDate.Year <= 2100 ? serialDate : null; }
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp) && text.Contains('T') && stamp.Year is >= 2000 and <= 2100) return DateOnly.FromDateTime(stamp);
        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var localStamp) && text.Contains(':') && localStamp.Year is >= 2000 and <= 2100) return DateOnly.FromDateTime(localStamp);
        if (DateOnly.TryParse(text, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var date) && date.Year is >= 2000 and <= 2100) return date;
        return null;
    }
    public static ProcessDashboard Calculate(IEnumerable<StagedFile> files, string process, DateOnly? from = null, DateOnly? to = null, string? material = null)
    {
        if (!Processes.Contains(process)) throw new ArgumentException("Bilinmeyen süreç.");
        var reportDate = to ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Europe/Istanbul").DateTime);
        var fileSet = files.ToArray();
        // Repeated sheet names, field values and source names dominate this path.
        // Keep reuse local to this calculation so no source data survives globally.
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        string Normalize(string value)
        {
            if (normalized.TryGetValue(value, out var result)) return result;
            result = ProcessAnalytics.Normalize(value);
            if (normalized.Count < 16384) normalized[value] = result;
            return result;
        }
        var all = new List<Row>(); int excluded = 0; var warnings = new HashSet<string>();
        foreach (var file in fileSet.OrderBy(f => f.UploadedAt))
        {
        var normalizedColumns = file.Columns.Select(Normalize).ToArray();
        foreach (var values in file.Rows)
        {
            var raw = new Dictionary<string,string>();
            for (int i = 0; i < normalizedColumns.Length; i++)
            {
                var value = i < values.Length ? values[i] : "";
                if (string.IsNullOrWhiteSpace(value)) continue;
                raw.TryAdd(normalizedColumns[i], value);
            }
            var row = new Row(raw, file, Normalize);
            if (file.Status != "Completed" || row.Derived || row.Get("material").Length == 0 || Normalize(row.Get("material")).Contains("toplam"))
            { if (Relevant(row, process)) excluded++; continue; }
            all.Add(row);
        }
        }
        if (process == "fason")
        {
            var snapshotAsOf = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Europe/Istanbul").DateTime);
            // The owner confirmed that dated production reports are complete revised
            // snapshots. Their deleted/changed movements must not survive in a union.
            // This affects only fason rows in this report family, not other ledgers.
            var reports = fileSet.Where(f => f.Status == "Completed" && Normalize(f.FileName).StartsWith("uretimraporu") && !Normalize(f.FileName).Contains("derleme"))
                .Select(f => new { File = f, Match = Regex.Match(f.FileName, @"\d{2}\.\d{2}\.\d{4}") })
                .Where(f => f.Match.Success)
                .Select(f => new { f.File, Date = ParseDate(f.Match.Value) }).Where(f => f.Date.HasValue).ToArray();
            // from/to are movement filters, not historical publication cutoffs.
            var eligible = reports.Where(f => f.Date <= snapshotAsOf).ToArray();
            var latestDate = eligible.Select(f => f.Date).Max();
            var latestReports = eligible.Where(f => f.Date == latestDate).ToArray();
            var reportIds = reports.Select(f => f.File.Id).ToHashSet();
            if (latestReports.Length > 1)
            {
                // Filename/upload order is not an approved tie-breaker for revisions.
                excluded += all.RemoveAll(r => Relevant(r, process));
                warnings.Add("Aynı rapor tarihli birden fazla tam üretim raporu var; sürüm önceliği belirsiz olduğundan fason KPI hesaplanmadı.");
            }
            else if (reports.Length > 0)
            {
                var chosen = latestReports.SingleOrDefault();
                excluded += all.RemoveAll(r => Relevant(r, process) && reportIds.Contains(r.File.Id) && r.File.Id != chosen?.File.Id);
                warnings.Add(chosen is null
                    ? "Bugün itibarıyla yayımlanmış tam üretim raporu yok; sonraki tarihli raporların fason satırları kullanılmadı."
                    : $"Fason tam rapor kaynağı: {chosen.File.FileName} ({chosen.Date:yyyy-MM-dd}). Önceki raporların fason satırları toplama eklenmedi; staging korunuyor.");
            }
        }
        // Never infer an order from material alone. Fill missing relationship fields only
        // from a unique explicit work-order/material relation, respecting supplied dimensions.
        var relations = all.Where(r => r.Get("workorder").Length > 0 && r.Get("order").Length > 0).ToLookup(r => (r.Get("workorder"), r.Get("material")));
        foreach (var row in all.Where(r => r.Get("workorder").Length > 0))
        {
            var candidates = relations[(row.Get("workorder"), row.Get("material"))].Where(r =>
                new[] { "orderType", "order", "line", "workorderType", "size", "variant" }.All(f => row.Get(f).Length == 0 || r.Get(f) == row.Get(f))).ToArray();
            if (candidates.Select(r => r.Identity).Distinct().Count() == 1)
            {
                foreach (var field in new[] { "orderType", "order", "line", "workorderType", "size", "variant" })
                    if (row.Get(field).Length == 0) row.SetMatch(field, candidates[0].Get(field));
                row.MatchSource = candidates[0].File.FileName + " / " + candidates[0].Sheet + " / " + candidates[0].Raw.GetValueOrDefault("row", "");
            }
            else if (candidates.Length > 1) warnings.Add("Çelişkili iş emri–sipariş/beden ilişkisi otomatik eşlenmedi.");
        }
        // Relationship fields are now resolved; center inference never changes them.
        // Reuse their identity throughout grouping and matching instead of rebuilding
        // eight fields (and normalizing sheet names) for every comparison.
        foreach (var row in all) row.FreezeIdentity();
        var centerRows = all.Where(r => r.Linked && r.Get("center").Length > 0).ToLookup(r => (r.Identity, r.Date, r.Process, r.Get("shift")));
        foreach (var row in all.Where(r => r.Linked && r.Get("center").Length == 0 && r.Date.HasValue))
        {
            var quantityField = row.Process == "dikim" ? "sew" : row.Process == "kesim" ? "cut" : "pack";
            var candidates = centerRows[(row.Identity, row.Date, row.Process, row.Get("shift"))].Where(r => r.Num(quantityField).HasValue && r.Num(quantityField) == row.Num(quantityField)).ToArray();
            var centers = candidates.Select(r => r.Get("center")).Distinct().ToArray();
            if (centers.Length == 1)
            {
                row.SetMatch("center", centers[0]);
                row.MatchSource += " İş merkezi: " + candidates[0].File.FileName + " / " + candidates[0].Sheet + " / " + candidates[0].Raw.GetValueOrDefault("row", "");
            }
        }
        // Multiset de-duplication preserves identical legitimate rows inside one source.
        // Compare normalized business fields so ODS ISO dates and XLSX serials agree.
        var seen = new Dictionary<string,int>(); var unique = new List<Row>();
        var sourceOccurrences = new Dictionary<(string Fingerprint, int Occurrence), Row>();
        // Relations above require all sources, but de-duplicating unrelated ledgers
        // for every dashboard wastes most of the real-data validation runtime.
        foreach (var fileGroup in all.Where(r => Relevant(r, process) || process == "paket" && r.Process == "sevkiyat").GroupBy(r => (r.File.Id, Ledger: Normalize(r.Sheet) is "konfaylikismrkzliurekrani" or "konfmerkzicalismaekrani" ? Normalize(r.Sheet) : "")))
        {
            var counts = new Dictionary<string,int>();
            foreach (var row in fileGroup)
            {
                string Canonical(string field) => field == "date" ? row.Date?.ToString("yyyy-MM-dd") ?? row.Get(field) :
                    field is "order" or "workorder" or "material" or "line" or "inspectionId" ? row.Get(field) :
                    Number(row.Get(field))?.ToString(CultureInfo.InvariantCulture) ?? Normalize(row.Get(field));
                var fingerprint = row.Process + "|" + (row.Process == "fason" ? Normalize(row.Sheet) : "") + "|" +
                    string.Join("|", Fields.Keys.Where(f => row.Process == "maliyet" || !new[] { "employees", "hours", "minutes", "overtime", "overtimeEmployees", "personHours" }.Contains(f)).Select(f => f + ":" + Canonical(f))) + "|" +
                    string.Join("|", row.Raw.Where(v => IsDefectColumn(v.Key)).OrderBy(v => v.Key).Select(v => v.Key + ":" + Number(v.Value)));
                counts[fingerprint] = counts.GetValueOrDefault(fingerprint) + 1;
                if (counts[fingerprint] <= seen.GetValueOrDefault(fingerprint))
                {
                    var prior = sourceOccurrences[(fingerprint, counts[fingerprint])];
                    if (!row.Cost && new[] { "employees", "hours", "minutes", "personHours" }.Any(f => prior.Get(f) != row.Get(f)))
                    {
                        prior.Raw["effortconflict"] = "true";
                        if (Relevant(row, process)) warnings.Add("Kopya üretim kayıtlarının çalışan/saat bilgisi farklı; çıktı bir kez sayıldı, çelişkili işçilik verimliliğe alınmadı.");
                    }
                    if (Relevant(row, process)) excluded++;
                    continue;
                }
                unique.Add(row);
                sourceOccurrences[(fingerprint, counts[fingerprint])] = row;
            }
            foreach (var count in counts) seen[count.Key] = Math.Max(seen.GetValueOrDefault(count.Key), count.Value);
        }
        if (!to.HasValue && unique.Any(r => Relevant(r,process) && r.Date > reportDate)) warnings.Add("Rapor tarihinden sonraki olaylar KPI dışında tutuldu; kaynak tarihleri doğrulanmalı.");
        var history = unique.Where(r => Relevant(r, process) && r.Date.HasValue && r.Date <= reportDate &&
            (string.IsNullOrWhiteSpace(material) || r.Get("material").Contains(material, StringComparison.OrdinalIgnoreCase))).ToList();
        var invalidDates = unique.Count(r => Relevant(r, process) && !r.Date.HasValue);
        excluded += invalidDates;
        if (invalidDates > 0) warnings.Add("Tarihi boş/geçersiz satırlar tarihli KPI hesaplarına alınmadı; ham staging korunuyor.");
        var selected = history.Where(r => !from.HasValue || r.Date >= from).ToList();
        if (selected.Any(r => !r.Linked)) warnings.Add("Sipariş kalemi / iş emri ilişkisi eksik; yalnız malzeme ile sipariş eşleştirilmedi.");
        var latest = selected.Select(r => r.Date).Max(); var day = selected.Where(r => r.Date == latest).ToArray();
        var missing = new HashSet<string>(); var metrics = new List<ProcessMetric>();
        decimal? Sum(IEnumerable<Row> rows, string field) { var nums = rows.Select(r => r.Num(field)).Where(n => n.HasValue).ToArray(); return nums.Length == 0 ? null : nums.Sum(n => n!.Value); }
        decimal? Snapshot(string field)
        {
            var snapshots = history.Where(r => r.Num(field).HasValue).GroupBy(r => r.Key + (r.Linked ? "" : "|" + Normalize(r.Get("product")))).Select(g => { var date = g.Max(r => r.Date); return g.Where(r => r.Date == date).ToArray(); }).ToArray();
            if (snapshots.Length == 0) return null;
            if (snapshots.Any(g => g.Length > 1)) { warnings.Add("Aynı malzeme/ürün/iş/sipariş anahtarında aynı gün birden fazla birikimli kayıt var. Sipariş kalemi / iş emri / beden eşlemesi olmadan toplam hesaplanmadı."); return null; }
            return snapshots.Sum(g => g[0].Num(field)!.Value);
        }
        decimal? OrderSnapshot()
        {
            var groups = history.Where(r => r.Num("orderQty").HasValue).GroupBy(r => r.Get("order").Length > 0 && r.Get("line").Length > 0 ? string.Join("|", new[] { "orderType", "order", "line", "material", "variant", "size" }.Select(r.Get)) : r.Identity).ToArray();
            if (groups.Length == 0) return null;
            decimal total = 0;
            foreach (var group in groups)
            {
                var last = group.Where(r => r.Date == group.Max(r => r.Date)).ToArray();
                var quantities = last.Select(r => r.Num("orderQty")!.Value).Distinct().ToArray();
                if (quantities.Length != 1 || !last[0].Linked && last.Length > 1) return null;
                total += quantities[0];
            }
            return total;
        }
        decimal? ReadyBalance()
        {
            if (history.Any(r => r.Num("ready").HasValue))
                return history.Where(r => r.Num("pack").HasValue || r.Num("packTotal").HasValue).All(r => r.Num("ready").HasValue) ? Snapshot("ready") : null;
            var packed = history.Where(r => r.Num("pack").HasValue || r.Num("packTotal").HasValue || r.Num("approvedPack").HasValue).ToArray();
            if (packed.Length == 0 || packed.Any(r => !r.Linked || !r.Num("approvedPack").HasValue)) return null;
            decimal total = 0;
            foreach (var group in packed.GroupBy(r => r.Identity))
            {
                var shipments = unique.Where(r => r.Process == "sevkiyat" && r.Identity == group.Key && r.Date.HasValue && r.Date <= reportDate).ToArray();
                // Explicit zero shipment or opening is needed to distinguish no shipment from missing data.
                if (shipments.Length == 0 || shipments.Any(r => !r.Num("shipment").HasValue) || shipments.Min(r => r.Date) < group.Min(r => r.Date)) return null;
                var balance = Sum(group,"approvedPack") - Sum(shipments,"shipment");
                if (balance is null or < 0) return null;
                total += balance.Value;
            }
            return total;
        }
        void Add(string key, string label, decimal? value, string unit, string formula, string requirement)
        { metrics.Add(new(key, label, value, unit, formula)); if (!value.HasValue) missing.Add(requirement); }
        var breakdown = new List<CategoryKpi>(); string dailyField = "";
        if (process == "kesim")
        {
            Add("order", "Sipariş Kesim Adedi", OrderSnapshot(), "adet", "Sipariş kalemi/malzeme/beden başına son değer; operasyonlar tekrar eklenmez", "Sipariş kesim adedi ve benzersiz sipariş kalemi/iş emri/beden");
            Add("daily", "Günlük Kesim", Sum(day, "cut"), "adet", "Son veri gününün kesim hareketleri", "Kesim Adet");
            Add("total", "Toplam Kesim", Snapshot("cutTotal"), "adet", "Anahtar başına son birikimli kesim; tarihler toplanmaz", "Toplam Kesim Adedi ve benzersiz kayıt anahtarı");
            var outputs = day.Where(r => r.Num("cut").HasValue).ToArray();
            decimal quantity = 0, effort = 0; int matched = 0;
            foreach (var row in outputs)
            {
                Row? labor = row;
                decimal? PersonHours(Row r) => r.Raw.ContainsKey("effortconflict") ? null : r.Num("personHours") ?? (r.Num("employees") > 0 ? r.Num("employees") * (r.Num("hours") ?? r.Num("minutes") / 60) : null);
                if (!(PersonHours(row) > 0))
                {
                    var candidates = unique.Where(r => !r.Cost && r != row && r.Linked && row.Linked && r.Identity == row.Identity && r.Date == row.Date &&
                        r.Process == "kesim" && r.Get("center") == row.Get("center") && r.Get("shift") == row.Get("shift") &&
                        !r.Num("cut").HasValue && PersonHours(r) > 0).ToArray();
                    // A shared team total cannot be assigned once to every product/operation row.
                    labor = candidates.Length == 1 && outputs.Count(r => r.Identity == row.Identity && r.Get("center") == row.Get("center") && r.Get("shift") == row.Get("shift")) == 1 ? candidates[0] : null;
                }
                if (labor != null && PersonHours(labor) > 0) { quantity += row.Num("cut")!.Value; effort += PersonHours(labor)!.Value; matched++; }
            }
            Add("efficiency", "Çalışan Verimliliği", effort > 0 ? quantity / effort : null, "adet/kişi-saat", "Eşleşen çıktı / normal kişi-saat; mesai ayrı, grup saatleri dağıtılmaz", "Satır veya tekil emir/tarih/iş merkezi/vardiya seviyesinde çalışan ve saat");
            if (matched < outputs.Length) warnings.Add($"Kesim çalışan-saat kapsamı: {matched}/{outputs.Length} çıktı satırı. Verimlilik yalnız eşleşen çıktıyı kapsar.");
            dailyField = "cut";
        }
        if (process == "dikim")
        {
            Add("daily", "Günlük Dikim", Sum(day,"sew"), "adet", "Son veri gününün satır bazlı dikim adedi; günlük genel toplam hariç", "Günlük Dikim Adet");
            Add("total", "Toplam Dikim", Snapshot("sewTotal"), "adet", "Anahtar başına son birikimli değer", "Top Dikim Adedi ve benzersiz kayıt anahtarı");
            breakdown = day.Where(r => r.Get("center").Length > 0 && r.Num("sew").HasValue).GroupBy(r => r.Get("center")).Select(g => new CategoryKpi(g.Key, Sum(g,"sew")!.Value)).ToList();
            Add("performance", "Bant Bazlı Performans", day.Any(r => r.Num("sew").HasValue) && day.Where(r => r.Num("sew").HasValue).All(r => r.Get("center").Length > 0) ? breakdown.Sum(b => b.Quantity) : null, "adet", "Son gün bant/iş merkezi kırılımındaki dikim", "Bant / İş Merkezi kodu");
            Add("overtime", "Mesai Analizi", day.Where(r => r.Num("overtime").HasValue).All(r => r.Get("center").Length > 0) ? Sum(day,"overtime") : null, "saat", "Son gün açık Mesai Saati ve iş merkezi; birimi belirsiz Mesai kullanılmaz", "Mesai saati ve çalışan/bant eşlemesi"); dailyField = "sew";
        }
        if (process == "kalite")
        {
            var first = Sum(selected,"first"); var second = Sum(selected,"second");
            Add("first", "1. Kalite", first, "adet", "Seçili dönemde açıkça 1. kalite olarak kaydedilen adet", "1. Kalite sınıfı miktarı");
            Add("second", "2. Kalite", second, "adet", "Seçili dönemde 2. kalite kayıtları", "2. Kalite sınıfı miktarı");
            foreach (var row in selected)
            {
                if (row.Num("defectQty").HasValue && row.Get("defectName").Length > 0) breakdown.Add(new(row.Get("defectName"), row.Num("defectQty")!.Value));
                else foreach (var defect in row.Raw.Where(v => IsDefectColumn(v.Key)))
                    if (Number(defect.Value) is { } qty) breakdown.Add(new(defect.Key, qty));
            }
            breakdown = breakdown.GroupBy(b => b.Name).Select(g => new CategoryKpi(g.Key,g.Sum(b => b.Quantity))).ToList();
            Add("types", "Hata Türleri", breakdown.Count > 0 ? breakdown.Count(b => b.Quantity > 0) : null, "tür", "Miktarı bulunan hata sütunları/kodları", "Hata kodu/açıklaması ve hata adedi");
            // One inspection may have many defect lines. Its denominator is counted once.
            var qualityRows = selected.Where(r => Defects(r).HasValue || r.Num("inspection").HasValue).ToArray();
            var inspections = Enumerable.Range(0,qualityRows.Length).GroupBy(i => qualityRows[i].Get("inspectionId").Length > 0 ? qualityRows[i].Date + "|" + qualityRows[i].Identity + "|" + qualityRows[i].Get("inspectionId") : "source-row:" + i).Select(g => g.Select(i => qualityRows[i]).ToArray()).ToArray();
            decimal inspected = 0, defects = 0; bool valid = inspections.Length > 0;
            foreach (var group in inspections)
            {
                var denominators = group.Where(r => r.Num("inspection").HasValue).Select(r => r.Num("inspection")!.Value).Distinct().ToArray();
                if (denominators.Length != 1 || denominators[0] <= 0 || group.Any(r => Defects(r) is null or < 0) ||
                    group.Any(r => r.Get("unit").Length > 0 && Normalize(r.Get("unit")) is not ("ad" or "adet"))) { valid = false; continue; }
                inspected += denominators[0]; defects += group.Sum(r => Defects(r)!.Value);
            }
            Add("rate", "Hata Oranları", valid && inspected > 0 ? defects / inspected * 100 : null, "%", "Aynı kontrol kapsamındaki hata olay adedi / tekilleştirilmiş kontrol miktarı × 100", "Her hata için kontrol kimliği/kapsamı, adet birimi ve pozitif kontrol miktarı"); dailyField = "second";
            warnings.Add("Hata oranı olay/100 kontrol edilen adettir; bir üründe birden fazla hata olabilir. Paket ve 1./2. kalite toplamı kontrol paydası sayılmaz.");
        }
        if (process == "paket")
        {
            Add("daily", "Günlük Paket", Sum(day,"pack"), "adet", "Son günün satır bazlı Paket Adedi; Günlük Paket genel toplamı hariç", "Paket Adedi");
            Add("total", "Toplam Paket", Snapshot("packTotal"), "adet", "Anahtar başına son birikimli paket değeri", "Top Paket Adedi ve benzersiz kayıt anahtarı");
            Add("ready", "Sevkiyata Hazır Ürün", ReadyBalance(), "adet", "Onaylı hazır anlık bakiye veya onaylı paket hareketi − aynı emrin sevk hareketi; dönem öncesi dahil", "Sevkiyat onayı / sevkiyata hazır bakiye / sevk edilmiş miktar"); dailyField = "pack";
        }
        if (process == "fason")
        {
            var sent = Sum(selected,"sent"); var received = Sum(selected,"received");
            Add("sent", "Giden", sent, "adet", "Seçili dönem gönderim hareketleri", "Fason Giden Adet");
            Add("received", "Gelen", received, "adet", "Seçili dönem dönüş hareketleri", "Fason Gelen Adet");
            var net = sent.HasValue || received.HasValue ? (sent ?? 0) - (received ?? 0) : (decimal?)null;
            // Opening is an anchor at the start of its date, not an additive movement.
            var balances = new List<(decimal Balance, DateOnly? Due)>();
            bool complete = history.Count > 0;
            foreach (var group in history.GroupBy(r => r.Key))
            {
                var anchors = group.Where(r => r.Num("opening").HasValue && (!from.HasValue || (ParseDate(r.Get("openingDate")) ?? r.Date) <= from)).ToArray();
                var anchorDate = anchors.Select(r => ParseDate(r.Get("openingDate")) ?? r.Date).Max();
                var openingValues = anchors.Where(r => (ParseDate(r.Get("openingDate")) ?? r.Date) == anchorDate).Select(r => r.Num("opening")!.Value).Distinct().ToArray();
                if (!group.All(r => r.Linked) || openingValues.Length != 1 || anchorDate == null || anchors.Any(r => ParseDate(r.Get("openingDate")) > r.Date)) { complete = false; continue; }
                var movements = group.Where(r => r.Date >= anchorDate).ToArray();
                var balance = openingValues[0] + (Sum(movements,"sent") ?? 0) - (Sum(movements,"received") ?? 0);
                var datedDue = group.Where(r => ParseDate(r.Get("due")).HasValue).ToArray();
                var dueDates = datedDue.Where(r => r.Date == datedDue.Select(d => d.Date).Max()).Select(r => ParseDate(r.Get("due"))).Distinct().ToArray();
                balances.Add((balance, dueDates.Length == 1 ? dueDates[0] : null));
            }
            Add("balance", "Açık Bakiye", complete ? balances.Sum(b => b.Balance) : null, "adet", "Anahtar başına son açılış + açılış tarihinden rapor sonuna giden − gelen; açılış bir kez", "Her fason süreç/tedarikçi/emir/beden için tarihli açılış ve tam hareket geçmişi");
            Add("net", "Dönem Net Hareketi", net, "adet", "Seçili dönem giden − gelen; açık bakiye değildir", "Giden / gelen hareketleri");
            var asOf = to ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Istanbul").DateTime);
            Add("overdue", "Termin Aşımı", complete && balances.All(b => b.Due.HasValue) ? balances.Where(b => b.Due < asOf).Sum(b => Math.Max(0,b.Balance)) : null, "adet", "Rapor tarihinde termini geçmiş pozitif açık bakiye", "Termin/revize tarihi, açık bakiye ve sipariş/emir eşlemesi");
            breakdown = selected.GroupBy(r => r.Get("supplier").Length > 0 ? r.Get("supplier") : "Tedarikçi belirtilmemiş").Select(g => new CategoryKpi(g.Key, (Sum(g,"sent") ?? 0) - (Sum(g,"received") ?? 0))).ToList(); dailyField = "sent";
        }
        if (process == "maliyet")
        {
            var currencies = selected.Select(r => Currency(r.Get("currency"))).Distinct().ToArray();
            bool validCurrency = currencies.Length == 1 && currencies[0].Length > 0;
            var unit = validCurrency ? currencies[0] : "PB belirsiz";
            decimal? Money(string field) => validCurrency ? Sum(selected,field) : null;
            var revenue = Money("revenue"); var expense = Money("expense");
            Add("revenue", "Gelir", revenue, unit, "Açık Toplam Gelir; tek ve tanımlı para birimi; Toplam Birim ayrıca eklenmez", "Toplam Gelir ve açık para birimi");
            Add("expense", "Gider", expense, unit, "Açık Toplam Gider; normal/mesai tekrar eklenmez", "Toplam Gider ve açık para birimi");
            bool paired = selected.Where(r => r.Num("revenue").HasValue || r.Num("expense").HasValue).All(r => r.Num("revenue").HasValue && r.Num("expense").HasValue);
            Add("profit", "Kar/Zarar", paired ? revenue - expense : null, unit, "Aynı kaynak gruplarında tanımlı para birimiyle gelir − gider", "Aynı kapsamda gelir, gider ve para birimi");
            var laborRows = selected.Where(r => r.Num("normalCost").HasValue || r.Num("overtimeCost").HasValue).ToArray();
            Add("labor", "İşçilik Maliyeti", validCurrency && laborRows.Length > 0 && laborRows.All(r => r.Num("normalCost").HasValue && r.Num("overtimeCost").HasValue) ? Sum(laborRows,"normalCost") + Sum(laborRows,"overtimeCost") : null, unit, "Aynı kaynak grubunun normal + mesai gideri; boş mesai sıfır değildir", "Normal gider, açık mesai gideri (sıfır dahil) ve para birimi");
            if (!validCurrency) { missing.Add("Tutarın açık para birimi (TL/TRY, EUR, USD, GBP); sipariş/fiyat para birimi gider için varsayılmaz"); warnings.Add("Farklı, boş veya tanımsız para birimleri birlikte toplanmadı; günlük gider grafiği de kapatıldı."); }
            warnings.Add("Grup gelir/giderleri devam satırlarına dağıtılmaz. Toplam Birim, birim fiyat ve sipariş para birimleri muhasebe geliri/gideri yerine kullanılmaz.");
            dailyField = "expense";
        }
        if (process is "kesim" or "dikim" or "paket") warnings.Add("Günlük KPI seçili dönemin son veri gününü, birikimli KPI rapor sonuna kadar son bakiyeyi gösterir. Başlangıç filtresi hareketleri sınırlar; geçmiş bakiye korunur. Boş hücre sıfır sayılmaz.");
        if (excluded > 0) warnings.Add($"{excluded} mükerrer, türetilmiş, kimliksiz veya geçersiz tarihli satır KPI dışında tutuldu; staging kaydı korunuyor.");
        var moneyUnits = selected.Select(r => Currency(r.Get("currency"))).Distinct().ToArray();
        var chartRows = process == "maliyet" && (moneyUnits.Length != 1 || moneyUnits[0].Length == 0) ? new List<Row>() : selected;
        var daily = chartRows.GroupBy(r => r.Date).Select(g => new { Date = g.Key, Value = Sum(g,dailyField) }).Where(g => g.Value.HasValue).OrderBy(g => g.Date).Select(g => new CategoryKpi(g.Date!.Value.ToString("yyyy-MM-dd"),g.Value!.Value)).ToArray();
        if (breakdown.Count == 0 && dailyField.Length > 0 && process is not ("maliyet" or "dikim" or "kalite")) breakdown = selected.Where(r => r.Num(dailyField).HasValue).GroupBy(r => r.Get("product")).Select(g => new CategoryKpi(g.Key, Sum(g,dailyField)!.Value)).OrderByDescending(g => g.Quantity).Take(12).ToList();
        var detail = selected.OrderByDescending(r => r.Date).Take(100).Select(r => new ProcessRow(r.Date!.Value.ToString("yyyy-MM-dd"),r.Get("material"),r.Get("product"),r.Get("center"),r.Get("supplier"),r.File.FileName + " / " + r.Sheet,int.TryParse(r.Raw.GetValueOrDefault("row"), out var n) ? n : 0,new Dictionary<string,decimal?> { ["günlük"] = r.Num(dailyField), ["giden"] = r.Num("sent"), ["gelen"] = r.Num("received"), ["1. kalite"] = r.Num("first"), ["2. kalite"] = r.Num("second"), ["gelir"] = r.Num("revenue"), ["gider"] = r.Num("expense") }) { Order = r.Get("order"), OrderLine = r.Get("line"), WorkOrder = r.Get("workorder"), Size = r.Get("size"), MatchSource = r.MatchSource }).ToArray();
        return new(process, latest?.ToString("yyyy-MM-dd"),selected.Count,excluded,metrics,breakdown,daily,detail,missing.ToArray(),warnings.ToArray());
    }
}

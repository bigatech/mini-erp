using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DataIntakeAnalytics.Application;

namespace DataIntakeAnalytics.Infrastructure;

public sealed class SpreadsheetReader : ISpreadsheetReader
{
    public async Task<(string[] Columns, IReadOnlyList<string[]> Rows)> ReadAsync(Stream content, string extension, CancellationToken ct)
    {
        if (extension == ".csv") return await ReadCsvAsync(content, ct);
        var sheets = new List<(string Name, List<(int Number, string[] Cells)> Rows)>();
        if (extension == ".xls")
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var excel = ExcelDataReader.ExcelReaderFactory.CreateBinaryReader(content, new() { LeaveOpen = true });
            do
            {
                var rows = new List<(int, string[])>(); int number = 0;
                while (excel.Read())
                {
                    ct.ThrowIfCancellationRequested(); number++;
                    var cells = Enumerable.Range(0, excel.FieldCount).Select(i => excel.GetValue(i) switch
                    {
                        DateTime date => date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture) ?? "",
                        var value => value?.ToString() ?? ""
                    }).ToArray();
                    if (cells.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add((number, cells));
                    if (rows.Count > 100_000) throw new InvalidDataException("Sayfa satır sınırı aşıldı.");
                }
                sheets.Add((excel.Name, rows));
            } while (excel.NextResult());
            return MapSheets(sheets);
        }
        using var zip = new ZipArchive(content, ZipArchiveMode.Read, true);
        XDocument Load(string name) { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
        if (extension == ".ods")
        {
            XNamespace t = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
            XNamespace o = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
            foreach (var table in Load("content.xml").Descendants(t + "table"))
            {
                var rows = new List<(int, string[])>(); int number = 1;
                foreach (var row in table.Elements(t + "table-row"))
                {
                    ct.ThrowIfCancellationRequested();
                    var cells = new List<string>();
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName is "table-cell" or "covered-table-cell"))
                    {
                        var value = (string?)cell.Attribute(o + "date-value") ?? (string?)cell.Attribute(o + "value") ?? string.Join(" ", cell.Descendants().Where(e => e.Name.LocalName == "p").Select(e => e.Value));
                        int repeat = Math.Min(101, (int?)cell.Attribute(t + "number-columns-repeated") ?? 1);
                        for (int i = 0; i < repeat && cells.Count < 101; i++) cells.Add(value);
                    }
                    if (cells.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add((number, cells.ToArray()));
                    number += (int?)row.Attribute(t + "number-rows-repeated") ?? 1;
                    if (rows.Count > 100_000) throw new InvalidDataException("Sayfa satır sınırı aşıldı.");
                }
                sheets.Add(((string?)table.Attribute(t + "name") ?? "Sayfa", rows));
            }
        }
        else
        {
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var strings = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Load("xl/sharedStrings.xml").Descendants(s + "si").Select(e => string.Concat(e.Descendants(s + "t").Select(v => v.Value))).ToArray();
            var links = Load("xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
            foreach (var sheet in Load("xl/workbook.xml").Descendants(s + "sheet"))
            {
                var target = links[(string)sheet.Attribute(r + "id")!];
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                using var stream = zip.GetEntry(path)!.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                var rows = new List<(int, string[])>();
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row") continue;
                    using var sub = reader.ReadSubtree(); var row = XElement.Load(sub);
                    var values = new Dictionary<int, string>();
                    foreach (var cell in row.Elements(s + "c"))
                    {
                        var value = (string?)cell.Element(s + "v") ?? string.Concat(cell.Descendants(s + "t").Select(e => e.Value));
                        if ((string?)cell.Attribute("t") == "s" && int.TryParse(value, out var index)) value = strings[index];
                        if (string.IsNullOrWhiteSpace(value)) continue;
                        int col = 0; foreach (var ch in (string?)cell.Attribute("r") ?? "A") { if (!char.IsLetter(ch)) break; col = col * 26 + ch - 'A' + 1; }
                        if (col > 100) throw new InvalidDataException("Dolu sütun sayısı 100 sınırını aşıyor.");
                        values[col - 1] = value;
                    }
                    if (values.Count == 0) continue;
                    var cells = new string[values.Keys.Max() + 1]; Array.Fill(cells, ""); foreach (var v in values) cells[v.Key] = v.Value;
                    rows.Add(((int?)row.Attribute("r") ?? rows.Count + 1, cells));
                    if (rows.Count > 100_000) throw new InvalidDataException("Sayfa satır sınırı aşıldı.");
                }
                sheets.Add(((string)sheet.Attribute("name")!, rows));
            }
        }
        return MapSheets(sheets);
    }

    private static (string[] Columns, IReadOnlyList<string[]> Rows) MapSheets(List<(string Name, List<(int Number, string[] Cells)> Rows)> sheets)
    {
        var columns = new List<string> { "__sheet", "__row", "__family", "__schema", "__context_center", "__context_workorder", "__context_workorder_type", "__context_source_row" }; var mapped = new List<Dictionary<string,string>>();
        foreach (var sheet in sheets.Where(s => s.Rows.Count > 0))
        {
            int Score(string[] cells) => cells.Count(v => new[] { "tarih", "malzeme", "adet", "miktar", "gider", "gelir", "kalite", "urun", "emirno", "ismerk", "sipnum", "mno", "uretimad", "belgeno", "kalemno" }.Any(m => ProcessAnalytics.Normalize(v).Contains(m)));
            int header = Enumerable.Range(0, Math.Min(30, sheet.Rows.Count)).OrderByDescending(i => Score(sheet.Rows[i].Cells)).First();
            var seen = new Dictionary<string,int>();
            var headers = sheet.Rows[header].Cells.Select((v,i) => { var h = string.IsNullOrWhiteSpace(v) ? $"Sütun {i+1}" : v.Trim(); seen[h] = seen.GetValueOrDefault(h)+1; return seen[h] > 1 ? $"{h} [{seen[h]}]" : h; }).ToArray();
            foreach (var h in headers) if (!columns.Contains(h)) columns.Add(h);
            string sectionCenter = ""; int sectionRow = 0;
            foreach (var row in sheet.Rows.Skip(header + 1))
            {
                var family = headers.Any(h => ProcessAnalytics.Normalize(h) == "normalgider" || ProcessAnalytics.Normalize(h) == "toplamgelir") ? "cost" : "production";
                var record = new Dictionary<string,string> { ["__sheet"] = sheet.Name, ["__row"] = row.Number.ToString(), ["__family"] = family, ["__schema"] = "kpi-v2" };
                for (int i = 0; i < headers.Length; i++) record[headers[i]] = i < row.Cells.Length ? row.Cells[i] : "";
                if (ProcessAnalytics.Normalize(sheet.Name) == "konfgunlukuretintablosu")
                {
                    var person = record.FirstOrDefault(p => ProcessAnalytics.Normalize(p.Key) == "personelismerkezi").Value ?? "";
                    var section = System.Text.RegularExpressions.Regex.Match(person, @"^([A-Z]{2}\d{3})\s*-", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (section.Success) { sectionCenter = section.Groups[1].Value.ToUpperInvariant(); sectionRow = row.Number; }
                    record["__context_center"] = sectionCenter;
                    record["__context_source_row"] = sectionRow.ToString(CultureInfo.InvariantCulture);
                    var order = record.FirstOrDefault(p => ProcessAnalytics.Normalize(p.Key) == "isnotip").Value ?? "";
                    var match = System.Text.RegularExpressions.Regex.Match(order.Trim(), @"^([A-Z]\d+)-(\d+)$");
                    if (match.Success) { record["__context_workorder_type"] = match.Groups[1].Value; record["__context_workorder"] = match.Groups[2].Value; }
                }
                mapped.Add(record);
            }
        }
        // Raw cells are retained. Group effort, ambiguous durations and money are never filled down.
        return (columns.ToArray(), mapped.Select(row => columns.Select(h => row.GetValueOrDefault(h, "")).ToArray()).ToArray());
    }

    private static async Task<(string[] Columns, IReadOnlyList<string[]> Rows)> ReadCsvAsync(Stream content, CancellationToken ct)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        var records = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        char separator = text.Split('\n')[0].Count(c => c == ';') > text.Split('\n')[0].Count(c => c == ',') ? ';' : ',';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted) { if (c == '"' && i+1 < text.Length && text[i+1] == '"') { field.Append(c); i++; } else if (c == '"') quoted = false; else field.Append(c); }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == separator) { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n') { if (c == '\r' && i+1 < text.Length && text[i+1] == '\n') i++; row.Add(field.ToString()); field.Clear(); if (row.Any(v => v.Length > 0)) records.Add(row.ToArray()); row.Clear(); }
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("CSV dosyasında kapanmamış tırnak var.");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); records.Add(row.ToArray()); }
        if (records.Count == 0) return ([], []);
        if (records.Any(r => r.Length != records[0].Length)) throw new InvalidDataException("CSV sütun sayıları eşleşmiyor.");
        return (records[0], records.Skip(1).ToArray());
    }
}

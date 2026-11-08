using DataIntakeAnalytics.Application;
using UglyToad.PdfPig;
using System.Text.RegularExpressions;

namespace DataIntakeAnalytics.Infrastructure;

public sealed class PdfReader : IPdfReader
{
    public sealed record Reference(string Material, string OrderType, string Order, string Line, string WorkOrderType, string WorkOrder, string Size);

    // These are reference relations, never production, inspection or monetary movements.
    public static IReadOnlyList<Reference> ExtractReferences(string text)
    {
        if (text.Contains("KALİTE RAPORU", StringComparison.OrdinalIgnoreCase))
        {
            var material = Regex.Match(text, @"Malz\.\s*Kodu:\s*(\d{6,})");
            var order = Regex.Match(text, @"Satış\s+Siparişi:\s*([A-Z]\d+)\s+(\d+)\s+(\d+)");
            var work = Regex.Match(text, @"İş\s+Emri\s+Tipi/No:\s*([A-Z]\d+)\s+(\d+)");
            if (material.Success && order.Success && work.Success)
                return [new(material.Groups[1].Value, order.Groups[1].Value, order.Groups[2].Value, order.Groups[3].Value, work.Groups[1].Value, work.Groups[2].Value, "")];
        }
        if (text.Contains("İş Emri Kontrol Kartı", StringComparison.OrdinalIgnoreCase))
        {
            var material = Regex.Match(text, @"Malzeme\s+No:\s*(\d{6,})");
            var order = Regex.Match(text, @"Sipariş\s+No:\s*([A-Z][A-Z0-9]*)\s*-\s*(\d+)");
            var sizes = Regex.Matches(text, @"Beden:\s*([A-Z0-9]+)").Select(m=>m.Groups[1].Value).Distinct().ToArray();
            // A page with mixed sizes needs block-level parsing and remains unstructured.
            if (material.Success && order.Success && sizes.Length == 1)
                return Regex.Matches(text, @"(?<![A-Z0-9])([A-Z]\d)\s*(\d{8})(?!\d)")
                    .Select(m=>new Reference(material.Groups[1].Value,order.Groups[1].Value,order.Groups[2].Value,"",m.Groups[1].Value,m.Groups[2].Value,sizes[0])).Distinct().ToArray();
        }
        return [];
    }

    public Task<(string[] Columns, IReadOnlyList<string[]> Rows)> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(content);
        var rows = new List<string[]>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = new List<(double Y, List<(double X,string Text)> Words)>();
            foreach(var word in page.GetWords().OrderByDescending(w=>w.BoundingBox.Bottom).ThenBy(w=>w.BoundingBox.Left))
            {
                var index = lines.FindIndex(l=>Math.Abs(l.Y-word.BoundingBox.Bottom)<2.5);
                if(index<0) { lines.Add((word.BoundingBox.Bottom,[])); index=lines.Count-1; }
                lines[index].Words.Add((word.BoundingBox.Left,word.Text));
            }
            var layout = string.Join("\n",lines.OrderByDescending(l=>l.Y).Select(l=>string.Join(" ",l.Words.OrderBy(w=>w.X).Select(w=>w.Text))));
            rows.Add([page.Number.ToString(),string.IsNullOrWhiteSpace(page.Text) ? "Taranmış sayfa · OCR incelemesi gerekli" : page.Text,"PDF metni",page.Number.ToString(),"document","kpi-v2","","","","","","",""]);
            foreach(var reference in ExtractReferences(layout))
                rows.Add([page.Number.ToString(),layout,"PDF ilişkisi",page.Number.ToString(),"reference","kpi-v2",reference.Material,reference.OrderType,reference.Order,reference.Line,reference.WorkOrderType,reference.WorkOrder,reference.Size]);
        }
        if (rows.Count == 0) throw new InvalidDataException("PDF dosyasında sayfa bulunamadı.");
        return Task.FromResult<(string[] Columns, IReadOnlyList<string[]> Rows)> ((["Sayfa", "Metin", "__sheet", "__row", "__family", "__schema", "Malzeme No", "Sipariş Tipi", "Sipariş No", "Sipariş Kalem No", "İş Emri Tipi", "İş Emri No", "Beden"],rows));
    }
}

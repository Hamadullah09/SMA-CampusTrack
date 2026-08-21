using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CampusTrack.Infrastructure.Reporting;

public record ExportColumn(string Header, Func<object, object?> Value, int? Width = null);

public record ExportedFile(byte[] Content, string ContentType, string FileName);

public interface IExportService
{
    ExportedFile ToCsv<T>(IEnumerable<T> rows, IReadOnlyList<ExportColumn> columns, string fileName);
    ExportedFile ToExcel<T>(IEnumerable<T> rows, IReadOnlyList<ExportColumn> columns, string sheetName, string fileName);
    ExportedFile ToPdf<T>(IEnumerable<T> rows, IReadOnlyList<ExportColumn> columns,
        string title, string? subtitle, string fileName);
}

/// <summary>
/// Renders a report in whichever format the requester needs.
///
/// One column definition drives all three outputs, so a CSV, a spreadsheet and a printable
/// PDF of the same report can never drift apart in content or ordering - which is what
/// happens when each format is built by its own hand-written method.
/// </summary>
public class ExportService : IExportService
{
    static ExportService()
    {
        // QuestPDF's community licence covers this use; it must be declared before first use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ExportedFile ToCsv<T>(IEnumerable<T> rows, IReadOnlyList<ExportColumn> columns, string fileName)
    {
        using var buffer = new MemoryStream();

        // UTF-8 with a BOM: without it Excel opens the file as ANSI and mangles any non-ASCII
        // name, which in a school register is most of them.
        using (var writer = new StreamWriter(buffer, new System.Text.UTF8Encoding(true), leaveOpen: true))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
        {
            foreach (var column in columns) csv.WriteField(column.Header);
            csv.NextRecord();

            foreach (var row in rows)
            {
                foreach (var column in columns) csv.WriteField(Format(column.Value(row!)));
                csv.NextRecord();
            }
        }

        return new ExportedFile(buffer.ToArray(), "text/csv", EnsureExtension(fileName, ".csv"));
    }

    public ExportedFile ToExcel<T>(
        IEnumerable<T> rows, IReadOnlyList<ExportColumn> columns, string sheetName, string fileName)
    {
        using var workbook = new XLWorkbook();
        // Excel rejects sheet names over 31 characters or containing certain punctuation.
        var worksheet = workbook.Worksheets.Add(SanitiseSheetName(sheetName));

        for (var i = 0; i < columns.Count; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = columns[i].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                var value = columns[i].Value(row!);
                var cell = worksheet.Cell(rowIndex, i + 1);

                // Typed cells rather than strings, so sorting and filtering behave correctly
                // once the file is open.
                switch (value)
                {
                    case null: break;
                    case int or long or decimal or double or float:
                        cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        break;
                    case DateTime dateTime:
                        cell.Value = dateTime;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        break;
                    case DateOnly dateOnly:
                        cell.Value = dateOnly.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "yyyy-mm-dd";
                        break;
                    case bool flag:
                        cell.Value = flag ? "Yes" : "No";
                        break;
                    default:
                        cell.Value = Format(value);
                        break;
                }
            }

            rowIndex++;
        }

        worksheet.SheetView.FreezeRows(1);
        if (rowIndex > 2) worksheet.Range(1, 1, rowIndex - 1, columns.Count).SetAutoFilter();
        worksheet.Columns().AdjustToContents(minWidth: 10, maxWidth: 50);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        return new ExportedFile(buffer.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            EnsureExtension(fileName, ".xlsx"));
    }

    public ExportedFile ToPdf<T>(
        IEnumerable<T> rows, IReadOnlyList<ExportColumn> columns, string title, string? subtitle, string fileName)
    {
        var materialised = rows.ToList();
        var generatedAt = DateTime.UtcNow;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                // Wide reports need landscape; a portrait page would squeeze twelve columns
                // into something unreadable.
                page.Size(columns.Count > 6 ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Calibri));

                page.Header().Column(header =>
                {
                    header.Item().Text(title).FontSize(16).SemiBold().FontColor(Colors.Blue.Darken3);
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        header.Item().Text(subtitle).FontSize(10).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(definition =>
                    {
                        foreach (var column in columns)
                        {
                            if (column.Width is { } width) definition.ConstantColumn(width);
                            else definition.RelativeColumn();
                        }
                    });

                    table.Header(headerRow =>
                    {
                        foreach (var column in columns)
                        {
                            headerRow.Cell()
                                .Background(Colors.Grey.Lighten3)
                                .Padding(4)
                                .Text(column.Header).SemiBold();
                        }
                    });

                    var striped = false;
                    foreach (var row in materialised)
                    {
                        striped = !striped;
                        foreach (var column in columns)
                        {
                            table.Cell()
                                .Background(striped ? Colors.White : Colors.Grey.Lighten5)
                                .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                .Padding(4)
                                .Text(Format(column.Value(row!)));
                        }
                    }
                });

                page.Footer().Row(footer =>
                {
                    footer.RelativeItem().Text($"Generated {generatedAt:yyyy-MM-dd HH:mm} UTC  ·  {materialised.Count} row(s)")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    footer.ConstantItem(80).AlignRight().Text(text =>
                    {
                        text.CurrentPageNumber().FontSize(8);
                        text.Span(" / ").FontSize(8);
                        text.TotalPages().FontSize(8);
                    });
                });
            });
        });

        return new ExportedFile(document.GeneratePdf(), "application/pdf", EnsureExtension(fileName, ".pdf"));
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "Yes" : "No",
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
        double number => number.ToString("0.##", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string EnsureExtension(string fileName, string extension) =>
        fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + extension;

    private static string SanitiseSheetName(string name)
    {
        var cleaned = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}

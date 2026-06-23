using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace OneDriveMcp.Files;

public sealed record SpreadsheetData(
    string FileName,
    string? SheetName,
    IReadOnlyList<string> SheetNames,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int ReturnedRows,
    bool Truncated);

/// <summary>
/// Convertit un fichier .xlsx/.xls (ClosedXML) ou .csv (CsvHelper) en lignes/colonnes
/// exploitables par Claude.
/// </summary>
public sealed class SpreadsheetReader
{
    public SpreadsheetData Read(string fileName, byte[] content, string? sheet, int maxRows)
    {
        var isCsv = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        return isCsv
            ? ReadCsv(fileName, content, maxRows)
            : ReadExcel(fileName, content, sheet, maxRows);
    }

    private static SpreadsheetData ReadCsv(string fileName, byte[] content, int maxRows)
    {
        using var stream = new MemoryStream(content);
        using var reader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null
        };
        using var csv = new CsvReader(reader, config);

        var headers = new List<string>();
        var rows = new List<IReadOnlyList<string?>>();
        var truncated = false;
        var dataRowCount = 0;

        while (csv.Read())
        {
            var record = new List<string?>();
            for (var i = 0; i < csv.Parser.Count; i++)
                record.Add(csv.GetField(i));

            if (headers.Count == 0)
            {
                headers.AddRange(record.Select(c => c ?? string.Empty));
                continue;
            }

            if (dataRowCount >= maxRows)
            {
                truncated = true;
                break;
            }

            rows.Add(record);
            dataRowCount++;
        }

        return new SpreadsheetData(fileName, null, Array.Empty<string>(), headers, rows, rows.Count, truncated);
    }

    private static SpreadsheetData ReadExcel(string fileName, byte[] content, string? sheet, int maxRows)
    {
        using var stream = new MemoryStream(content);
        using var workbook = new XLWorkbook(stream);

        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();

        var worksheet = !string.IsNullOrWhiteSpace(sheet)
            ? workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheet, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"Feuille '{sheet}' introuvable. Feuilles disponibles : {string.Join(", ", sheetNames)}.")
            : workbook.Worksheets.First();

        var used = worksheet.RangeUsed();
        if (used is null)
        {
            return new SpreadsheetData(fileName, worksheet.Name, sheetNames,
                Array.Empty<string>(), Array.Empty<IReadOnlyList<string?>>(), 0, false);
        }

        var allRows = used.Rows().ToList();
        var headers = allRows[0].Cells().Select(c => c.GetFormattedString()).ToList();

        var rows = new List<IReadOnlyList<string?>>();
        var truncated = false;

        foreach (var row in allRows.Skip(1))
        {
            if (rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }
            rows.Add(row.Cells(1, headers.Count).Select(c => (string?)c.GetFormattedString()).ToList());
        }

        return new SpreadsheetData(fileName, worksheet.Name, sheetNames, headers, rows, rows.Count, truncated);
    }
}

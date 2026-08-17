using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Categories;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Products;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// §9.28. CSV import/export for the catalog. Onboarding a business with 2,000 SKUs was 2,000
/// individual API calls before this — and the Growth plan's 2,000-product cap is reachable by
/// exactly the kind of tenant who will not type them in one at a time.
///
/// Admin-tier only: a bad import can rewrite every price in the catalog at once.
/// </summary>
[Tags("BackOffice - Products")]
[Route("api/businesses/{businessId}/products")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
[Authorize(Policy = "BusinessMember")]
public class ProductBulkController(
    ICurrentUserContext currentUser,
    IProductService productService,
    ICategoryService categoryService) : VastoraControllerBase(currentUser)
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private static readonly string[] Columns =
        ["sku", "name", "category", "description", "price", "costPrice", "stockQuantity", "brand", "barcode", "weightKg", "tags"];

    /// <summary>
    /// Upserts by SKU. Rows referencing a category that doesn't exist are skipped and reported
    /// rather than silently creating categories — an import typo should not reshape the catalog's
    /// taxonomy.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<ProductImportResult>> Import(string businessId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { title = "No file uploaded.", status = StatusCodes.Status400BadRequest });
        }

        var categories = await categoryService.GetForBusinessAsync(businessId, ct);
        var categoryIdsByName = categories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var (rows, parseErrors) = ParseCsv(await reader.ReadToEndAsync(ct));

        var result = await productService.ImportAsync(ResolvedTenantId, businessId, rows, categoryIdsByName, ct);

        // Parse failures and business-rule failures are the same thing from the caller's side —
        // "this row didn't land, here's why" — so they're merged into one report.
        return Ok(result with
        {
            Skipped = result.Skipped + parseErrors.Count,
            Errors = [.. result.Errors.Concat(parseErrors)]
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(string businessId, CancellationToken ct)
    {
        var categories = await categoryService.GetForBusinessAsync(businessId, ct);
        var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);

        var rows = await productService.ExportAsync(businessId, categoryNamesById, ct);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', Columns));

        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',',
                Escape(row.Sku), Escape(row.Name), Escape(row.CategoryName), Escape(row.Description),
                Format(row.Price), Format(row.CostPrice), row.StockQuantity.ToString(CultureInfo.InvariantCulture),
                Escape(row.Brand), Escape(row.Barcode), Format(row.WeightKg), Escape(row.Tags)));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"products-{businessId}.csv");
    }

    /// <summary>
    /// A deliberately small CSV reader rather than a parser dependency: the format here is fixed
    /// and self-imposed (this endpoint's own export is its reference input). It handles quoted
    /// fields and escaped quotes, which is what a spreadsheet actually produces.
    /// </summary>
    private static (List<ProductImportRow> Rows, List<string> Errors) ParseCsv(string content)
    {
        var rows = new List<ProductImportRow>();
        var errors = new List<string>();

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count <= 1)
        {
            errors.Add("The file has a header but no data rows.");
            return (rows, errors);
        }

        var header = SplitLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var index = Columns.ToDictionary(c => c, c => header.IndexOf(c.ToLowerInvariant()));

        foreach (var required in new[] { "sku", "name", "category", "price" })
        {
            if (index[required] < 0)
            {
                errors.Add($"Required column '{required}' is missing. Expected header: {string.Join(", ", Columns)}");
                return (rows, errors);
            }
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var fields = SplitLine(lines[i]);

            string Field(string column)
            {
                var position = index[column];
                return position >= 0 && position < fields.Count ? fields[position].Trim() : string.Empty;
            }

            if (!decimal.TryParse(Field("price"), NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                errors.Add($"Row {i + 1}: '{Field("price")}' is not a valid price.");
                continue;
            }

            rows.Add(new ProductImportRow(
                Field("sku"), Field("name"), Field("category"), Field("description"), price,
                ParseNullableDecimal(Field("costPrice")),
                int.TryParse(Field("stockQuantity"), out var stock) ? stock : 0,
                Field("brand"), Field("barcode"),
                ParseNullableDecimal(Field("weightKg")),
                Field("tags")));
        }

        return (rows, errors);
    }

    private static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                // A doubled quote inside a quoted field is a literal quote — the spreadsheet
                // convention, and the one this endpoint's own export emits.
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static decimal? ParseNullableDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string Format(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}

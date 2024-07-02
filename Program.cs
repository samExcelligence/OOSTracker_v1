using OOSWebScraper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using OOSWebScraper.models;
using OOSWebScrapper.Models;
using OOSWebScrapper;

class Program
{
    private const string CheckpointFile = "scraper_checkpoint.json";
    private const string PreviousResultsFile = "previous_results.json";

    static async Task Main(string[] args)
    {
        List<OOSItemDetails> outOfStockItems = new List<OOSItemDetails>();

        try
        {
            Console.WriteLine("Out of Stock Tracker");
            Console.WriteLine("----------------------");

            string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
            string catalogName = string.Empty;
            CatalogSelectors selectors = null;
            string catalogType = string.Empty;

            Console.WriteLine("Select the report type:");
            Console.WriteLine("1. Out of Stock Report");
            Console.WriteLine("2. Combined Report (New, Coming Soon, Clearance)");

            string reportChoice = Console.ReadLine();

            Console.WriteLine("Select the catalog to scan:");
            Console.WriteLine("1. Discount School Supply (DSS)");
            Console.WriteLine("2. Really Good Stuff (RGS)");

            string companyChoice = Console.ReadLine();

            switch (companyChoice)
            {
                case "1":
                    catalogType = "DSS";
                    selectors = new DssCatalogSelectors();
                    break;
                case "2":
                    catalogType = "RGS";
                    selectors = new RgsCatalogSelectors();
                    break;
                default:
                    Console.WriteLine("Invalid choice. Exiting program.");
                    return;
            }

            switch (reportChoice)
            {
                case "1":
                    await HandleOOSReportAsync(chromePath, selectors, catalogType);
                    break;
                case "2":
                    await HandleCombinedReportAsync(chromePath, selectors, catalogType);
                    break;
                default:
                    Console.WriteLine("Invalid choice. Exiting program.");
                    return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }

    private static async Task HandleOOSReportAsync(string chromePath, CatalogSelectors selectors, string catalogType)
    {
        string targetUrl = string.Empty;
        string catalogName = string.Empty;

        switch (catalogType)
        {
            case "DSS":
                targetUrl = "https://www.discountschoolsupply.com/search/?q=*%3Arelevance%3Abadges%3AOut%2Bof%2BStock&text=*#";
                catalogName = "DSS_OutOfStock";
                break;
            case "RGS":
                targetUrl = "https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3AOut%2BOf%2BStock&text=*#";
                catalogName = "RGS_OutOfStock";
                break;
        }

        var scraper = new OutOfStockScraper(chromePath, targetUrl, 1000, selectors, catalogType, catalogName);

        List<OOSItemDetails> outOfStockItems = new List<OOSItemDetails>();

        if (File.Exists(CheckpointFile))
        {
            Console.WriteLine("Checkpoint file found. Do you want to resume from the last saved position? (Y/N)");
            if (Console.ReadKey().Key == ConsoleKey.Y)
            {
                Console.WriteLine("\nResuming scan...");
                outOfStockItems = await scraper.ScrapeOutOfStockItemsAsync();

                // Merge with previous results if they exist
                if (File.Exists(PreviousResultsFile))
                {
                    var previousItems = JsonSerializer.Deserialize<List<OOSItemDetails>>(File.ReadAllText(PreviousResultsFile));
                    outOfStockItems.InsertRange(0, previousItems);
                    Console.WriteLine($"Merged {previousItems.Count} previously scanned items.");
                }
            }
            else
            {
                Console.WriteLine("\nStarting new scan...");
                File.Delete(CheckpointFile);
                File.Delete(PreviousResultsFile);
                outOfStockItems = await scraper.ScrapeOutOfStockItemsAsync();
            }
        }
        else
        {
            Console.WriteLine("Starting new scan...");
            outOfStockItems = await scraper.ScrapeOutOfStockItemsAsync();
        }

        // Remove duplicates
        outOfStockItems = RemoveDuplicates(outOfStockItems);

        // Generate Excel file
        GenerateExcelReport(outOfStockItems, catalogName);
    }

    private static async Task HandleCombinedReportAsync(string chromePath, CatalogSelectors selectors, string catalogType)
    {
        var categories = new List<(string url, string badge, string stockStatus)>
        {
            ("https://www.discountschoolsupply.com/search?q=*%3Arelevance%3Abadges%3ANew&text=*#", "New", "In Stock"),
            ("https://www.discountschoolsupply.com/search?q=*%3Arelevance%3Abadges%3AComing%2BSoon&text=*#", "Coming Soon", "Out of Stock"),
            ("https://www.discountschoolsupply.com/search?q=*%3Arelevance%3Abadges%3AClearance&text=*#", "Clearance", "In Stock"),
            ("https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3ANew&text=*#", "New", "In Stock"),
            ("https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3AComing%2BSoon&text=*#", "Coming Soon", "Out of Stock"),
            ("https://www.reallygoodstuff.com/search?q=*%3Arelevance%3Ap_featuredCollection%3AClearance&text=*#", "Clearance", "In Stock")
        };

        var filteredCategories = categories.Where(c => (catalogType == "DSS" && c.url.Contains("discountschoolsupply")) ||
                                                       (catalogType == "RGS" && c.url.Contains("reallygoodstuff"))).ToList();

        var scraper = new CombinedReportScraper(chromePath, 1000, selectors, catalogType);
        var combinedItems = await scraper.ScrapeCombinedItemsAsync(filteredCategories);

        string combinedCatalogName = $"{catalogType}_Combined";
        var package = new ExcelPackage();

        foreach (var badge in filteredCategories.Select(c => c.badge).Distinct())
        {
            var itemsForBadge = combinedItems.Where(item => item.Badge == badge).ToList();
            GenerateExcelSheet(package, itemsForBadge, badge);
        }

        // Save the Excel file with separate sheets
        SaveExcelPackage(package, combinedCatalogName);
    }

    public static List<OOSItemDetails> RemoveDuplicates(List<OOSItemDetails> items)
    {
        var uniqueItems = new HashSet<string>();
        var result = new List<OOSItemDetails>();

        foreach (var item in items)
        {
            if (uniqueItems.Add(item.UPID + item.ItemURL))
            {
                result.Add(item);
            }
        }

        return result;
    }

    public static void GenerateExcelSheet(ExcelPackage package, List<OOSItemDetails> items, string sheetName)
    {
        var worksheet = package.Workbook.Worksheets.Add(sheetName);

        // Headers
        worksheet.Cells[1, 1].Value = "Item Name";
        worksheet.Cells[1, 2].Value = "UPID";
        worksheet.Cells[1, 3].Value = "Parent UPID";  // New column
        worksheet.Cells[1, 4].Value = "Stock Status";
        worksheet.Cells[1, 5].Value = "Badge"; // New column for badge
        worksheet.Cells[1, 6].Value = "Retrieved At";
        worksheet.Cells[1, 7].Value = "Page Number";
        worksheet.Cells[1, 8].Value = "Position On Page";
        worksheet.Cells[1, 9].Value = "Has Variations";
        worksheet.Cells[1, 10].Value = "Item URL";

        // Style headers
        using (var range = worksheet.Cells[1, 1, 1, 10])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
        }

        // Populate data
        int row = 2;
        foreach (var item in items)
        {
            worksheet.Cells[row, 1].Value = item.ItemName;
            worksheet.Cells[row, 2].Value = item.UPID;
            worksheet.Cells[row, 3].Value = "";  // Parent item has no parent UPID
            worksheet.Cells[row, 4].Value = item.StockStatus;
            worksheet.Cells[row, 5].Value = item.Badge; // Badge value
            worksheet.Cells[row, 6].Value = item.RetrievedAt.ToString("g");
            worksheet.Cells[row, 7].Value = item.PageNumber;
            worksheet.Cells[row, 8].Value = item.PositionOnPage;
            worksheet.Cells[row, 9].Value = item.HasVariations ? "Yes" : "No";
            worksheet.Cells[row, 10].Value = item.ItemURL;

            // Highlight parent item
            using (var range = worksheet.Cells[row, 1, row, 10])
            {
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
            }
            row++;

            // Add variations if they exist
            if (item.HasVariations && item.Variations != null)
            {
                foreach (var variation in item.Variations)
                {
                    worksheet.Cells[row, 1].Value = "    " + variation.Name;
                    worksheet.Cells[row, 2].Value = variation.UPID;
                    worksheet.Cells[row, 3].Value = item.UPID;  // Parent UPID
                    worksheet.Cells[row, 4].Value = variation.StockStatus;
                    worksheet.Cells[row, 5].Value = item.Badge; // Badge value
                    worksheet.Cells[row, 6].Value = "";
                    worksheet.Cells[row, 7].Value = "";
                    worksheet.Cells[row, 8].Value = "";
                    worksheet.Cells[row, 9].Value = "";
                    worksheet.Cells[row, 10].Value = "";

                    // Highlight variation
                    using (var range = worksheet.Cells[row, 1, row, 10])
                    {
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.LightYellow);
                    }
                    row++;
                }
            }
        }

        // Auto-fit columns
        worksheet.Cells.AutoFitColumns();
    }

    public static void SaveExcelPackage(ExcelPackage package, string catalogName)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var dateString = DateTime.Now.ToString("yyyyMMdd");
        var filePath = Path.Combine(desktopPath, $"{catalogName}_{dateString}.xlsx");
        File.WriteAllBytes(filePath, package.GetAsByteArray());
        Console.WriteLine($"Excel report generated: {filePath}");
    }

    public static void GenerateExcelReport(List<OOSItemDetails> items, string catalogName)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using (var package = new ExcelPackage())
        {
            GenerateExcelSheet(package, items, "Out of Stock Items");
            SaveExcelPackage(package, catalogName);
        }
    }
}

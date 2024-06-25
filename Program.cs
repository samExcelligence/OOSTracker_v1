using OOSWebScraper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using OOSWebScraper.models;

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
            string targetUrl = string.Empty;
            string catalogName = string.Empty;

            Console.WriteLine("Select the catalog to scan: ");
            Console.WriteLine("1. Discount School Supply (DSS) Out of Stock Catalog");
            Console.WriteLine("2. Really Good Stuff (RGS) Out of Stock Catalog");
            Console.WriteLine("3. Really Good Stuff (RGS) Coming Soon Catalog");

            string choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    targetUrl = "https://www.discountschoolsupply.com/search/?q=*%3Arelevance%3Abadges%3AOut%2Bof%2BStock&text=*#";
                    catalogName = "DSS_OutOfStock";
                    break;
                case "2":
                    targetUrl = "https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3AOut%2BOf%2BStock&text=*#";
                    catalogName = "RGS_OutOfStock";
                    break;
                case "3":
                    targetUrl = "https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3AComing%2BSoon&text=*#";
                    catalogName = "RGS_ComingSoon";
                    break;
                default:
                    Console.WriteLine("Invalid choice. Exiting program.");
                    return;
            }

            var throttleDelay = 1000;
            var scraper = new OutOfStockScraper(chromePath, targetUrl, throttleDelay);

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

            // Calculate summary statistics
            var outOfStockVariationsCount = outOfStockItems.Sum(item => item.Variations.Count(v => v.IsOutOfStock));
            var outOfStockParentCount = outOfStockItems.Count(item => item.StockStatus.Contains("Out of Stock") && !item.HasVariations);
            var totalItemsScanned = outOfStockItems.Count;

            Console.WriteLine($"Scan completed successfully.");
            Console.WriteLine($"Total items scanned: {totalItemsScanned}");
            Console.WriteLine($"Total out-of-stock parent items (without variations): {outOfStockParentCount}");
            Console.WriteLine($"Total out-of-stock variations: {outOfStockVariationsCount}");
            Console.WriteLine($"Date of scan: {DateTime.Now}");

            // Generate Excel file
            GenerateExcelReport(outOfStockItems, catalogName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }
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

    public static void GenerateExcelReport(List<OOSItemDetails> items, string catalogName)
    {
        // Set the EPPlus license context
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Out of Stock Items");

            // Headers
            worksheet.Cells[1, 1].Value = "Item Name";
            worksheet.Cells[1, 2].Value = "UPID";
            worksheet.Cells[1, 3].Value = "Stock Status";
            worksheet.Cells[1, 4].Value = "Retrieved At";
            worksheet.Cells[1, 5].Value = "Page Number";
            worksheet.Cells[1, 6].Value = "Position On Page";
            worksheet.Cells[1, 7].Value = "Has Variations";
            worksheet.Cells[1, 8].Value = "Item URL";

            // Style headers
            using (var range = worksheet.Cells[1, 1, 1, 8])
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
                worksheet.Cells[row, 3].Value = item.StockStatus;
                worksheet.Cells[row, 4].Value = item.RetrievedAt.ToString("g");
                worksheet.Cells[row, 5].Value = item.PageNumber;
                worksheet.Cells[row, 6].Value = item.PositionOnPage;
                worksheet.Cells[row, 7].Value = item.HasVariations ? "Yes" : "No";
                worksheet.Cells[row, 8].Value = item.ItemURL;

                // Highlight parent item
                using (var range = worksheet.Cells[row, 1, row, 8])
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
                        worksheet.Cells[row, 1].Value = "    " + variation.Name; // Indent to show it's a variation
                        worksheet.Cells[row, 2].Value = variation.UPID;
                        worksheet.Cells[row, 3].Value = variation.StockStatus;
                        worksheet.Cells[row, 4].Value = "";
                        worksheet.Cells[row, 5].Value = "";
                        worksheet.Cells[row, 6].Value = "";
                        worksheet.Cells[row, 7].Value = "";
                        worksheet.Cells[row, 8].Value = "";

                        // Highlight variation
                        using (var range = worksheet.Cells[row, 1, row, 8])
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

            // Get the desktop path
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var dateString = DateTime.Now.ToString("yyyyMMdd");
            var filePath = Path.Combine(desktopPath, $"{catalogName}_{dateString}.xlsx");

            // Save the Excel file
            File.WriteAllBytes(filePath, package.GetAsByteArray());
            Console.WriteLine($"Excel report generated: {filePath}");
        }
    }
}

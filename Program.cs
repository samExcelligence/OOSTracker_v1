using OOSWebScraper;
using OOSWebScraper.models;
using OOSWebScrapper.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using System.Diagnostics;

namespace OOSWebScrapper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Stopwatch stopwatch = new Stopwatch(); // Create a new Stopwatch
            stopwatch.Start();

            try
            {
                Console.WriteLine("Out of Stock Tracker");
                Console.WriteLine("----------------------");

                string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
                string targetUrl = string.Empty;
                string catalogName = string.Empty;
                CatalogSelectors selectors = null;
                string catalogType = string.Empty;

                if (args.Length == 0)
                {
                    Console.WriteLine("Please provide the catalog type as an argument.");
                    Console.WriteLine("Usage: dotnet run -- <catalog_type>");
                    Console.WriteLine("Catalog types: 1 (DSS_OutOfStock), 2 (DSS_Combined), 3 (RGS_OutOfStock), 4 (RGS_Combined)");
                    return;
                }
                // for debugging uncomment this
                //Console.WriteLine("Select the catalog to scan:");
                //Console.WriteLine("***Discount School Supply (DSS) Catalogs***");
                //Console.WriteLine("1. Discount School Supply (DSS) Out of Stock Catalog");
                //Console.WriteLine("2. Discount School Supply (DSS) Combined New/Coming Soon/Clearance Catalog");
                //Console.WriteLine("***Really Good Stuff (RGS) Catalogs***");
                //Console.WriteLine("3. Really Good Stuff (RGS) Out of Stock Catalog");
                //Console.WriteLine("4. Really Good Stuff (RGS) Combined New/Coming Soon/Clearance Catalog");

                //string choice = Console.ReadLine();
                string choice = args[0];

                switch (choice)
                {
                    case "1":
                        targetUrl = "https://www.discountschoolsupply.com/search/?q=*%3Arelevance%3Abadges%3AOut%2Bof%2BStock&text=*#";
                        catalogName = "DSS_OutOfStock";
                        selectors = new DssCatalogSelectors();
                        catalogType = "DSS";
                        await RunSingleCatalogScraper(chromePath, targetUrl, selectors, catalogType, catalogName);
                        break;
                    case "2":
                        catalogName = "DSS_Combined";
                        selectors = new DssCatalogSelectors();
                        catalogType = "DSS";
                        await RunCombinedCatalogScraper(chromePath, selectors, catalogType, catalogName);
                        break;
                    case "3":
                        targetUrl = "https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3AOut%2BOf%2BStock&text=*#";
                        catalogName = "RGS_OutOfStock";
                        selectors = new RgsCatalogSelectors();
                        catalogType = "RGS";
                        await RunSingleCatalogScraper(chromePath, targetUrl, selectors, catalogType, catalogName);
                        break;
                    case "4":
                        catalogName = "RGS_Combined";
                        selectors = new RgsCatalogSelectors();
                        catalogType = "RGS";
                        await RunCombinedCatalogScraper(chromePath, selectors, catalogType, catalogName);
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
                stopwatch.Stop(); // Stop the timer
                TimeSpan ts = stopwatch.Elapsed;
                string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                Console.WriteLine($"Total execution time: {elapsedTime}");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
            }
        }

        static async Task RunSingleCatalogScraper(string chromePath, string targetUrl, CatalogSelectors selectors, string catalogType, string catalogName)
        {

            var throttleDelay = 1000;
            var scraper = new OutOfStockScraper(chromePath, targetUrl, throttleDelay, selectors, catalogType, catalogName);
            var outOfStockItems = await scraper.ScrapeOutOfStockItemsAsync();
            GenerateExcelReport(outOfStockItems, catalogName, false);
        }

        static async Task RunCombinedCatalogScraper(string chromePath, CatalogSelectors selectors, string catalogType, string catalogName)
        {
            // For testing mode:
            bool isTestingMode = true;
            int testingItemsPerPage = 3;
            int testingMaxPages = 2;
            var throttleDelay = 1000;

            var scraper = new CombinedReportScraper(
                                                    chromePath,
                                                    throttleDelay,
                                                    selectors,
                                                    catalogType,
                                                    isTestingMode,
                                                    testingItemsPerPage,
                                                    testingMaxPages
                                                );

            List<(string url, string badge, string stockStatus)> categories = new List<(string, string, string)>();
            if (catalogType == "DSS")
            {
                categories.Add(("https://www.discountschoolsupply.com/search/?q=*%3Arelevance%3Abadges%3ANew&text=*#", "New", "In Stock"));
                categories.Add(("https://www.discountschoolsupply.com/search/?q=*%3Arelevance%3Abadges%3AComing%2BSoon&text=*#", "Coming Soon", "Coming Soon"));
                categories.Add(("https://www.discountschoolsupply.com/search/?q=*%3Arelevance%3Abadges%3AClearance&text=*#", "Clearance", "In Stock"));
            }
            else if (catalogType == "RGS")
            {
                categories.Add(("https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3ANew&text=*#", "New", "In Stock"));
                categories.Add(("https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3AComing%2BSoon&text=*#", "Coming Soon", "Coming Soon"));
                categories.Add(("https://www.reallygoodstuff.com/search/?q=*%3Arelevance%3Ap_featuredCollection%3AClearance&text=*#", "Clearance", "In Stock"));
            }

            var combinedItems = await scraper.ScrapeCombinedItemsAsync(categories);
            GenerateExcelReport(combinedItems, catalogName, true);
        }

        public static void GenerateExcelReport(List<OOSItemDetails> items, string catalogName, bool isCombinedReport = false)
        {
            using (var workbook = new XLWorkbook())
            {
                if (isCombinedReport)
                {
                    GenerateCombinedReport(workbook, items, catalogName);
                }
                else
                {
                    GenerateSingleReport(workbook, items, catalogName);
                }

                // Save the Excel file
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var dateString = DateTime.Now.ToString("yyyyMMdd");
                var filePath = Path.Combine(desktopPath, $"{catalogName}_{dateString}.xlsx");
                
                try
                {
                    workbook.SaveAs(filePath);
                    Console.WriteLine($"Excel report generated: {filePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving Excel file: {ex.Message}");
                    var alternatePath = Path.Combine(Path.GetTempPath(), $"{catalogName}_{dateString}.xlsx");
                    workbook.SaveAs(alternatePath);
                    Console.WriteLine($"Excel report generated at alternate location: {alternatePath}");
                }
            }
        }

        private static void GenerateSingleReport(XLWorkbook workbook, List<OOSItemDetails> items, string sheetName)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            PopulateWorksheet(worksheet, items, true);
        }

        private static void GenerateCombinedReport(XLWorkbook workbook, List<OOSItemDetails> items, string catalogName)
        {
            var newItems = items.Where(i => i.Badge == "New").ToList();
            var comingSoonItems = items.Where(i => i.Badge == "Coming Soon").ToList();
            var clearanceItems = items.Where(i => i.Badge == "Clearance").ToList();

            GenerateSingleReport(workbook, newItems, "New Items");
            GenerateSingleReport(workbook, comingSoonItems, "Coming Soon Items");
            GenerateSingleReport(workbook, clearanceItems, "Clearance Items");

            // Generate summary sheet
            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell("A1").Value = "Category";
            summarySheet.Cell("B1").Value = "Item Count";
            summarySheet.Cell("A2").Value = "New";
            summarySheet.Cell("B2").Value = newItems.Count;
            summarySheet.Cell("A3").Value = "Coming Soon";
            summarySheet.Cell("B3").Value = comingSoonItems.Count;
            summarySheet.Cell("A4").Value = "Clearance";
            summarySheet.Cell("B4").Value = clearanceItems.Count;
            summarySheet.Cell("A5").Value = "Total";
            summarySheet.Cell("B5").FormulaA1 = "SUM(B2:B4)";

            summarySheet.Columns().AdjustToContents();
        }

        private static void PopulateWorksheet(IXLWorksheet worksheet, List<OOSItemDetails> items, bool includeBadgeColumn)
        {
            // Headers
            worksheet.Cell(1, 1).Value = "Item Name";
            worksheet.Cell(1, 2).Value = "UPID";
            worksheet.Cell(1, 3).Value = "Parent UPID";
            worksheet.Cell(1, 4).Value = "Stock Status";
            worksheet.Cell(1, 5).Value = "Retrieved At";
            worksheet.Cell(1, 6).Value = "Page Number";
            worksheet.Cell(1, 7).Value = "Position On Page";
            worksheet.Cell(1, 8).Value = "Has Variations";
            worksheet.Cell(1, 9).Value = "Item URL";
            if (includeBadgeColumn)
            {
                worksheet.Cell(1, 10).Value = "Badge";
            }

            // Style headers
            var headerRange = worksheet.Range(1, 1, 1, includeBadgeColumn ? 10 : 9);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Populate data
            int row = 2;
            foreach (var item in items)
            {
                worksheet.Cell(row, 1).Value = item.ItemName;
                worksheet.Cell(row, 2).Value = item.UPID;
                worksheet.Cell(row, 3).Value = "";  // Parent item has no parent UPID
                worksheet.Cell(row, 4).Value = item.StockStatus;
                worksheet.Cell(row, 5).Value = item.RetrievedAt.ToString("g");
                worksheet.Cell(row, 6).Value = item.PageNumber;
                worksheet.Cell(row, 7).Value = item.PositionOnPage;
                worksheet.Cell(row, 8).Value = item.HasVariations ? "Yes" : "No";
                worksheet.Cell(row, 9).Value = item.ItemURL;
                if (includeBadgeColumn)
                {
                    worksheet.Cell(row, 10).Value = item.Badge;
                }

                // Highlight parent item
                var parentRange = worksheet.Range(row, 1, row, includeBadgeColumn ? 10 : 9);
                parentRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
                row++;

                // Add variations if they exist
                if (item.HasVariations && item.Variations != null)
                {
                    foreach (var variation in item.Variations)
                    {
                        worksheet.Cell(row, 1).Value = "    " + variation.Name;
                        worksheet.Cell(row, 2).Value = variation.UPID;
                        worksheet.Cell(row, 3).Value = item.UPID;  // Parent UPID
                        worksheet.Cell(row, 4).Value = variation.StockStatus;
                        worksheet.Cell(row, 5).Value = "";
                        worksheet.Cell(row, 6).Value = "";
                        worksheet.Cell(row, 7).Value = "";
                        worksheet.Cell(row, 8).Value = "";
                        worksheet.Cell(row, 9).Value = "";
                        if (includeBadgeColumn)
                        {
                            worksheet.Cell(row, 10).Value = variation.Badge;
                        }

                        // Highlight variation
                        var variationRange = worksheet.Range(row, 1, row, includeBadgeColumn ? 10 : 9);
                        variationRange.Style.Fill.BackgroundColor = XLColor.LightYellow;
                        row++;
                    }
                }
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();
        }
    }
}
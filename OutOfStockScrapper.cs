using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PuppeteerSharp;
using System.Linq;
using System.Text.Json;
using System.IO;
using OOSWebScraper.models;


namespace OOSWebScraper
{
    public class OutOfStockScraper
    {
        private readonly string _chromePath;
        private readonly string _url;
        private readonly int _throttleDelay;
        private readonly Random _random = new Random();
        private const int MaxRetries = 3;
        private const string CheckpointFile = "scraper_checkpoint.json";
        private const string PreviousResultsFile = "previous_results.json";

        public OutOfStockScraper(string chromePath, string url, int throttleDelay)
        {
            _chromePath = chromePath;
            _url = url;
            _throttleDelay = throttleDelay;
        }

        public async Task<List<OOSItemDetails>> ScrapeOutOfStockItemsAsync()
        {
            var outOfStockItems = new List<OOSItemDetails>();
            IBrowser browser = null;
            IPage mainPage = null;
            IPage itemPage = null;
            int startPage = 0;
            int startPosition = 0;

            if (File.Exists(CheckpointFile))
            {
                Console.WriteLine("Checkpoint file found. Do you want to resume from the last saved position? (Y/N)");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                {
                    var checkpoint = JsonSerializer.Deserialize<ScraperCheckpoint>(File.ReadAllText(CheckpointFile));
                    startPage = checkpoint.LastPageScraped;
                    startPosition = checkpoint.LastPositionScraped;
                    Console.WriteLine($"\nResuming from page {startPage}, position {startPosition}");

                    if (File.Exists(PreviousResultsFile))
                    {
                        outOfStockItems = JsonSerializer.Deserialize<List<OOSItemDetails>>(File.ReadAllText(PreviousResultsFile));
                        Console.WriteLine($"Loaded {outOfStockItems.Count} previously scraped items.");
                    }
                }
                else
                {
                    Console.WriteLine("\nStarting from the beginning.");
                    File.Delete(CheckpointFile);
                    File.Delete(PreviousResultsFile);
                }
            }

            try
            {
                Console.WriteLine("Starting the browser...");

                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = _chromePath,
                    Args = new string[]
                    {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-web-security",
                "--disable-features=IsolateOrigins,site-per-process",
                "--disable-blink-features=AutomationControlled",
                "--window-size=1920,1080"
                    },
                    Timeout = 120000
                });

                Console.WriteLine("Browser started successfully.");

                mainPage = await browser.NewPageAsync();
                itemPage = await browser.NewPageAsync();
                mainPage.DefaultNavigationTimeout = 120000;
                itemPage.DefaultNavigationTimeout = 120000;

                await mainPage.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                await mainPage.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
                await mainPage.EvaluateExpressionAsync(@"navigator.webdriver = undefined");

                bool hasNextPage;
                int pageNumber = startPage;
                Console.WriteLine($"Navigating to {_url} (page {pageNumber + 1})...");
                await mainPage.GoToAsync($"{_url}&page={pageNumber}&pageSize=");

                do
                {
                    await mainPage.WaitForSelectorAsync("div.product-grid", new WaitForSelectorOptions { Timeout = 60000 });
                    Console.WriteLine("Navigation completed and product grid loaded.");

                    var nextPageDisabledJsPath = "document.querySelector('main div.product-grid div.grid-content div.pagination-bar.top div.pagination-wrap.hidden-sm.hidden-xs ul > li.pagination-next.disabled.img-link')";
                    var nextPageJsPath = "document.querySelector('main div.product-grid div.grid-content div.pagination-bar.top div.pagination-wrap.hidden-sm.hidden-xs ul > li.pagination-next.img-link > a')";
                    hasNextPage = !await mainPage.EvaluateExpressionAsync<bool>($"{nextPageDisabledJsPath} !== null");
                    Console.WriteLine($"HAS NEXT PAGE: {hasNextPage}");

                    string nextPageUrl = null;
                    if (hasNextPage)
                    {
                        nextPageUrl = await mainPage.EvaluateExpressionAsync<string>($"{nextPageJsPath}.href");
                        Console.WriteLine($"Next page URL: {nextPageUrl}");
                    }


                    var parentItems = new List<(string URL, string Title, bool HasVariations, int Position)>();
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            var itemElements = await mainPage.QuerySelectorAllAsync("body > main > div.container.main__inner-wrapper > div.product-grid > div.grid-content > div > ul > div.product-item");
                            int itemCount = itemElements.Count();
                            for (int i = 0; i < itemCount; i++)
                            {
                                var itemElement = itemElements[i];
                                try
                                {
                                    var linkElement = await itemElement.QuerySelectorAsync("a");
                                    var url = await linkElement.EvaluateFunctionAsync<string>("link => link.href");

                                    var titleElement = await itemElement.QuerySelectorAsync("div.details-outer > div > div.details-inner > a > span");
                                    var title = titleElement != null ? await titleElement.EvaluateFunctionAsync<string>("el => el.innerText.trim()") : string.Empty;

                                    var hasVariations = await itemElement.EvaluateFunctionAsync<bool>("item => item.querySelector('div.details-outer > div > div.colorswatch')?.querySelectorAll('span').length > 0");

                                    var position = i + 1;

                                    parentItems.Add((url, title, hasVariations, position));
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to extract data for an item: {ex.Message}");
                                }
                            }
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Attempt {attempt + 1} failed to access items: {ex.Message}");
                            if (attempt == 2) throw;
                            await Task.Delay(2000);
                        }
                    }

                    Console.WriteLine($"**Found {parentItems.Count} parent items on page {pageNumber + 1}.");

                    for (int i = (pageNumber == startPage ? startPosition : 0); i < parentItems.Count; i++)
                    {
                        var (itemUrl, itemTitle, hasVariations, position) = parentItems[i];

                        for (int retry = 0; retry < MaxRetries; retry++)
                        {
                            try
                            {
                                var itemDetails = new OOSItemDetails
                                {
                                    ItemName = itemTitle,
                                    UPID = itemUrl.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty,
                                    StockStatus = "Out of Stock",
                                    RetrievedAt = DateTime.UtcNow,
                                    PageNumber = pageNumber + 1,
                                    PositionOnPage = position,
                                    ItemURL = itemUrl,
                                    HasVariations = hasVariations,
                                    Variations = new List<Variation>()
                                };

                                if (hasVariations)
                                {
                                    bool navigated = false;
                                    int maxAttempts = 3;
                                    int attempt = 0;

                                    while (!navigated && attempt < maxAttempts)
                                    {
                                        try
                                        {
                                            var response = await itemPage.GoToAsync(itemUrl, new NavigationOptions
                                            {
                                                Timeout = 60000,
                                            });

                                            if (!response.Ok)
                                            {
                                                throw new Exception($"HTTP error! status: {response.Status}");
                                            }

                                            navigated = true;
                                        }
                                        catch (TimeoutException)
                                        {
                                            Console.WriteLine($"Attempt {attempt + 1}/{maxAttempts} - Navigation timed out. Retrying...");
                                        }
                                        catch (NavigationException ex)
                                        {
                                            Console.WriteLine($"Attempt {attempt + 1}/{maxAttempts} - Navigation failed: {ex.Message}. Retrying...");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Attempt {attempt + 1}/{maxAttempts} - Unexpected error during navigation: {ex.Message}. Retrying...");
                                        }

                                        if (!navigated)
                                        {
                                            attempt++;
                                            if (attempt == maxAttempts)
                                            {
                                                Console.WriteLine($"Failed to navigate to item URL after {maxAttempts} attempts.");
                                                break;
                                            }
                                            await Task.Delay(2000 * attempt); // Increasing delay with each attempt
                                        }
                                    }

                                    if (navigated)
                                    {
                                        await itemPage.WaitForSelectorAsync("div.product-details", new WaitForSelectorOptions { Timeout = 60000 });

                                        var variationElements = await itemPage.QuerySelectorAllAsync("body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.purchase > div.product-option > ul > li");

                                        int outOfStockCount = 0;
                                        foreach (var element in variationElements)
                                        {
                                            var isOutOfStock = await element.EvaluateFunctionAsync<bool>("el => el.classList.contains('opacity')");
                                            if (isOutOfStock) outOfStockCount++;
                                        }

                                        bool isTotallyOutOfStock = outOfStockCount == variationElements.Length;
                                        itemDetails.StockStatus = isTotallyOutOfStock ? "Out of Stock" : "Partially Out of Stock";
                                        Console.WriteLine($"Parent item is {itemDetails.StockStatus.ToLower()}.");

                                        foreach (var element in variationElements)
                                        {
                                            await Task.Delay(2000);
                                            var isOutOfStock = await element.EvaluateFunctionAsync<bool>("el => el.classList.contains('opacity')");
                                            if (isOutOfStock)
                                            {
                                                var variationName = await element.EvaluateFunctionAsync<string>("el => el.innerText");
                                                var variationHref = await element.EvaluateFunctionAsync<string>("el => el.querySelector('a').href");
                                                var variationUpid = variationHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty;

                                                Console.WriteLine($"Found out-of-stock variation: {variationName}");

                                                itemDetails.Variations.Add(new Variation
                                                {
                                                    Name = variationName,
                                                    IsOutOfStock = true,
                                                    UPID = variationUpid,
                                                    StockStatus = "Out of Stock"
                                                });
                                            }
                                        }
                                    }
                                }

                                outOfStockItems.Add(itemDetails);
                                Console.WriteLine(new string('*', 72));
                                Console.WriteLine($"Scanned item on Page {pageNumber + 1}: {itemDetails.ItemName}");
                                Console.WriteLine($"UPID: {itemDetails.UPID}, Position: {itemDetails.PositionOnPage}");
                                Console.WriteLine(new string('*', 72));

                                foreach (Variation item in itemDetails.Variations)
                                {
                                    Console.WriteLine($"Scanned variation item: {item.Name}, UPID: {item.UPID}");
                                }

                                SaveCheckpoint(pageNumber, i, outOfStockItems.Count);
                                SaveResults(outOfStockItems);

                                Console.WriteLine($"Throttling for {_throttleDelay} milliseconds...");
                                await Task.Delay(_throttleDelay + _random.Next(0, 1000));

                                break; // Exit the retry loop on success
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error processing item at position {position} on page {pageNumber + 1}: {ex.Message}");
                                if (retry == MaxRetries - 1) throw;
                                await Task.Delay(2000);
                            }
                        }
                    }

                    if (hasNextPage && !string.IsNullOrEmpty(nextPageUrl))
                    {
                        Console.WriteLine($"Navigating to {nextPageUrl} (page {pageNumber})...");
                        await mainPage.GoToAsync(nextPageUrl, new NavigationOptions
                        {
                            Timeout = 160000,
                            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                        });
                        Console.WriteLine($"Successfully navigated to page {pageNumber}.");
                        pageNumber++;                       
                        Console.WriteLine($"Throttling for {_throttleDelay} milliseconds...");
                        await Task.Delay(_throttleDelay + _random.Next(0, 1000));
                    }
                    else
                    {
                        Console.WriteLine("No more pages to navigate.");
                        hasNextPage = false;
                    }

                } while (hasNextPage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during scraping process: {ex.Message}");
            }
            finally
            {
                if (browser != null)
                {
                    await browser.CloseAsync();
                    Console.WriteLine("Browser closed.");
                }
            }

            return outOfStockItems;
        }





        private void SaveCheckpoint(int pageNumber, int position, int itemCount)
        {
            var checkpoint = new ScraperCheckpoint
            {
                LastPageScraped = pageNumber,
                LastPositionScraped = position,
                TotalItemsScraped = itemCount
            };

            string json = JsonSerializer.Serialize(checkpoint);
            File.WriteAllText(CheckpointFile, json);
        }

        private void SaveResults(List<OOSItemDetails> items)
        {
            string json = JsonSerializer.Serialize(items);
            File.WriteAllText(PreviousResultsFile, json);
        }
    }

    public class ScraperCheckpoint
    {
        public int LastPageScraped { get; set; }
        public int LastPositionScraped { get; set; }
        public int TotalItemsScraped { get; set; }
    }
}
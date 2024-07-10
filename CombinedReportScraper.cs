using OOSWebScraper.models;
using OOSWebScraper;
using OOSWebScrapper.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace OOSWebScrapper
{
    public class CombinedReportScraper
    {
        private readonly string _chromePath;
        private readonly int _throttleDelay;
        private readonly CatalogSelectors _selectors;
        private readonly string _catalogType;
        private readonly Random _random = new Random();
        private const int MaxRetries = 3;
        private readonly bool _isTestingMode;
        private readonly int _testingItemsPerPage;
        private readonly int _testingMaxPages;

        public CombinedReportScraper(string chromePath, int throttleDelay, CatalogSelectors selectors, string catalogType, bool isTestingMode = false, int testingItemsPerPage = 3, int testingMaxPages = 3)
        {
            _chromePath = chromePath;
            _throttleDelay = throttleDelay;
            _selectors = selectors;
            _catalogType = catalogType;
            _isTestingMode = isTestingMode;
            _testingItemsPerPage = testingItemsPerPage;
            _testingMaxPages = testingMaxPages;
        }

        public async Task<List<OOSItemDetails>> ScrapeCombinedItemsAsync(List<(string url, string badge, string stockStatus)> categories)
        {
            var combinedItems = new List<OOSItemDetails>();

            foreach (var (url, badge, stockStatus) in categories)
            {
                var items = await ScrapeItemsAsync(url, badge, stockStatus);
                combinedItems.AddRange(items);
            }

            return combinedItems;
        }
        
        private async Task<List<OOSItemDetails>> ScrapeItemsAsync(string url, string badge, string stockStatus)
        {
            var items = new List<OOSItemDetails>();
            IBrowser browser = null;
            IPage mainPage = null;
            IPage itemPage = null;
            int pageNumber = 0;

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

                mainPage = await CreatePageAsync(browser);
                itemPage = await CreatePageAsync(browser);

                await mainPage.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                await mainPage.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
                await mainPage.EvaluateExpressionAsync(@"navigator.webdriver = undefined");

                bool hasNextPage;
                Console.WriteLine($"Navigating to {url} (page {pageNumber + 1})...");
                await mainPage.GoToAsync($"{url}&page={pageNumber}&pageSize=");

                do
                {
                    await mainPage.WaitForSelectorAsync(_selectors.ProductGridSelector, new WaitForSelectorOptions { Timeout = 60000 });
                    Console.WriteLine("Navigation completed and product grid loaded.");

                    var nextPageDisabledJsPath = $"document.querySelector('{_selectors.NextPageDisabledSelector}')";
                    var nextPageJsPath = $"document.querySelector('{_selectors.NextPageLinkSelector}')";
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
                            var itemElements = await mainPage.QuerySelectorAllAsync(_selectors.ItemsSelector);
                            int itemCount = _isTestingMode ? Math.Min(itemElements.Count(), _testingItemsPerPage) : itemElements.Count();
                            for (int i = 0; i < itemCount; i++)
                            {
                                var itemElement = itemElements[i];
                                try
                                {
                                    var linkElement = await itemElement.QuerySelectorAsync(_selectors.ItemLinkSelector);
                                    var itemUrl = await linkElement.EvaluateFunctionAsync<string>("link => link.href");

                                    var titleElement = await itemElement.QuerySelectorAsync(_selectors.ItemTitleSelector);
                                    string itemTitle;

                                    if (_catalogType == "DSS")
                                    {
                                        itemTitle = titleElement != null ? await titleElement.EvaluateFunctionAsync<string>("el => el.innerText.trim()") : string.Empty;
                                    }
                                    else if (_catalogType == "RGS")
                                    {
                                        itemTitle = titleElement != null ? await titleElement.EvaluateFunctionAsync<string>("el => el.getAttribute('title')") : string.Empty;
                                    }
                                    else
                                    {
                                        itemTitle = "no title found";
                                    }

                                    bool hasVariations;

                                    if (_catalogType == "DSS")
                                    {
                                        hasVariations = await itemElement.EvaluateFunctionAsync<bool>($"item => item.querySelector('{_selectors.ItemVariationsSelector}')?.querySelectorAll('span').length > 0");
                                    }
                                    else if (_catalogType == "RGS")
                                    {
                                        hasVariations = await itemElement.EvaluateFunctionAsync<bool>($"item => item.querySelector('div.details-outer > div > div.colorswatch.row span label a') !== null");
                                    }
                                    else
                                    {
                                        hasVariations = false;
                                    }
                                    var position = i + 1;

                                    parentItems.Add((itemUrl, itemTitle, hasVariations, position));
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

                    for (int i = 0; i < parentItems.Count; i++)
                    {
                        var (itemUrl, itemTitle, hasVariations, position) = parentItems[i];

                        for (int retry = 0; retry < MaxRetries; retry++)
                        {
                            try
                            {
                                string upid;
                                if (_catalogType == "DSS")
                                {
                                    upid = itemUrl.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty;
                                }
                                else if (_catalogType == "RGS")
                                {
                                    upid = (itemUrl.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty).TrimEnd('/');
                                }
                                else
                                {
                                    upid = string.Empty;
                                }

                                var itemDetails = new OOSItemDetails
                                {
                                    ItemName = itemTitle,
                                    UPID = upid,
                                    Badge = badge,
                                    StockStatus = stockStatus,
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
                                            await Task.Delay(2000 * attempt);
                                        }
                                    }

                                    if (navigated)
                                    {
                                        await itemPage.WaitForSelectorAsync(_selectors.ProductDetailsSelector, new WaitForSelectorOptions { Timeout = 60000 });

                                        if (_catalogType == "RGS")
                                        {
                                            var variationElements = await itemPage.QuerySelectorAllAsync("#priority1 > label");
                                            Console.WriteLine("Variations found:");

                                            foreach (var element in variationElements)
                                            {
                                                await Task.Delay(2000);
                                                var isOutOfStock = await element.EvaluateFunctionAsync<bool>("el => el.querySelector('span.custom-radio-btn.opacity') !== null");

                                                var variationNameElement = await element.QuerySelectorAsync("label > span");
                                                var variationName = variationNameElement != null ? await variationNameElement.EvaluateFunctionAsync<string>("el => el.innerText") : string.Empty;

                                                var variationHrefElement = await element.QuerySelectorAsync("span.variantURL");
                                                var variationHref = variationHrefElement != null ? await variationHrefElement.EvaluateFunctionAsync<string>("el => el.innerText") : string.Empty;
                                                var variationUpid = (variationHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty).TrimEnd('/');

                                                Console.WriteLine($"Found variation: {variationName}");

                                                itemDetails.Variations.Add(new Variation
                                                {
                                                    Name = variationName,
                                                    Badge = badge,
                                                    IsOutOfStock = isOutOfStock,
                                                    ParentUPID = itemDetails.UPID,
                                                    UPID = variationUpid,
                                                    StockStatus = isOutOfStock ? "Out of Stock" : stockStatus
                                                });
                                            }
                                        }
                                        else
                                        {
                                            var variationElements = await itemPage.QuerySelectorAllAsync(_selectors.VariationsSelector);
                                            Console.WriteLine("Variations found:");

                                            foreach (var element in variationElements)
                                            {
                                                await Task.Delay(2000);
                                                var isOutOfStock = await element.EvaluateFunctionAsync<bool>("el => el.classList.contains('opacity')");

                                                var variationName = await element.EvaluateFunctionAsync<string>("el => el.innerText");
                                                var variationHref = await element.EvaluateFunctionAsync<string>("el => el.querySelector('a').href");
                                                var variationUpid = variationHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty;

                                                Console.WriteLine($"Found variation: {variationName}");

                                                itemDetails.Variations.Add(new Variation
                                                {
                                                    Name = variationName,
                                                    Badge = badge,
                                                    IsOutOfStock = isOutOfStock,
                                                    UPID = variationUpid,
                                                    StockStatus = isOutOfStock ? "Out of Stock" : stockStatus
                                                });
                                            }
                                        }
                                    }
                                }

                                items.Add(itemDetails);
                                await ClosePageAsync(itemPage);
                                itemPage = await CreatePageAsync(browser);
                                Console.WriteLine(new string('*', 72));
                                Console.WriteLine($"Scanned item on Page {pageNumber + 1}: {itemDetails.ItemName}");
                                Console.WriteLine($"UPID: {itemDetails.UPID}, Position: {itemDetails.PositionOnPage}");
                                Console.WriteLine(new string('*', 72));

                                foreach (Variation item in itemDetails.Variations)
                                {
                                    Console.WriteLine($"Scanned variation item: {item.Name}, UPID: {item.UPID}");
                                }

                                Console.WriteLine($"Throttling for {_throttleDelay} milliseconds...");
                                await Task.Delay(_throttleDelay + _random.Next(0, 1000));

                                break;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error processing item at position {position} on page {pageNumber + 1}: {ex.Message}");
                                if (retry == MaxRetries - 1) throw;
                                await Task.Delay(2000);
                            }
                        }
                    }

                    pageNumber++;
                    if (_isTestingMode && pageNumber >= _testingMaxPages)
                    {
                        hasNextPage = false;
                        Console.WriteLine($"Reached maximum test pages ({_testingMaxPages}). Stopping scraping.");
                    }
                    else if (hasNextPage && !string.IsNullOrEmpty(nextPageUrl))
                    {
                        // Before navigating to the next page
                        await ClosePageAsync(itemPage);
                        itemPage = await CreatePageAsync(browser);
                        Console.WriteLine($"Navigating to {nextPageUrl} (page {pageNumber})...");
                        await mainPage.GoToAsync(nextPageUrl, new NavigationOptions
                        {
                            Timeout = 160000,
                            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                        });
                        Console.WriteLine($"Successfully navigated to page {pageNumber}.");
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
                // Close all pages
                if (mainPage != null) await ClosePageAsync(mainPage);
                if (itemPage != null) await ClosePageAsync(itemPage);

                if (browser != null)
                {
                    await browser.CloseAsync();
                    Console.WriteLine("Browser closed.");
                }
            }

            return items;
        }
        private async Task<IPage> CreatePageAsync(IBrowser browser)
        {
            var page = await browser.NewPageAsync();
            page.DefaultNavigationTimeout = 120000;
            return page;
        }

        private async Task ClosePageAsync(IPage page)
        {
            if (page != null)
            {
                await page.CloseAsync();
                await page.DisposeAsync();
            }
        }
    }
}
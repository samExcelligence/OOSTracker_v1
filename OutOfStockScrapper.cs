using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PuppeteerSharp;
using System.Linq;
using System.Text.Json;
using System.IO;
using OOSWebScraper.models;
using OOSWebScrapper.Models;

namespace OOSWebScraper
{
    /************************************************************************************
    Name: OutOfStockScraper
    Purpose: Scrapes out-of-stock items from a specified product catalog using Puppeteer.
    Notes: Initializes with browser settings, navigation URLs, and various configuration options.
    Developer Name: Sam Espinoza
    ************************************************************************************/
    public class OutOfStockScraper
    {
        private readonly string _chromePath;
        private readonly string _url;
        private readonly int _throttleDelay;
        private readonly string _catalogName;
        private readonly CatalogSelectors _selectors;
        private readonly string _catalogType;
        private readonly Random _random = new Random();
        private const int MaxRetries = 3;
        private const string CheckpointFile = "scraper_checkpoint.json";
        private const string PreviousResultsFile = "previous_results.json";

        /************************************************************************************
        Name: OutOfStockScraper Constructor
        Purpose: Initializes an instance of OutOfStockScraper with necessary parameters.
        ************************************************************************************/

        public OutOfStockScraper(string chromePath, string url, int throttleDelay, CatalogSelectors selectors, string catalogType, string catalogName)
        {
            _chromePath = chromePath;
            _url = url;
            _throttleDelay = throttleDelay;
            _selectors = selectors;
            _catalogType = catalogType;
            _catalogName = catalogName;
        }
        /************************************************************************************
        Name: ScrapeOutOfStockItemsAsync
        Purpose: Main method to scrape out-of-stock items from the product catalog.
        ************************************************************************************/

        public async Task<List<OOSItemDetails>> ScrapeOutOfStockItemsAsync()
        {
            var outOfStockItems = new List<OOSItemDetails>();
            IBrowser browser = null;
            IPage mainPage = null;
            IPage itemPage = null;
            IPage dropdownPage = null;
            IPage innerDropdownPage = null;
            int startPage = 0;
            int startPosition = 0;

            /************************************************************************************
            Name: Check for checkpoint file
            Purpose: Resumes scraping from the last saved position if checkpoint file exists.
            ************************************************************************************/
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
                /************************************************************************************
                Name: Browser Initialization
                Purpose: Launches a headless browser with specified configurations.
                ************************************************************************************/

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
                /************************************************************************************
                Name: Page Initialization
                Purpose: Initializes multiple browser pages for concurrent navigation and scraping.
                ************************************************************************************/
                mainPage = await browser.NewPageAsync();
                itemPage = await browser.NewPageAsync();
                dropdownPage = await browser.NewPageAsync();
                innerDropdownPage = await browser.NewPageAsync();
                mainPage.DefaultNavigationTimeout = 120000;
                itemPage.DefaultNavigationTimeout = 120000;
                dropdownPage.DefaultNavigationTimeout = 120000;
                innerDropdownPage.DefaultNavigationTimeout = 120000;


                await mainPage.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                await mainPage.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
                await mainPage.EvaluateExpressionAsync(@"navigator.webdriver = undefined");

                bool hasNextPage;
                int pageNumber = startPage;
                Console.WriteLine($"Navigating to {_url} (page {pageNumber + 1})...");
                await mainPage.GoToAsync($"{_url}&page={pageNumber}&pageSize=");
                /************************************************************************************
                Name: Main Scraping Loop
                Purpose: Iterates through pages and extracts out-of-stock items.
                ************************************************************************************/

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
                            int itemCount = itemElements.Count();
                            for (int i = 0; i < itemCount; i++)
                            {
                                var itemElement = itemElements[i];
                                try
                                {
                                    var linkElement = await itemElement.QuerySelectorAsync(_selectors.ItemLinkSelector);
                                    var url = await linkElement.EvaluateFunctionAsync<string>("link => link.href");

                                    var titleElement = await itemElement.QuerySelectorAsync(_selectors.ItemTitleSelector);
                                    string title;

                                    if (_catalogType == "DSS")
                                    {
                                        title = titleElement != null ? await titleElement.EvaluateFunctionAsync<string>("el => el.innerText.trim()") : string.Empty;
                                    }
                                    else if (_catalogType == "RGS")
                                    {
                                        title = titleElement != null ? await titleElement.EvaluateFunctionAsync<string>("el => el.getAttribute('title')") : string.Empty;
                                    }
                                    else
                                    {
                                        title = "no title found"; // Default case if needed
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
                                        hasVariations = false; // Default case if needed
                                    }
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
                        /************************************************************************************
                        Name: Item Processing Loop
                        Purpose: Processes each item and handles its variations if any.
                        ************************************************************************************/
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
                                    upid = string.Empty; // Default case if needed
                                }

                                var itemDetails = new OOSItemDetails
                                {
                                    ItemName = itemTitle,
                                    UPID = upid,
                                    Badge = "Out of Stock",
                                    StockStatus = "Out of Stock",
                                    RetrievedAt = DateTime.UtcNow,
                                    PageNumber = pageNumber + 1,
                                    PositionOnPage = position,
                                    ItemURL = itemUrl,
                                    HasVariations = hasVariations,
                                    Variations = new List<Variation>()
                                };
                                /************************************************************************************
                                Name: Handle Item Variations
                                Purpose: Navigate and scrape variations if the item has variations.
                                ************************************************************************************/
                                if (hasVariations)
                                {
                                    bool navigated = false;
                                    int maxAttempts = 3;
                                    int attempt = 0;

                                    while (!navigated && attempt < maxAttempts)
                                    {
                                        try
                                        {
                                            Console.WriteLine($"Attempt {attempt + 1}/{maxAttempts} - Navigating to item URL");

                                            // Check if itemPage is null
                                            if (itemPage == null)
                                            {
                                                Console.WriteLine("itemPage is null. Creating a new page.");
                                                itemPage = await browser.NewPageAsync();
                                            }

                                            // Navigate to the item URL
                                            var response = await itemPage.GoToAsync(itemUrl, new NavigationOptions
                                            {
                                                WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                                                Timeout = 60000
                                            });

                                            if (response == null)
                                            {
                                                throw new Exception("Navigation response is null");
                                            }

                                            if (!response.Ok)
                                            {
                                                throw new Exception($"Navigation failed with status {response.Status}");
                                            }

                                            // Wait for the product details selector
                                            await itemPage.WaitForSelectorAsync(_selectors.ProductDetailsSelector, new WaitForSelectorOptions { Timeout = 30000 });

                                            navigated = true;
                                            Console.WriteLine("Successfully navigated to item page");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Attempt {attempt + 1}/{maxAttempts} - Navigation failed: {ex.Message}");
                                            if (ex.InnerException != null)
                                            {
                                                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                                            }
                                            attempt++;
                                            if (attempt < maxAttempts)
                                            {
                                                var delay = (int)Math.Pow(2, attempt) * 1000; // Exponential backoff
                                                Console.WriteLine($"Retrying in {delay}ms...");
                                                await Task.Delay(delay);
                                            }
                                        }

                                        if (!navigated && attempt == maxAttempts)
                                        {
                                            Console.WriteLine($"Failed to navigate to item URL after {maxAttempts} attempts.");
                                            break;
                                        }
                                    }


                                    if (navigated)
                                    {
                                        await itemPage.WaitForSelectorAsync(_selectors.ProductDetailsSelector, new WaitForSelectorOptions { Timeout = 60000 });
                                        Console.WriteLine("----> NAVIGATION TO PARENT ITEM PAGE COMPLETE <-------");
                                        /************************************************************************************
                                        Name: Handle RGS Variations
                                        Purpose: Processes variations for RGS catalog type.
                                        ************************************************************************************/
                                        if (_catalogType == "RGS")
                                        {
                                            var variationElements = await itemPage.QuerySelectorAllAsync("#priority1 > label");
                                            Console.WriteLine("Variations found:");

                                            foreach (var element in variationElements)
                                            {
                                                var variationNameElement = await element.QuerySelectorAsync("span");
                                                var variationName = variationNameElement != null ? await variationNameElement.EvaluateFunctionAsync<string>("el => el.innerText") : string.Empty;

                                                var variationHrefElement = await element.QuerySelectorAsync("input[type='radio']");
                                                var variationHref = variationHrefElement != null ? await variationHrefElement.EvaluateFunctionAsync<string>("el => el.value") : string.Empty;
                                                var variationUpid = (variationHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty).TrimEnd('/');

                                                //Console.WriteLine($"Variation Name: {variationName}");
                                                //Console.WriteLine($"Variation URL: {variationHref}");
                                                //Console.WriteLine($"Variation UPID: {variationUpid}");
                                                //Console.WriteLine(new string('-', 50));  // Separator line for better readability
                                            }
                                            // Handle RGS specific variation logic
                                            int outOfStockCount = 0;
                                            foreach (var element in variationElements)
                                            {
                                                var isOutOfStock = await element.EvaluateFunctionAsync<bool>("el => el.querySelector('span.custom-radio-btn.opacity') !== null");
                                                if (isOutOfStock) outOfStockCount++;
                                            }

                                            bool isTotallyOutOfStock = outOfStockCount == variationElements.Length;
                                            itemDetails.StockStatus = isTotallyOutOfStock ? "Out of Stock" : "Partially Out of Stock";
                                            Console.WriteLine($"Parent item is {itemDetails.StockStatus.ToLower()}.");

                                            foreach (var element in variationElements)
                                            {
                                                await Task.Delay(2000);
                                                var isOutOfStock = await element.EvaluateFunctionAsync<bool>("el => el.querySelector('span.custom-radio-btn.opacity') !== null");
                                                if (isOutOfStock)
                                                {
                                                    // Extract the variation name from the span element containing the inner text
                                                    var variationNameElement = await element.QuerySelectorAsync("label > span");
                                                    var variationName = variationNameElement != null ? await variationNameElement.EvaluateFunctionAsync<string>("el => el.innerText") : string.Empty;

                                                    var variationHrefElement = await element.QuerySelectorAsync("span.variantURL");
                                                    var variationHref = variationHrefElement != null ? await variationHrefElement.EvaluateFunctionAsync<string>("el => el.innerText") : string.Empty;
                                                    var variationUpid = (variationHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty).TrimEnd('/');

                                                    Console.WriteLine($"Found out-of-stock variation: {variationName}");

                                                    itemDetails.Variations.Add(new Variation
                                                    {
                                                        Name = variationName,
                                                        Badge = "Out of Stock",
                                                        IsOutOfStock = true,
                                                        UPID = variationUpid,
                                                        StockStatus = "Out of Stock"
                                                    });
                                                }
                                            }
                                        }
                                        /************************************************************************************
                                       Name: Handle DSS Variations
                                       Purpose: Processes variations for DSS catalog type.
                                       ************************************************************************************/
                                        else if (_catalogType == "DSS")
                                        {
                                            var variationElements = await itemPage.QuerySelectorAllAsync(_selectors.VariationsSelector);
                                            var dropListElements = await itemPage.QuerySelectorAllAsync(_selectors.DropListVariationsSelector);
                                            var comboDropListElements = await itemPage.QuerySelectorAllAsync(_selectors.ComboDropListVariationsSelector);
                                            var assemblyOptionElements = await itemPage.QuerySelectorAllAsync(_selectors.AssemblyOptionSelector);
                                            int parentOutOfStockCount = 0;
                                            int totalComboOutOfStockCount = 0;
                                            int totalComboCount = 0;
                                            bool isVariationAndCombo = false;
                                            bool isAssembly = false;
                                            if (variationElements.Any() && comboDropListElements.Any())
                                            {
                                                isVariationAndCombo = true;

                                                Console.WriteLine("Color and Dropdown variations found:");

                                                foreach (var colorElement in variationElements)
                                                {
                                                    try
                                                    {
                                                        var isColorOutOfStock = await colorElement.EvaluateFunctionAsync<bool>("el => el.classList.contains('opacity')");
                                                        if (isColorOutOfStock)
                                                        {
                                                            var colorName = await colorElement.EvaluateFunctionAsync<string>("el => el.innerText");
                                                            var colorHref = await colorElement.EvaluateFunctionAsync<string>("el => el.querySelector('a').href");
                                                            var colorUpid = (colorHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty).TrimEnd('/');

                                                            parentOutOfStockCount++;

                                                            // Use the third navigator to visit the color variation page
                                                            var colorNavigated = false;
                                                            int colorAttempts = 0;
                                                            while (!colorNavigated && colorAttempts < MaxRetries)
                                                            {
                                                                try
                                                                {
                                                                    var response = await dropdownPage.GoToAsync(colorHref, new NavigationOptions
                                                                    {

                                                                        Timeout = 60000,
                                                                    });

                                                                    if (!response.Ok)
                                                                    {
                                                                        throw new Exception($"HTTP error! status: {response.Status}");
                                                                    }

                                                                    colorNavigated = true;
                                                                }
                                                                catch (Exception ex)
                                                                {
                                                                    Console.WriteLine($"Attempt {colorAttempts + 1}/{MaxRetries} - Color variation page navigation failed: {ex.Message}. Retrying...");
                                                                    colorAttempts++;
                                                                    await Task.Delay(2000 * colorAttempts); // Increasing delay with each attempt
                                                                }
                                                            }

                                                            if (colorNavigated)
                                                            {
                                                                var colorPageDropListElements = await dropdownPage.QuerySelectorAllAsync(_selectors.ComboDropListVariationsSelector);
                                                                int colorComboOutOfStockCount = 0;
                                                                totalComboCount += colorPageDropListElements.Length;

                                                                foreach (var dropDownElement in colorPageDropListElements)
                                                                {
                                                                    try
                                                                    {
                                                                        var optionValue = await dropDownElement.EvaluateFunctionAsync<string>("el => el.value");
                                                                        var optionText = await dropDownElement.EvaluateFunctionAsync<string>("el => el.textContent.trim()");
                                                                        // Ignore the first item if it is "Select a Style..."
                                                                        if (optionText == "Select a Style...")
                                                                        {
                                                                            continue;
                                                                        }
                                                                        Console.WriteLine($"Checking variation: Color - {colorName}, Size - {optionText}");

                                                                        // Use a fourth navigator to handle dropdown variations within the color variation page
                                                                        var dropdownNavigated = false;
                                                                        int dropdownAttempts = 0;
                                                                        while (!dropdownNavigated && dropdownAttempts < MaxRetries)
                                                                        {
                                                                            try
                                                                            {
                                                                                // Select the dropdown option
                                                                                await innerDropdownPage.GoToAsync(colorHref);
                                                                                await innerDropdownPage.SelectAsync(_selectors.ComboDropListSelector, optionValue);
                                                                                await Task.Delay(2000); // Wait for the page to update

                                                                                dropdownNavigated = true;
                                                                            }
                                                                            catch (Exception ex)
                                                                            {
                                                                                Console.WriteLine($"Attempt {dropdownAttempts + 1}/{MaxRetries} - Dropdown/Colors Combo navigation failed: {ex.Message}. Retrying...");
                                                                                dropdownAttempts++;
                                                                                await Task.Delay(2000 * dropdownAttempts); // Increasing delay with each attempt
                                                                            }
                                                                        }

                                                                        if (dropdownNavigated)
                                                                        {
                                                                            // Check for out-of-stock label
                                                                            bool isOutOfStock = await innerDropdownPage.EvaluateFunctionAsync<bool>($"() => document.querySelector('{_selectors.VariationOutOfStockLabel_1}') !== null");

                                                                            if (isOutOfStock)
                                                                            {
                                                                                Console.WriteLine($"Found out-of-stock variation: Color - {colorName}, Size - {optionText}");

                                                                                itemDetails.Variations.Add(new Variation
                                                                                {
                                                                                    Name = $"{colorName} - {optionText}",
                                                                                    Badge = "Out of Stock",
                                                                                    IsOutOfStock = true,
                                                                                    UPID = colorUpid, // Assuming UPID stays the same for dropdown variations
                                                                                    StockStatus = "Out of Stock"
                                                                                });

                                                                                colorComboOutOfStockCount++;
                                                                                totalComboOutOfStockCount++;
                                                                            }
                                                                        }
                                                                    }
                                                                    catch (Exception ex)
                                                                    {
                                                                        Console.WriteLine($"Error processing dropdown variation: {ex.Message}");
                                                                    }
                                                                }

                          
                                                            }
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error processing color variation: {ex.Message}");
                                                    }
                                                }
                                            }
                                            else if (dropListElements.Any())
                                            {
                                                Console.WriteLine("Dropdown variations found:");

                                                foreach (var dropDownElement in dropListElements)
                                                {
                                                    try
                                                    {
                                                        var optionURL = await dropDownElement.EvaluateFunctionAsync<string>("el => el.value");
                                                        var optionText = await dropDownElement.EvaluateFunctionAsync<string>("el => el.textContent.trim()");
                                                        // Ignore the first item if it is "Select a Style..." or "Select a Height..."
                                                        if (optionText == "Select a Style..." || optionText == "Select a Height...")
                                                        {
                                                            continue;
                                                        }
                                                        Console.WriteLine($"Checking dropdown variation: {optionText}");
                                                        Console.WriteLine($"Variation URL: {optionURL}");

                                                        // Use the third navigator to handle dropdown variations
                                                        var dropdownNavigated = false;
                                                        int dropdownAttempts = 0;
                                                        while (!dropdownNavigated && dropdownAttempts < MaxRetries)
                                                        {
                                                            try
                                                            {
                                                                Console.WriteLine($"Attempt {dropdownAttempts + 1}/{MaxRetries} - Navigating to variation URL");

                                                                // Check if dropdownPage is null
                                                                if (dropdownPage == null)
                                                                {
                                                                    Console.WriteLine("dropdownPage is null. Creating a new page.");
                                                                    dropdownPage = await browser.NewPageAsync();
                                                                }

                                                                // Construct the full URL
                                                                string fullUrl = "https://www.discountschoolsupply.com" + optionURL;
                                                                Console.WriteLine($"Full URL: {fullUrl}");

                                                                // Navigate to the page
                                                                var response = await dropdownPage.GoToAsync(fullUrl, new NavigationOptions
                                                                {
                                                                    WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                                                                    Timeout = 60000
                                                                });

                                                                if (response == null)
                                                                {
                                                                    throw new Exception("Navigation response is null");
                                                                }

                                                                if (!response.Ok)
                                                                {
                                                                    throw new Exception($"Navigation failed with status {response.Status}");
                                                                }

                                                                // Wait for the product details selector
                                                                await dropdownPage.WaitForSelectorAsync(_selectors.ProductDetailsSelector, new WaitForSelectorOptions { Timeout = 30000 });

                                                                dropdownNavigated = true;
                                                                Console.WriteLine("Successfully navigated to variation page");
                                                            }
                                                            catch (Exception ex)
                                                            {
                                                                Console.WriteLine($"Attempt {dropdownAttempts + 1}/{MaxRetries} - Dropdown navigation failed: {ex.Message}");
                                                                if (ex.InnerException != null)
                                                                {
                                                                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                                                                }
                                                                dropdownAttempts++;
                                                                if (dropdownAttempts < MaxRetries)
                                                                {
                                                                    var delay = (int)Math.Pow(2, dropdownAttempts) * 1000; // Exponential backoff
                                                                    Console.WriteLine($"Retrying in {delay}ms...");
                                                                    await Task.Delay(delay);
                                                                }
                                                            }
                                                        }

                                                        if (dropdownNavigated)
                                                        {
                                                            try
                                                            {
                                                                // Check for out-of-stock label
                                                                bool isOutOfStock = await dropdownPage.EvaluateFunctionAsync<bool>($@"
                        () => {{
                            const label = document.querySelector('{_selectors.VariationOutOfStockLabel_1}');
                            console.log('Out of stock label:', label);
                            return label !== null;
                        }}
                    ");

                                                                Console.WriteLine($"Is out of stock: {isOutOfStock}");

                                                                if (isOutOfStock)
                                                                {
                                                                    Console.WriteLine($"Found out-of-stock dropdown variation: {optionText}");

                                                                    itemDetails.Variations.Add(new Variation
                                                                    {
                                                                        Name = optionText,
                                                                        Badge = "Out of Stock",
                                                                        IsOutOfStock = true,
                                                                        UPID = upid, // Assuming UPID stays the same for dropdown variations
                                                                        StockStatus = "Out of Stock"
                                                                    });
                                                                }
                                                            }
                                                            catch (Exception ex)
                                                            {
                                                                Console.WriteLine($"Error checking out-of-stock status: {ex.Message}");
                                                            }
                                                        }
                                                        else
                                                        {
                                                            Console.WriteLine($"Failed to navigate for variation: {optionText}");
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error processing dropdown element: {ex.Message}");
                                                    }
                                                }
                                            }
                                            else if (variationElements.Any() && !comboDropListElements.Any())
                                            {
                                                Console.WriteLine("Variations found:");
                                                foreach (var element in variationElements)
                                                {
                                                    try
                                                    {
                                                        var variationNameElement = await element.QuerySelectorAsync("span");
                                                        var variationName = variationNameElement != null ? await variationNameElement.EvaluateFunctionAsync<string>("el => el.innerText") : string.Empty;
                                                        var variationHrefElement = await element.QuerySelectorAsync("a");
                                                        var variationHref = variationHrefElement != null ? await variationHrefElement.EvaluateFunctionAsync<string>("el => el.href") : string.Empty;
                                                        var variationUpid = (variationHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty).TrimEnd('/');
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error processing variation element: {ex.Message}");
                                                    }
                                                }

                                                // Handle DSS specific variation logic
                                                int outOfStockCount = 0;
                                                foreach (var element in variationElements)
                                                {
                                                    try
                                                    {
                                                        var isOutOfStock = await element.EvaluateFunctionAsync<bool>("el => el.classList.contains('opacity')");
                                                        if (isOutOfStock) outOfStockCount++;
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error checking out of stock status: {ex.Message}");
                                                    }
                                                }

                                                bool isTotallyOutOfStock = outOfStockCount == variationElements.Length;
                                                itemDetails.StockStatus = isTotallyOutOfStock ? "Out of Stock" : "Partially Out of Stock";
                                                Console.WriteLine($"Parent item is {itemDetails.StockStatus.ToLower()}.");

                                                foreach (var element in variationElements)
                                                {
                                                    try
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
                                                                Badge = "Out of Stock",
                                                                IsOutOfStock = true,
                                                                UPID = variationUpid,
                                                                StockStatus = "Out of Stock"
                                                            });
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error processing out-of-stock variation: {ex.Message}");
                                                    }
                                                }
                                            }
                                            else if (assemblyOptionElements.Any())
                                            {
                                                isAssembly = true;
                                                Console.WriteLine("***Assembly options found:");
                                                int assemblyOutOfStockCount = 0;
                                                int totalAssemblyCount = assemblyOptionElements.Length;
                                                Console.WriteLine($"{totalAssemblyCount}");
                                                foreach (var assemblyElement in assemblyOptionElements)
                                                {
                                                    try
                                                    {
                                                        var isOutOfStock = await assemblyElement.EvaluateFunctionAsync<bool>("el => el.classList.contains('opacity')");
                                                        var assemblyHref = await assemblyElement.EvaluateFunctionAsync<string>("el => el.href");
                                                        var assemblyUpid = (assemblyHref.Split(new[] { "/p/" }, StringSplitOptions.None).LastOrDefault() ?? string.Empty).TrimEnd('/');
                                                        Console.WriteLine($"{isOutOfStock}");
                                                        if (isOutOfStock)
                                                        {
                                                            assemblyOutOfStockCount++;
                                                            // Navigate to the variation page to get the name
                                                            var assemblyNavigated = false;
                                                            int assemblyAttempts = 0;
                                                            while (!assemblyNavigated && assemblyAttempts < MaxRetries)
                                                            {
                                                                try
                                                                {
                                                                    var response = await dropdownPage.GoToAsync(assemblyHref, new NavigationOptions
                                                                    {
                                                                        Timeout = 60000,
                                                                    });

                                                                    if (!response.Ok)
                                                                    {
                                                                        throw new Exception($"HTTP error! status: {response.Status}");
                                                                    }

                                                                    assemblyNavigated = true;
                                                                }
                                                                catch (Exception ex)
                                                                {
                                                                    Console.WriteLine($"Attempt {assemblyAttempts + 1}/{MaxRetries} - Assembly page navigation failed: {ex.Message}. Retrying...");
                                                                    assemblyAttempts++;
                                                                    await Task.Delay(2000 * assemblyAttempts); // Increasing delay with each attempt
                                                                }
                                                            }

                                                            if (assemblyNavigated)
                                                            {
                                                                var assemblyName = await dropdownPage.EvaluateFunctionAsync<string>("() => document.querySelector('body > main > div.container.main__inner-wrapper > div.row.product-details > div.col-sm-6.product-details__left > div > div.js-zoom-target > div.hidden-xs.hidden-sm > h1').innerText");

                                                                Console.WriteLine($"Found out-of-stock assembly option: {assemblyName}");

                                                                itemDetails.Variations.Add(new Variation
                                                                {
                                                                    Name = assemblyName,
                                                                    Badge = "Out of Stock",
                                                                    IsOutOfStock = true,
                                                                    UPID = assemblyUpid,
                                                                    StockStatus = "Out of Stock"
                                                                });
                                                            }
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error processing assembly option: {ex.Message}");
                                                    }
                                                }
                                                isAssembly = true;
                                                totalComboOutOfStockCount = assemblyOutOfStockCount;
                                                totalComboCount = totalAssemblyCount;

                                            }
                                            if (isVariationAndCombo)
                                            {
                                                bool isParentTotallyOutOfStock = parentOutOfStockCount == variationElements.Length;
                                                itemDetails.StockStatus = isParentTotallyOutOfStock && totalComboOutOfStockCount == totalComboCount ? "Out of Stock" : "Partially Out of Stock";
                                                Console.WriteLine($"Parent item is {itemDetails.StockStatus.ToLower()}.");
                                            }
                                            else if (isAssembly)
                                            {
                                                bool isAssemblyTotallyOutOfStock = totalComboOutOfStockCount == totalComboCount;
                                                itemDetails.StockStatus = isAssemblyTotallyOutOfStock ? "Out of Stock" : "Partially Out of Stock";
                                                Console.WriteLine($"Parent item is {itemDetails.StockStatus.ToLower()}.");
                                            }
                                            else
                                            {
                                                int totalOutOfStockCount = itemDetails.Variations.Count(v => v.IsOutOfStock);
                                                bool isCompletelyOutOfStock = totalOutOfStockCount == variationElements.Length || totalOutOfStockCount == dropListElements.Length;
                                                itemDetails.StockStatus = isCompletelyOutOfStock ? "Out of Stock" : "Partially Out of Stock";
                                                Console.WriteLine($"Parent item is {itemDetails.StockStatus.ToLower()}.");
                                            }
                                        }
                                    }
                                }
                                /************************************************************************************
                                Name: Add Item Details
                                Purpose: Adds the processed item details to the out-of-stock items list.
                                ************************************************************************************/
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

                    /************************************************************************************
                    Name: Next Page Navigation
                    Purpose: Navigates to the next page if it exists.
                    ************************************************************************************/

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
                /************************************************************************************
                Name: Browser Closure
                Purpose: Closes the browser after the scraping process is complete or an error occurs.
                ************************************************************************************/
                if (browser != null)
                {
                    await browser.CloseAsync();
                    Console.WriteLine("Browser closed.");
                }
            }

            return outOfStockItems;

        }


        /************************************************************************************
        Name: SaveCheckpoint
        Purpose: Saves the current scraping state to a file to enable resuming later.
        ************************************************************************************/
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
        /************************************************************************************
        Name: SaveResults
        Purpose: Saves the list of out-of-stock items to a file.
        ************************************************************************************/
        private void SaveResults(List<OOSItemDetails> items)
        {
            string json = JsonSerializer.Serialize(items);
            File.WriteAllText(PreviousResultsFile, json);
        }


    }
    /************************************************************************************
    Name: ScraperCheckpoint
    Purpose: Represents the state of the scraper for checkpointing.
    Notes: should proably move this to its own file
    ************************************************************************************/

    public class ScraperCheckpoint
    {
        public int LastPageScraped { get; set; }
        public int LastPositionScraped { get; set; }
        public int TotalItemsScraped { get; set; }
    }
}

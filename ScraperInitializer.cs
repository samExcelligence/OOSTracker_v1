using System;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace OOSWebScraper
{
    public class ScraperInitializer
    {
        private readonly string _chromePath;

        public ScraperInitializer(string chromePath)
        {
            _chromePath = chromePath;
        }

        public async Task<(IBrowser, (IPage, IPage, IPage, IPage))> InitializeBrowserAndPagesAsync()
        {
            var browser = await LaunchBrowserAsync();
            var pages = await InitializePagesAsync(browser);
            return (browser, pages);
        }

        private async Task<IBrowser> LaunchBrowserAsync()
        {
            Console.WriteLine("Starting the browser...");

            var browser = await Puppeteer.LaunchAsync(new LaunchOptions
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
            return browser;
        }

        private async Task<(IPage, IPage, IPage, IPage)> InitializePagesAsync(IBrowser browser)
        {
            var mainPage = await browser.NewPageAsync();
            var itemPage = await browser.NewPageAsync();
            var dropdownPage = await browser.NewPageAsync();
            var innerDropdownPage = await browser.NewPageAsync();

            foreach (var page in new[] { mainPage, itemPage, dropdownPage, innerDropdownPage })
            {
                page.DefaultNavigationTimeout = 120000;
                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
                await page.EvaluateExpressionAsync(@"navigator.webdriver = undefined");
            }

            return (mainPage, itemPage, dropdownPage, innerDropdownPage);
        }

        public async Task CloseBrowser(IBrowser browser)
        {
            if (browser != null)
            {
                await browser.CloseAsync();
                Console.WriteLine("Browser closed.");
            }
        }
    }
}
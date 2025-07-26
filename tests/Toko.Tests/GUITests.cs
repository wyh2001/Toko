using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Toko.Tests
{
    public sealed class TokoServerFixture : IAsyncLifetime
    {
        private Process? _webProcess;
        private Process? _apiProcess;
        public string BaseUrl => "https://localhost:7253";
        public string ApiUrl => "https://localhost:7057";

        public async Task InitializeAsync()
        {
            // Start API server first
            var apiPsi = new ProcessStartInfo("dotnet", "run --no-build --urls " + ApiUrl)
            {
                WorkingDirectory = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Toko")),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _apiProcess = Process.Start(apiPsi)!;

            // Wait for API
            using var apiClient = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });
            for (var i = 0; i < 30; i++)
            {
                try
                {
                    var resp = await apiClient.GetAsync($"{ApiUrl}/api/room/list");
                    if (resp.IsSuccessStatusCode) break;
                }
                catch { }
                if (i == 29) throw new InvalidOperationException("Toko API did not start in time.");
                await Task.Delay(1000);
            }

            // Start web server
            var webPsi = new ProcessStartInfo("dotnet", "run --no-build --urls " + BaseUrl)
            {
                WorkingDirectory = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "client", "Toko.Web")),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _webProcess = Process.Start(webPsi)!;

            // Wait for Web
            using var webClient = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });
            for (var i = 0; i < 30; i++)
            {
                try
                {
                    var resp = await webClient.GetAsync(BaseUrl);
                    if (resp.IsSuccessStatusCode) return;
                }
                catch { }
                await Task.Delay(1000);
            }
            throw new InvalidOperationException("Toko.Web did not start in time.");
        }

        public Task DisposeAsync()
        {
            if (_webProcess is { HasExited: false })
            {
                _webProcess.Kill(true);
                _webProcess.WaitForExit();
            }
            _webProcess?.Dispose();

            if (_apiProcess is { HasExited: false })
            {
                _apiProcess.Kill(true);
                _apiProcess.WaitForExit();
            }
            _apiProcess?.Dispose();
            return Task.CompletedTask;
        }
    }

    [CollectionDefinition("Selenium")]
    public class SeleniumCollection : ICollectionFixture<TokoServerFixture> { }

    [Collection("Selenium")]
    public class GUITests
    {
        private readonly TokoServerFixture _server;
        private readonly ITestOutputHelper _output;
        public GUITests(TokoServerFixture server, ITestOutputHelper output) 
        { 
            _server = server;
            _output = output;
        }

        [Fact]
        public void TestGamePage()
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--window-size=1920,1080");
            options.AcceptInsecureCertificates = true;
            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(_server.BaseUrl);
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            var createRoomButton = wait.Until(d => d.FindElement(By.CssSelector(".tile.create-tile")));
            createRoomButton.Click();

            var createRoomSubmissionButton = wait.Until(d => d.FindElement(By.CssSelector(".btn.btn-primary")));
            createRoomSubmissionButton.Click();
            wait.Until(d => d.Url.Contains("/room/"));

            string currentUrl = driver.Url;
            string gameUrl = currentUrl.Replace("/room/", "/game/");
            driver.Navigate().GoToUrl(gameUrl);
            var raceBoard = wait.Until(d => d.FindElement(By.ClassName("track-grid")));
            var statusSection = driver.FindElement(By.ClassName("status-section"));

            _output.WriteLine("Checking visibility of status-section...");
            bool statusSectionVisible = IsElementFullyInViewport(driver, statusSection, "status-section", _output);
            Assert.True(statusSectionVisible, "status-section should be fully visible.");

            _output.WriteLine("Checking visibility of track-grid...");
            bool raceBoardFullyVisible = IsElementFullyInViewport(driver, raceBoard, "track-grid", _output);
            Assert.True(raceBoardFullyVisible, "track-grid should be fully visible.");

            _output.WriteLine("Checking grid tile and image size...");
            var gridTile = wait.Until(d => d.FindElement(By.ClassName("grid-tile")));
            var gridTileSize = gridTile.Size;
            _output.WriteLine($"Grid tile size: {gridTileSize.Width}x{gridTileSize.Height}");

            var image = gridTile.FindElement(By.TagName("img"));
            var imageSize = image.Size;
            _output.WriteLine($"Image render size: {imageSize.Width}x{imageSize.Height}");

            Assert.True(Math.Abs(gridTileSize.Width - imageSize.Width) <= 0.1, $"Grid tile width ({gridTileSize.Width}) and image width ({imageSize.Width}) should be almost equal.");
            Assert.True(Math.Abs(gridTileSize.Height - imageSize.Height) <= 0.1, $"Grid tile height ({gridTileSize.Height}) and image height ({imageSize.Height}) should be almost equal.");
        }

        static bool IsElementFullyInViewport(IWebDriver driver, IWebElement element, string elementName, ITestOutputHelper output)
        {
            var js = (IJavaScriptExecutor)driver;
            var raw = js.ExecuteScript(
                "const r = arguments[0].getBoundingClientRect();" +
                "return { top: r.top, bottom: r.bottom, left: r.left, right: r.right };",
                element
            );
            if (raw is not IDictionary<string, object> rectDict)
            {
                throw new InvalidOperationException("Could not get element position info from JS.");
            }
            double top    = ConvertToDouble(rectDict, "top");
            double bottom = ConvertToDouble(rectDict, "bottom");
            double left   = ConvertToDouble(rectDict, "left");
            double right  = ConvertToDouble(rectDict, "right");
            var viewportHeight = Convert.ToDouble(js.ExecuteScript("return window.innerHeight") ?? throw new InvalidOperationException("Could not get window height"));
            var viewportWidth  = Convert.ToDouble(js.ExecuteScript("return window.innerWidth")  ?? throw new InvalidOperationException("Could not get window width"));

            output.WriteLine($"Viewport size: Width={viewportWidth}, Height={viewportHeight}");
            output.WriteLine($"Element '{elementName}' bounding box: Top={top}, Bottom={bottom}, Left={left}, Right={right}");

            bool isFullyVisible = top >= 0
                && bottom <= viewportHeight
                && left >= 0
                && right <= viewportWidth;

            output.WriteLine(isFullyVisible
                ? $"Element '{elementName}' is fully visible, no scrolling needed."
                : $"Element '{elementName}' is not fully visible, scrolling required.");
            
            return isFullyVisible;
        }

        private static double ConvertToDouble(IDictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var obj) || obj == null)
                throw new InvalidOperationException($"JS result missing '{key}' field or it is null.");
            return Convert.ToDouble(obj);
        }
    }
}

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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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
        public GUITests(TokoServerFixture server) => _server = server;

        [Fact]
        public void TestGamePage()
        {
            var options = new ChromeOptions();
            // options.AddArgument("--headless=new");
            options.AddArgument("--ignore-certificate-errors");
            options.AcceptInsecureCertificates = true;
            using var driver = new ChromeDriver(options);
            driver.Manage().Window.Maximize();
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
            var raceBoard = wait.Until(d => d.FindElement(By.ClassName("race-board-container")));
            bool fullyVisible = IsElementFullyInViewport(driver, raceBoard);
            Console.WriteLine(fullyVisible
                ? "race-board-container is fully visible, no scrolling needed."
                : "race-board-container is not fully visible, scrolling required.");
            Assert.True(fullyVisible);
        }

        static bool IsElementFullyInViewport(IWebDriver driver, IWebElement element)
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
            return top    >= 0
                && bottom <= viewportHeight
                && left   >= 0
                && right  <= viewportWidth;
        }

        private static double ConvertToDouble(IDictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var obj) || obj == null)
                throw new InvalidOperationException($"JS result missing '{key}' field or it is null.");
            return Convert.ToDouble(obj);
        }
    }
}

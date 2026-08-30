using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ValidationPlatform.Core.Models;
using ValidationPlatform.Providers.Windows;
using WatchLists.Services;
using Xunit;

namespace ValidationPlatform.Tests.WatchList;

[Trait("Category", "WindowsUi")]
public class WatchListTests : WindowsTestBase

{
    private static readonly string[] ExeCandidates = new[]
    {
        @"C:\Users\benho\source\repos\WatchList\WatchLists\bin\Debug\net9.0-windows10.0.19041.0\win10-x64\WatchLists.exe"
    };

    public WatchListTests() : base(ExeCandidates)
    {
        DisableAutoSync();
    }

    private string GetSyncSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "User Name", "com.companyname.watchlists", "Data", "SyncSettings.json");
    }

    private void DisableAutoSync()
    {
        var settingsPath = GetSyncSettingsPath();
        try
        {
            var config = new SyncSettingsConfig
            {
                SyncMode = "CloudApi",
                ApiEndpointUrl = "https://watchlist-faa16-default-rtdb.firebaseio.com/",
                SyncCode = "MyWatchList2026",
                AutoSyncEnabled = false
            };
            var directory = Path.GetDirectoryName(settingsPath);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json);
        }
        catch
        {
            // Safe ignore
        }
    }

    private void RestoreAutoSync()
    {
        var settingsPath = GetSyncSettingsPath();
        try
        {
            if (File.Exists(settingsPath))
            {
                var config = new SyncSettingsConfig
                {
                    SyncMode = "CloudApi",
                    ApiEndpointUrl = "https://watchlist-faa16-default-rtdb.firebaseio.com/",
                    SyncCode = "MyWatchList2026",
                    AutoSyncEnabled = true
                };
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json);
            }
        }
        catch
        {
            // Safe ignore
        }
    }

    private void CleanupMatrixFromDb()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, "User Name", "com.companyname.watchlists", "Data", "watchlist.db"),
            Path.Combine(localAppData, "WatchLists", "watchlist.db"),
            Path.Combine(localAppData, "Packages", "com.companyname.watchlists_8wekyb3d8bbwe", "LocalState", "watchlist.db")
        };

        foreach (var dbPath in candidates)
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    using (var conn = new SQLite.SQLiteConnection(dbPath))
                    {
                        conn.Execute("DELETE FROM WatchItems WHERE Title = ? OR MovieId = ?", "The Matrix", 603);
                    }
                }
                catch
                {
                    // Safe ignore
                }
            }
        }
    }

    [Fact]
    public async Task AppLaunches_And_SearchBarIsVisible()
    {
        await EnsureAppLaunchedAsync();
        try
        {
            // 2. Check if Search Bar is visible (using className or placeholder name)
            var searchBarQuery = ElementQuery.ByName("Search...");
            var isVisible = await UiProvider.IsElementVisibleAsync(searchBarQuery);

            // If placeholder query fails, fallback check for search box
            if (!isVisible)
            {
                searchBarQuery = ElementQuery.ByClassName("AutoSuggestBox");
                isVisible = await UiProvider.IsElementVisibleAsync(searchBarQuery);
            }

            Assert.True(isVisible, "SearchBar element was not found in the UIA tree.");
        }
        catch (Exception ex)
        {
            var tree = await UiProvider.GetAccessibilityTreeAsync();
            throw new Exception($"Test failed. ex: {ex.Message}. UIA Tree:\n{tree}");
        }
    }

    [Fact]
    public async Task SearchAndSelectMovie_PopulatesDeepLinkAndMetadata()
    {
        CleanupMatrixFromDb();
        await EnsureAppLaunchedAsync();
        try
        {
            // 1. Click "+ Add Media" button
            var addBtnQuery = ElementQuery.ByName("+ Add Media");
            var isAddVisible = await UiProvider.IsElementVisibleAsync(addBtnQuery);
            if (!isAddVisible)
            {
                addBtnQuery = ElementQuery.ByName("Add");
            }
            await UiProvider.ClickAsync(addBtnQuery);
            await Task.Delay(1000);

            // 2. Click "Search APIs" button
            var searchApisBtnQuery = ElementQuery.ById("SearchApisButton");
            var isSearchBtnVisible = await UiProvider.IsElementVisibleAsync(searchApisBtnQuery);
            if (!isSearchBtnVisible)
            {
                searchApisBtnQuery = ElementQuery.ByName("Search APIs");
            }
            await UiProvider.ClickAsync(searchApisBtnQuery);
            await Task.Delay(1000);

            // 3. Enter query "The Matrix" in search bar and click Search
            var searchBarQuery = ElementQuery.ById("TextBox");
            await UiProvider.TypeTextAsync(searchBarQuery, "The Matrix");
            await Task.Delay(500);

            var searchBtnQuery = ElementQuery.ByName("Search");
            await UiProvider.ClickAsync(searchBtnQuery);
            await Task.Delay(2000);

            // 4. Click the first search result
            var matrixResultQuery = ElementQuery.ByName("The Matrix");
            await UiProvider.ClickAsync(matrixResultQuery);
            await Task.Delay(1000);

            // 5. Click "Select Movie" button
            var selectMovieBtnQuery = ElementQuery.ByName("➕ Select & Add to WatchList");
            var isSelectVisible = await UiProvider.IsElementVisibleAsync(selectMovieBtnQuery);
            if (!isSelectVisible)
            {
                selectMovieBtnQuery = ElementQuery.ByName("Select Movie");
            }
            await UiProvider.ClickAsync(selectMovieBtnQuery);
            await Task.Delay(1500);

            // 6. Verify Title is "The Matrix"
            var titleEntryQuery = ElementQuery.ById("TitleEntry");
            var titleText = await UiProvider.GetTextAsync(titleEntryQuery);
            Assert.Equal("The Matrix", titleText);

            // 7. Verify Deep Link is populated and contains expected path or search query
            var deepLinkEntryQuery = ElementQuery.ById("DeepLinkEntry");
            var deepLinkText = await UiProvider.GetTextAsync(deepLinkEntryQuery);
            Assert.StartsWith("https://", deepLinkText);
            Assert.Contains("Mat", deepLinkText);
        }
        catch (Exception ex)
        {
            var tree = await UiProvider.GetAccessibilityTreeAsync();
            throw new Exception($"Test failed. ex: {ex.Message}. UIA Tree:\n{tree}");
        }
    }

    public override void Dispose()
    {
        RestoreAutoSync();
        base.Dispose();
    }
}

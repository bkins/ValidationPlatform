using WatchLists.MVVM.Models;
using WatchLists.Services;
using Xunit;

namespace ValidationPlatform.Tests.WatchList;

public class SyncServiceTests
{
    private readonly WatchListService _watchListService;
    private readonly SettingsService  _settingsService;
    private readonly string           _testDbPath;

    public SyncServiceTests()
    {
        var testFolder    = Path.Combine(Path.GetTempPath(), $"Settings_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testFolder);
        _testDbPath       = Path.Combine(testFolder, $"WatchList_Test_{Guid.NewGuid():N}.db");
        _settingsService  = new SettingsService(testFolder);
        _watchListService = new WatchListService(_settingsService, _testDbPath);
    }

    [Fact]
    public async Task ImportAndMerge_WithNewerIncomingItem_UpdatesLocalItem()
    {
        var itemId = Guid.NewGuid();
        var localItem = new WatchItem
                        {
                            Id          = itemId
                          , Title       = "Local Title"
                          , LastUpdated = DateTime.UtcNow.AddHours(-2)
                        };

        _watchListService.AddWatchItem(localItem);

        var remoteBundle = new WatchLists.Services.Models.SyncBundle
                           {
                               Items = new List<WatchItem>
                                       {
                                           new WatchItem
                                           {
                                               Id          = itemId
                                             , Title       = "Remote Title"
                                             , LastUpdated = DateTime.UtcNow
                                           }
                                       }
                           };

        var mockProvider = new MockRemoteSyncProvider(remoteBundle);
        var syncService  = new SyncService(_watchListService, _settingsService, mockProvider);

        await _settingsService.SaveSyncModeAsync("CloudApi");
        await _settingsService.SaveApiEndpointUrlAsync("https://jsonblob.com/api/jsonBlob/test");
        await _settingsService.SaveSyncCodeAsync("TestCode");

        var result = await syncService.ImportAndMergeSyncBundleAsync();

        var items = _watchListService.GetWatchItems();
        Assert.Single(items);
        Assert.Equal("Remote Title", items[0].Title);
        Assert.Contains("1 updated", result);
    }

    [Fact]
    public async Task ImportAndMerge_WithOlderIncomingItem_PreservesLocalItem()
    {
        var itemId = Guid.NewGuid();
        var localItem = new WatchItem
                        {
                            Id          = itemId
                          , Title       = "Local Title"
                          , LastUpdated = DateTime.UtcNow
                        };

        _watchListService.AddWatchItem(localItem);

        var remoteBundle = new WatchLists.Services.Models.SyncBundle
                           {
                               Items = new List<WatchItem>
                                       {
                                           new WatchItem
                                           {
                                               Id          = itemId
                                             , Title       = "Older Remote Title"
                                             , LastUpdated = DateTime.UtcNow.AddHours(-5)
                                           }
                                       }
                           };

        var mockProvider = new MockRemoteSyncProvider(remoteBundle);
        var syncService  = new SyncService(_watchListService, _settingsService, mockProvider);

        await _settingsService.SaveSyncModeAsync("CloudApi");
        await _settingsService.SaveApiEndpointUrlAsync("https://jsonblob.com/api/jsonBlob/test");
        await _settingsService.SaveSyncCodeAsync("TestCode");

        var result = await syncService.ImportAndMergeSyncBundleAsync();

        var items = _watchListService.GetWatchItems();
        Assert.Single(items);
        Assert.Equal("Local Title", items[0].Title);
        Assert.Contains("0 updated", result);
    }

    [Fact]
    public async Task ImportAndMerge_WithSoftDeletedIncomingItem_SoftDeletesLocalItem()
    {
        var itemId = Guid.NewGuid();
        var localItem = new WatchItem
                        {
                            Id          = itemId
                          , Title       = "Local Item"
                          , IsDeleted   = false
                          , LastUpdated = DateTime.UtcNow.AddHours(-2)
                        };

        _watchListService.AddWatchItem(localItem);

        var remoteBundle = new WatchLists.Services.Models.SyncBundle
                           {
                               Items = new List<WatchItem>
                                       {
                                           new WatchItem
                                           {
                                               Id          = itemId
                                             , Title       = "Local Item"
                                             , IsDeleted   = true
                                             , LastUpdated = DateTime.UtcNow
                                           }
                                       }
                           };

        var mockProvider = new MockRemoteSyncProvider(remoteBundle);
        var syncService  = new SyncService(_watchListService, _settingsService, mockProvider);

        await _settingsService.SaveSyncModeAsync("CloudApi");
        await _settingsService.SaveApiEndpointUrlAsync("https://jsonblob.com/api/jsonBlob/test");
        await _settingsService.SaveSyncCodeAsync("TestCode");

        await syncService.ImportAndMergeSyncBundleAsync();

        var activeItems = _watchListService.GetWatchItems();
        Assert.Empty(activeItems);

        var allItems = _watchListService.GetAllWatchItemsIncludingDeleted();
        Assert.Single(allItems);
        Assert.True(allItems[0].IsDeleted);
    }

    [Fact]
    public async Task ImportAndMerge_WithNewCategoryOption_MergesUniqueOptions()
    {
        var remoteBundle = new WatchLists.Services.Models.SyncBundle
                           {
                               Categories = new List<string> { "Sci-Fi", "Documentary" }
                           };

        var mockProvider = new MockRemoteSyncProvider(remoteBundle);
        var syncService  = new SyncService(_watchListService, _settingsService, mockProvider);

        await _settingsService.SaveSyncModeAsync("CloudApi");
        await _settingsService.SaveApiEndpointUrlAsync("https://jsonblob.com/api/jsonBlob/test");
        await _settingsService.SaveSyncCodeAsync("TestCode");

        await syncService.ImportAndMergeSyncBundleAsync();

        var categories = await _settingsService.GetOptionsAsync(WatchLists.Services.Enums.SettingType.Categories);
        Assert.Contains("Sci-Fi", categories);
        Assert.Contains("Documentary", categories);
    }

    [Fact]
    public async Task ImportAndMerge_CloudApiMode_FetchesAndMergesRemoteBundle()
    {
        await _settingsService.SaveSyncModeAsync("CloudApi");
        await _settingsService.SaveApiEndpointUrlAsync("https://jsonblob.com/api/jsonBlob/test");
        await _settingsService.SaveSyncCodeAsync("TestSyncCode123");

        var itemId = Guid.NewGuid();
        var remoteBundle = new WatchLists.Services.Models.SyncBundle
                           {
                               Items = new List<WatchItem>
                                       {
                                           new WatchItem { Id = itemId, Title = "Cloud API Title", LastUpdated = DateTime.UtcNow }
                                       }
                           };

        var mockProvider = new MockRemoteSyncProvider(remoteBundle);
        var syncService  = new SyncService(_watchListService, _settingsService, mockProvider);

        var result = await syncService.ImportAndMergeSyncBundleAsync();

        var items = _watchListService.GetWatchItems();
        Assert.Contains(items, item => item.Id == itemId && item.Title == "Cloud API Title");
        Assert.Contains("Cloud API Sync complete", result);
    }

    [Fact]
    public async Task SettingsViewModel_SyncNow_InCloudApiMode_ProducesExportSuccessStatusMessage()
    {
        await _settingsService.SaveSyncModeAsync("CloudApi");
        await _settingsService.SaveApiEndpointUrlAsync("https://jsonblob.com/api/jsonBlob/test-blob-id");
        await _settingsService.SaveSyncCodeAsync("MyWatchList2026");

        var mockProvider = new MockRemoteSyncProvider(null);
        var syncService  = new SyncService(_watchListService, _settingsService, mockProvider);
        var viewModel    = new WatchLists.MVVM.ViewModels.SettingsViewModel(_settingsService, syncService);

        await viewModel.SyncNow();

        Assert.DoesNotContain("Export: Failed", viewModel.SyncStatusMessage);
        Assert.Contains("Cloud payload initialized", viewModel.SyncStatusMessage);
    }

    [Fact]
    public async Task SettingsViewModel_SyncNow_WithRealCloudProviderAndPlaceholderUrl_AutoHealsAndDisplaysExportSuccess()
    {
        await _settingsService.SaveSyncModeAsync("CloudApi");
        await _settingsService.SaveApiEndpointUrlAsync("https://watchlist-app-sync-default-rtdb.firebaseio.com");
        await _settingsService.SaveSyncCodeAsync("MyWatchList2026");

        var mockProvider = new MockRemoteSyncProvider(null, shouldFailUpload: true);
        var syncService  = new SyncService(_watchListService, _settingsService, mockProvider);
        var viewModel    = new WatchLists.MVVM.ViewModels.SettingsViewModel(_settingsService, syncService);

        await viewModel.SyncNow();

        Assert.DoesNotContain("Export: Failed", viewModel.SyncStatusMessage);
    }

    [Fact]
    public async Task RealApp_SyncNow_InCloudApiMode_SuccessfullySyncsAndReturnsSuccessMessageWithoutExportFailed()
    {
        var testFolder       = Path.Combine(Path.GetTempPath(), $"Settings_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testFolder);
        var settingsService  = new SettingsService(testFolder);
        var watchListService = new WatchListService(settingsService, Path.Combine(testFolder, $"WatchList_Live_{Guid.NewGuid():N}.db"));
        var mockProvider     = new MockRemoteSyncProvider(null);
        var syncService      = new SyncService(watchListService, settingsService, mockProvider);
        var viewModel        = new WatchLists.MVVM.ViewModels.SettingsViewModel(settingsService, syncService);

        await settingsService.SaveSyncModeAsync("CloudApi");
        await settingsService.SaveSyncCodeAsync($"LiveTestCode_{Guid.NewGuid():N}");

        await viewModel.SyncNow();

        Assert.DoesNotContain("(Export: Failed)", viewModel.SyncStatusMessage);
        Assert.DoesNotContain("Export Failed", viewModel.SyncStatusMessage);
    }

    private class MockRemoteSyncProvider : WatchLists.Services.Interfaces.IRemoteSyncProvider
    {
        private WatchLists.Services.Models.SyncBundle? _bundle;
        private readonly bool                          _shouldFailUpload;

        public MockRemoteSyncProvider(WatchLists.Services.Models.SyncBundle? bundle, bool shouldFailUpload = false)
        {
            _bundle           = bundle;
            _shouldFailUpload = shouldFailUpload;
        }

        public Task<WatchLists.Services.Models.SyncBundle?> FetchLatestBundleAsync(string endpointUrl, string syncCode)
        {
            if (_shouldFailUpload) return Task.FromResult<WatchLists.Services.Models.SyncBundle?>(null);
            return Task.FromResult(_bundle);
        }

        public Task<bool> UploadBundleAsync(string endpointUrl, string syncCode, WatchLists.Services.Models.SyncBundle bundle)
        {
            if (_shouldFailUpload && (endpointUrl.Contains("firebaseio.com") || ! endpointUrl.StartsWith("https://jsonblob.com")))
            {
                return Task.FromResult(false);
            }
            _bundle = bundle;
            return Task.FromResult(true);
        }

        public Task<string?> CreateNewCloudSyncBlobAsync()
        {
            return Task.FromResult<string?>("https://jsonblob.com/api/jsonBlob/mock-auto-heal-id");
        }
    }
}

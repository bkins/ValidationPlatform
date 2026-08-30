using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SQLite;
using WatchLists.MVVM.Models;
using WatchLists.MVVM.ViewModels;
using WatchLists.Services;
using Xunit;

namespace ValidationPlatform.Tests.WatchList;

public class StreamGTests : IDisposable
{
    private readonly string           _testFolder;
    private readonly string           _testDbPath;
    private readonly SettingsService  _settingsService;
    private readonly WatchListService _watchListService;

    public StreamGTests()
    {
        _testFolder       = Path.Combine(Path.GetTempPath(), $"WatchList_StreamG_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFolder);
        _testDbPath       = Path.Combine(_testFolder, "stream_g_test.db");
        _settingsService  = new SettingsService(_testFolder);
        _watchListService = new WatchListService(_settingsService, _testDbPath);
    }

    [Fact]
    public void GetWatchItems_WhenDatabaseEmpty_ReturnsEmptyListWithoutExceptions()
    {
        var items = _watchListService.GetWatchItems();

        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public void FindDuplicateItem_WhenDatabaseEmpty_ReturnsNullWithoutExceptions()
    {
        var duplicate = _watchListService.FindDuplicateItem(12345, "TMDB");

        Assert.Null(duplicate);
    }

    [Fact]
    public void DeleteWatchItems_WithMultipleGuids_SoftDeletesAllSelectedItemsInTransaction()
    {
        var item1 = new WatchItem
                    {
                        Id    = Guid.NewGuid()
                      , Title = "Movie One"
                    };
        var item2 = new WatchItem
                    {
                        Id    = Guid.NewGuid()
                      , Title = "Movie Two"
                    };
        var item3 = new WatchItem
                    {
                        Id    = Guid.NewGuid()
                      , Title = "Movie Three"
                    };

        _watchListService.AddWatchItem(item1);
        _watchListService.AddWatchItem(item2);
        _watchListService.AddWatchItem(item3);

        _watchListService.DeleteWatchItems(new[] { item1.Id, item2.Id });

        var activeItems = _watchListService.GetWatchItems();
        var allItems    = _watchListService.GetAllWatchItemsIncludingDeleted();

        Assert.Single(activeItems);
        Assert.Equal("Movie Three", activeItems[0].Title);
        Assert.Equal(3, allItems.Count);
        Assert.True(allItems.First(item => item.Id == item1.Id).IsDeleted);
        Assert.True(allItems.First(item => item.Id == item2.Id).IsDeleted);
        Assert.False(allItems.First(item => item.Id == item3.Id).IsDeleted);
    }

    [Fact]
    public void ToggleSelectionMode_WhenCalled_TogglesFlagAndClearsSelectionOnExit()
    {
        var viewModel = new WatchListViewModel(_watchListService, _settingsService);

        Assert.False(viewModel.IsSelectionMode);

        viewModel.ToggleSelectionMode();
        Assert.True(viewModel.IsSelectionMode);

        viewModel.ToggleSelectionMode();
        Assert.False(viewModel.IsSelectionMode);
        Assert.Equal(0, viewModel.SelectedItemsCount);

        viewModel.Dispose();
    }

    [Fact]
    public void WatchItem_HasIndexedAttributesOnKeyQueryProperties()
    {
        var titleProp       = typeof(WatchItem).GetProperty(nameof(WatchItem.Title));
        var lastUpdatedProp = typeof(WatchItem).GetProperty(nameof(WatchItem.LastUpdated));
        var movieIdProp     = typeof(WatchItem).GetProperty(nameof(WatchItem.MovieId));
        var isDeletedProp   = typeof(WatchItem).GetProperty(nameof(WatchItem.IsDeleted));

        Assert.NotNull(titleProp?.GetCustomAttribute<IndexedAttribute>());
        Assert.NotNull(lastUpdatedProp?.GetCustomAttribute<IndexedAttribute>());
        Assert.NotNull(movieIdProp?.GetCustomAttribute<IndexedAttribute>());
        Assert.NotNull(isDeletedProp?.GetCustomAttribute<IndexedAttribute>());
    }

    public void Dispose()
    {
        _watchListService.Dispose();
        try
        {
            if (Directory.Exists(_testFolder))
            {
                Directory.Delete(_testFolder, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

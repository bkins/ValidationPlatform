using Microsoft.Extensions.Configuration;
using System.Net.Http;
using WatchLists.Services;
using Xunit;

namespace ValidationPlatform.Tests.WatchList;

public class StreamingServicesTests
{
    [Fact]
    public void WatchmodeService_WithApiKey_SetsIsEnabledTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "WatchMode:ApiKey", "test_key" } })
            .Build();
        var httpClient = new HttpClient();

        var service = new WatchmodeService(httpClient, config);

        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void WatchmodeService_WithoutApiKey_SetsIsEnabledFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var httpClient = new HttpClient();

        var service = new WatchmodeService(httpClient, config);

        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void StreamingAvailabilityService_WithApiKey_SetsIsEnabledTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "RapidAPI:ApiKey", "test_key" } })
            .Build();
        var httpClient = new HttpClient();

        var service = new StreamingAvailabilityService(httpClient, config);

        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void StreamingAvailabilityService_WithoutApiKey_SetsIsEnabledFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var httpClient = new HttpClient();

        var service = new StreamingAvailabilityService(httpClient, config);

        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void TvMazeService_Always_SetsIsEnabledTrue()
    {
        var httpClient = new HttpClient();

        var service = new TvMazeService(httpClient);

        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void OmdbService_WithApiKey_SetsIsEnabledTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "OMDB:ApiKey", "test_key" } })
            .Build();
        var httpClient = new HttpClient();

        var service = new OmdbService(httpClient, config);

        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void OmdbService_WithoutApiKey_SetsIsEnabledFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var httpClient = new HttpClient();

        var service = new OmdbService(httpClient, config);

        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void FmDbService_Always_SetsIsEnabledTrue()
    {
        var httpClient = new HttpClient();

        var service = new FmDbService(httpClient);

        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void WatchItem_WithAvailableStreamingServices_FormatsDisplayString()
    {
        var item = new WatchLists.MVVM.Models.WatchItem
        {
            Title                      = "Inception",
            AvailableStreamingServices = "Netflix, Prime Video"
        };

        Assert.True(item.HasAvailableStreamingServices);
        Assert.Equal("Available on: Netflix, Prime Video", item.AvailableStreamingServicesDisplay);
    }

    [Fact]
    public void WatchItem_WithoutAvailableStreamingServices_FormatsFallbackDisplayString()
    {
        var item = new WatchLists.MVVM.Models.WatchItem
        {
            Title                      = "Inception",
            AvailableStreamingServices = ""
        };

        Assert.False(item.HasAvailableStreamingServices);
        Assert.Equal("Streaming: Not checked (tap Refresh)", item.AvailableStreamingServicesDisplay);
    }

    [Fact]
    public void MovieSearchResult_WithSourceApis_FormatsSourceApisDisplay()
    {
        var result = new WatchLists.Services.Models.MovieSearchResult
        {
            Title      = "Avatar: The Last Airbender",
            SourceApis = new List<string> { "TMDB", "TVMaze" }
        };

        Assert.True(result.HasSourceApis);
        Assert.Equal("Source API: TMDB, TVMaze", result.SourceApisDisplay);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using WatchLists.DataAccess.Interfaces;
using WatchLists.MVVM.Models;
using WatchLists.Services;
using WatchLists.Services.Models;
using Xunit;

namespace ValidationPlatform.Tests.WatchList;

public class MovieDataAggregatorTests
{
    [Fact]
    public async Task SearchMoviesAsync_CombinesFieldsFromMultipleProviders_PopulatesAggregatedData()
    {
        var provider1Mock = new Mock<IMovieDataProvider>();
        provider1Mock.Setup(provider => provider.IsEnabled).Returns(true);
        provider1Mock.Setup(provider => provider.SearchMoviesAsync("Inception"))
                     .ReturnsAsync(new AggregatedResult<MovieSearchResponse?>
                     {
                         Data = new MovieSearchResponse
                         {
                             Results = new List<MovieSearchResult>
                             {
                                 new MovieSearchResult
                                 {
                                     Id = 100,
                                     Title = "Inception",
                                     PosterPath = "/tmdb_inception.jpg",
                                     Overview = "A thief who steals corporate secrets.",
                                     StreamingProviders = new List<string> { "Netflix" }
                                 }
                             }
                         }
                     });

        var provider2Mock = new Mock<IMovieDataProvider>();
        provider2Mock.Setup(provider => provider.IsEnabled).Returns(true);
        provider2Mock.Setup(provider => provider.SearchMoviesAsync("Inception"))
                     .ReturnsAsync(new AggregatedResult<MovieSearchResponse?>
                     {
                         Data = new MovieSearchResponse
                         {
                             Results = new List<MovieSearchResult>
                             {
                                 new MovieSearchResult
                                 {
                                     Id = 100,
                                     Title = "Inception",
                                     PosterPath = "https://omdb.com/poster.jpg",
                                     Overview = "Sci-fi thriller about dreams.",
                                     StreamingProviders = new List<string> { "Prime Video" }
                                 }
                             }
                         }
                     });

        var aggregator = new MovieDataAggregator(new[] { provider1Mock.Object, provider2Mock.Object });

        var result = await aggregator.SearchMoviesAsync("Inception");

        Assert.NotNull(result?.Data?.Results);
        var firstResult = result.Data.Results.FirstOrDefault();
        Assert.NotNull(firstResult);
        Assert.Contains("Netflix", firstResult.StreamingProviders);
        Assert.Contains("Prime Video", firstResult.StreamingProviders);
        Assert.NotNull(firstResult.AggregatedData);
        Assert.True(firstResult.AggregatedData.Count > 0);
    }

    [Fact]
    public async Task GetMovieDetailsAsync_AggregatesDetailsAndMetadataFromMultipleProviders()
    {
        var provider1Mock = new Mock<IMovieDataProvider>();
        provider1Mock.Setup(provider => provider.IsEnabled).Returns(true);
        provider1Mock.Setup(provider => provider.GetMovieDetailsAsync(200))
                     .ReturnsAsync(new AggregatedResult<MovieDetail?>
                     {
                         Data = new MovieDetail
                         {
                             Id = 200,
                             Title = "Interstellar",
                             ReleaseDate = "2014-11-07",
                             Overview = "A team of explorers travel through a wormhole in space."
                         }
                     });

        var provider2Mock = new Mock<IMovieDataProvider>();
        provider2Mock.Setup(provider => provider.IsEnabled).Returns(true);
        provider2Mock.Setup(provider => provider.GetMovieDetailsAsync(200))
                     .ReturnsAsync(new AggregatedResult<MovieDetail?>
                     {
                         Data = new MovieDetail
                         {
                             Id = 200,
                             Title = "Interstellar",
                             Genres = new List<Genre> { new Genre { Id = 1, Name = "Sci-Fi" } },
                             StreamingProviders = new List<string> { "Paramount+" }
                         }
                     });

        var aggregator = new MovieDataAggregator(new[] { provider1Mock.Object, provider2Mock.Object });

        var result = await aggregator.GetMovieDetailsAsync(200);

        Assert.NotNull(result?.Data);
        Assert.Equal("Interstellar", result.Data.Title);
        Assert.Equal("2014-11-07", result.Data.ReleaseDate);
        Assert.Single(result.Data.Genres);
        Assert.Contains("Paramount+", result.Data.StreamingProviders);
        Assert.NotNull(result.Data.AggregatedData);
        Assert.True(result.Data.AggregatedData.Count > 0);
    }

    [Fact]
    public void WatchItem_AggregatedData_SerializesAndDeserializesJsonCorrectly()
    {
        var initialDict = new Dictionary<string, string>
        {
            { "Tmdb:imdb_id", "tt1375666" },
            { "Omdb:imdbRating", "8.8" },
            { "ImdbRating", "8.8" }
        };

        var watchItem = new WatchItem
        {
            Title = "Inception",
            AggregatedData = initialDict
        };

        Assert.False(string.IsNullOrWhiteSpace(watchItem.AggregatedDataJson));
        
        var reloadedWatchItem = new WatchItem
        {
            AggregatedDataJson = watchItem.AggregatedDataJson
        };

        Assert.Equal("tt1375666", reloadedWatchItem.AggregatedData["Tmdb:imdb_id"]);
        Assert.Equal("8.8", reloadedWatchItem.AggregatedData["Omdb:imdbRating"]);
        Assert.Equal("8.8", reloadedWatchItem.AggregatedData["ImdbRating"]);
    }

    [Fact]
    public void WatchItem_AggregatedDataItems_ReturnsKeyValueListForUIBinding()
    {
        var watchItem = new WatchItem
        {
            Title = "Avatar",
            AggregatedData = new Dictionary<string, string>
            {
                { "Tmdb:release_date", "2009-12-18" },
                { "Omdb:Rated", "PG-13" }
            }
        };

        var items = watchItem.AggregatedData.ToList();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.Key == "Tmdb:release_date" && item.Value == "2009-12-18");
        Assert.Contains(items, item => item.Key == "Omdb:Rated" && item.Value == "PG-13");
    }

    [Fact]
    public async Task GetMovieDetailsAsync_IgnoresMismatchedProviderTitles_WhenIdCollides()
    {
        var tmdbMock = new Mock<IMovieDataProvider>();
        tmdbMock.Setup(provider => provider.IsEnabled).Returns(true);
        tmdbMock.Setup(provider => provider.GetMovieDetailsAsync(241))
                .ReturnsAsync(new AggregatedResult<MovieDetail?>
                {
                    Data = new MovieDetail
                    {
                        Id = 241,
                        Title = "Natural Born Killers",
                        Overview = "Two victims of traumatized childhoods become lover-killers.",
                        PrimarySourceApi = "TMDB"
                    }
                });

        var tvMazeMock = new Mock<IMovieDataProvider>();
        tvMazeMock.Setup(provider => provider.IsEnabled).Returns(true);
        tvMazeMock.Setup(provider => provider.GetMovieDetailsAsync(241))
                  .ReturnsAsync(new AggregatedResult<MovieDetail?>
                  {
                      Data = new MovieDetail
                      {
                          Id = 241,
                          Title = "Benched",
                          Overview = "Unrelated TV show with colliding ID 241",
                          PrimarySourceApi = "TVMaze"
                      }
                  });

        var aggregator = new MovieDataAggregator(new[] { tmdbMock.Object, tvMazeMock.Object });

        var result = await aggregator.GetMovieDetailsAsync(241);

        Assert.NotNull(result?.Data);
        Assert.Equal("Natural Born Killers", result.Data.Title);
        Assert.DoesNotContain(result.Data.AggregatedData, kvp => kvp.Value.Contains("Benched"));
        Assert.Contains(result.Diagnostics.Values, v => v.Contains("title mismatch"));
    }
}

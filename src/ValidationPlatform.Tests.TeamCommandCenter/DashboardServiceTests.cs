using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamCommandCenter.Core.Domain;
using TeamCommandCenter.Infrastructure;
using TeamCommandCenter.Infrastructure.Persistence;
using Xunit;

namespace ValidationPlatform.Tests.TeamCommandCenter;

public class DashboardServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext      _dbContext;

    public DashboardServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
                      .UseSqlite(_connection)
                      .Options;

        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetStoryPointsSummaryAsync_CalculatesAveragesCorrectly_WhenDataExists()
    {
        var developer = new Developer
                        {
                            AdoId = "dev-1"
                          , Name  = "Alice Smith"
                          , Email = "alice@example.com"
                        };

        var project = new Project
                      {
                          AdoId = "proj-1"
                        , Name  = "CorePlatform"
                      };

        var iteration1 = new Iteration
                         {
                             AdoId        = "iter-1"
                           , Name         = "Sprint 1"
                           , ProjectAdoId = "proj-1"
                           , StartDate    = new DateOnly(2026, 6, 1)
                           , EndDate      = new DateOnly(2026, 6, 14)
                         };

        var iteration2 = new Iteration
                         {
                             AdoId        = "iter-2"
                           , Name         = "Sprint 2"
                           , ProjectAdoId = "proj-1"
                           , StartDate    = new DateOnly(2026, 6, 15)
                           , EndDate      = new DateOnly(2026, 6, 28)
                         };

        _dbContext.Developers.Add(developer);
        _dbContext.Projects.Add(project);
        _dbContext.Iterations.AddRange(iteration1, iteration2);
        await _dbContext.SaveChangesAsync();

        var workItem1 = new WorkItem
                        {
                            AdoId                 = "wi-1"
                          , Title                 = "Story 1"
                          , Type                  = "User Story"
                          , State                 = "Closed"
                          , StoryPoints           = 5.0
                          , ProjectId             = project.Id
                          , AssignedToDeveloperId = developer.Id
                          , IterationId           = iteration1.Id
                        };

        var workItem2 = new WorkItem
                        {
                            AdoId                 = "wi-2"
                          , Title                 = "Story 2"
                          , Type                  = "User Story"
                          , State                 = "Done"
                          , StoryPoints           = 8.0
                          , ProjectId             = project.Id
                          , AssignedToDeveloperId = developer.Id
                          , IterationId           = iteration2.Id
                        };

        _dbContext.WorkItems.AddRange(workItem1, workItem2);
        await _dbContext.SaveChangesAsync();

        var dashboardService = new DashboardService(_dbContext);

        var result = await dashboardService.GetStoryPointsSummaryAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Developers);
        Assert.Equal("Alice Smith", result.Developers[0].DeveloperName);
        Assert.Equal(6.5, result.Developers[0].AverageStoryPointsPerSprint);
        Assert.Equal(13.0, result.Developers[0].TotalCompletedStoryPoints);
        Assert.Equal(2, result.Developers[0].TotalSprintsCount);

        Assert.Single(result.Projects);
        Assert.Equal("CorePlatform", result.Projects[0].ProjectName);
        Assert.Equal(6.5, result.Projects[0].AverageStoryPointsPerSprint);
        Assert.Equal(6.5, result.OverallProjectAveragePointsPerSprint);
    }

    [Fact]
    public async Task GetWorkloadsAsync_ExcludesRemovedWorkItems()
    {
        var developer = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        var project = new Project { AdoId = "proj-1", Name = "CorePlatform" };

        _dbContext.Developers.Add(developer);
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync();

        var activeItem = new WorkItem
        {
            AdoId                 = "wi-1"
          , Title                 = "Active Task"
          , Type                  = "Task"
          , State                 = "Active"
          , RemainingHours        = 4.0
          , ProjectId             = project.Id
          , AssignedToDeveloperId = developer.Id
        };

        var removedItem = new WorkItem
        {
            AdoId                 = "wi-2"
          , Title                 = "Removed Task"
          , Type                  = "Task"
          , State                 = "Removed"
          , RemainingHours        = 8.0
          , ProjectId             = project.Id
          , AssignedToDeveloperId = developer.Id
        };

        _dbContext.WorkItems.AddRange(activeItem, removedItem);
        await _dbContext.SaveChangesAsync();

        var dashboardService = new DashboardService(_dbContext);
        var result = await dashboardService.GetWorkloadsAsync(CancellationToken.None);

        var devWorkload = Assert.Single(result);
        var item = Assert.Single(devWorkload.WorkItems);
        Assert.Equal("wi-1", item.AdoId);
    }

    [Fact]
    public async Task GetCarryOverSummaryAsync_CalculatesCarryOverMetrics()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var developer = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        var project = new Project { AdoId = "proj-1", Name = "CorePlatform" };
        var iteration = new Iteration
        {
            AdoId        = "iter-1"
          , Name         = "Sprint 1"
          , ProjectAdoId = "proj-1"
          , StartDate    = today.AddDays(-2)
          , EndDate      = today.AddDays(10)
        };

        _dbContext.Developers.Add(developer);
        _dbContext.Projects.Add(project);
        _dbContext.Iterations.Add(iteration);
        await _dbContext.SaveChangesAsync();

        var carryOverItem = new WorkItem
        {
            AdoId                 = "wi-1"
          , Title                 = "Carried Over Story"
          , Type                  = "User Story"
          , State                 = "Active"
          , StoryPoints           = 5.0
          , RemainingHours        = 6.0
          , CreatedDate           = DateTime.Today.AddDays(-15)
          , ProjectId             = project.Id
          , AssignedToDeveloperId = developer.Id
          , IterationId           = iteration.Id
        };

        _dbContext.WorkItems.Add(carryOverItem);
        await _dbContext.SaveChangesAsync();

        var dashboardService = new DashboardService(_dbContext);
        var carryOver = await dashboardService.GetCarryOverSummaryAsync(CancellationToken.None);

        Assert.NotNull(carryOver);
        Assert.Equal(1, carryOver.TotalCarryOverItems);
        Assert.Equal(5.0, carryOver.TotalCarryOverStoryPoints);
        Assert.Equal(6.0, carryOver.TotalCarryOverHours);
        Assert.Single(carryOver.ByDeveloper);
        Assert.Equal("Alice Smith", carryOver.ByDeveloper[0].DeveloperName);
    }
}

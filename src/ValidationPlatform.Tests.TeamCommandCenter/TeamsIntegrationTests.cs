using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamCommandCenter.Core.Domain;
using TeamCommandCenter.Core.DTOs;
using TeamCommandCenter.Infrastructure;
using TeamCommandCenter.Infrastructure.Persistence;
using TeamCommandCenter.Infrastructure.Services;
using Xunit;

namespace ValidationPlatform.Tests.TeamCommandCenter;

public class TeamsIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext      _dbContext;
    private readonly TeamService       _teamService;
    private readonly DashboardService  _dashboardService;

    public TeamsIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
                      .UseSqlite(_connection)
                      .Options;

        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();

        _teamService      = new TeamService(_dbContext);
        _dashboardService = new DashboardService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateTeam_WithMembers_SavesToDatabaseAndReturnsTeamDtoWithMembers()
    {
        var dev1 = new Developer
                   {
                       AdoId                        = "dev-1"
                     , Name                         = "Alice Smith"
                     , Email                        = "alice@example.com"
                     , DefaultCapacityHoursPerSprint = 40.0
                   };

        var dev2 = new Developer
                   {
                       AdoId                        = "dev-2"
                     , Name                         = "Bob Jones"
                     , Email                        = "bob@example.com"
                     , DefaultCapacityHoursPerSprint = 35.0
                   };

        _dbContext.Developers.AddRange(dev1, dev2);
        await _dbContext.SaveChangesAsync();

        var request = new CreateTeamRequest( "Alpha Team"
                                           , "Core Development Team"
                                           , [dev1.Id, dev2.Id] );

        var createdTeam = await _teamService.CreateTeamAsync(request, CancellationToken.None);

        Assert.NotNull(createdTeam);
        Assert.True(createdTeam.Id > 0);
        Assert.Equal("Alpha Team", createdTeam.Name);
        Assert.Equal("Core Development Team", createdTeam.Description);
        Assert.Equal(2, createdTeam.Members.Count);

        var memberEmails = createdTeam.Members.Select(member => member.DeveloperEmail).ToList();
        Assert.Contains("alice@example.com", memberEmails);
        Assert.Contains("bob@example.com", memberEmails);
    }

    [Fact]
    public async Task GetTeams_ReturnsAllTeamsWithMemberLists()
    {
        var dev1 = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        var dev2 = new Developer { AdoId = "dev-2", Name = "Bob Jones", Email = "bob@example.com" };
        _dbContext.Developers.AddRange(dev1, dev2);
        await _dbContext.SaveChangesAsync();

        await _teamService.CreateTeamAsync(new CreateTeamRequest("Alpha Team", "Team Alpha", [dev1.Id]), CancellationToken.None);
        await _teamService.CreateTeamAsync(new CreateTeamRequest("Beta Team", "Team Beta", [dev2.Id]), CancellationToken.None);

        var teams = await _teamService.GetTeamsAsync(CancellationToken.None);

        Assert.Equal(2, teams.Count);
        var alpha = Assert.Single(teams, team => team.Name == "Alpha Team");
        Assert.Single(alpha.Members, member => member.DeveloperName == "Alice Smith");

        var beta = Assert.Single(teams, team => team.Name == "Beta Team");
        Assert.Single(beta.Members, member => member.DeveloperName == "Bob Jones");
    }

    [Fact]
    public async Task GetTeamById_WhenExists_ReturnsTeam_WhenNotExists_ReturnsNull()
    {
        var dev = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        _dbContext.Developers.Add(dev);
        await _dbContext.SaveChangesAsync();

        var created = await _teamService.CreateTeamAsync(new CreateTeamRequest("Gamma Team", "Description", [dev.Id]), CancellationToken.None);

        var fetched = await _teamService.GetTeamByIdAsync(created.Id, CancellationToken.None);
        var nonExistent = await _teamService.GetTeamByIdAsync(99999, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal("Gamma Team", fetched.Name);
        Assert.Single(fetched.Members);
        Assert.Null(nonExistent);
    }

    [Fact]
    public async Task UpdateTeam_UpdatesNameDescriptionAndMemberAssignments()
    {
        var dev1 = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        var dev2 = new Developer { AdoId = "dev-2", Name = "Bob Jones", Email = "bob@example.com" };
        var dev3 = new Developer { AdoId = "dev-3", Name = "Charlie Brown", Email = "charlie@example.com" };
        _dbContext.Developers.AddRange(dev1, dev2, dev3);
        await _dbContext.SaveChangesAsync();

        var created = await _teamService.CreateTeamAsync(new CreateTeamRequest("Delta Team", "Original Description", [dev1.Id, dev2.Id]), CancellationToken.None);

        var updateRequest = new UpdateTeamRequest( "Delta Team Updated"
                                                 , "New Description"
                                                 , [dev2.Id, dev3.Id] );

        var updated = await _teamService.UpdateTeamAsync(created.Id, updateRequest, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Delta Team Updated", updated.Name);
        Assert.Equal("New Description", updated.Description);
        Assert.Equal(2, updated.Members.Count);

        var memberIds = updated.Members.Select(member => member.DeveloperId).ToList();
        Assert.DoesNotContain(dev1.Id, memberIds);
        Assert.Contains(dev2.Id, memberIds);
        Assert.Contains(dev3.Id, memberIds);
    }

    [Fact]
    public async Task DeleteTeam_DeletesTeamAndMemberAssociations()
    {
        var dev = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        _dbContext.Developers.Add(dev);
        await _dbContext.SaveChangesAsync();

        var created = await _teamService.CreateTeamAsync(new CreateTeamRequest("Epsilon Team", "To Be Deleted", [dev.Id]), CancellationToken.None);

        var deleteResult = await _teamService.DeleteTeamAsync(created.Id, CancellationToken.None);
        var fetchAfterDelete = await _teamService.GetTeamByIdAsync(created.Id, CancellationToken.None);
        var nonExistentDelete = await _teamService.DeleteTeamAsync(99999, CancellationToken.None);

        Assert.True(deleteResult);
        Assert.Null(fetchAfterDelete);
        Assert.False(nonExistentDelete);
    }

    [Fact]
    public async Task GetUnassignedDevelopers_DiscoversOnlyUnassignedDevelopers_AndUpdatesOnAssignmentChanges()
    {
        var dev1 = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        var dev2 = new Developer { AdoId = "dev-2", Name = "Bob Jones", Email = "bob@example.com" };
        var dev3 = new Developer { AdoId = "dev-3", Name = "Charlie Brown", Email = "charlie@example.com" };
        var dev4 = new Developer { AdoId = "dev-4", Name = "Diana Prince", Email = "diana@example.com" };
        _dbContext.Developers.AddRange(dev1, dev2, dev3, dev4);
        await _dbContext.SaveChangesAsync();

        // 1. Initially all 4 developers are unassigned
        var initialUnassigned = await _teamService.GetUnassignedDevelopersAsync(CancellationToken.None);
        Assert.Equal(4, initialUnassigned.Count);

        // 2. Assign dev1 and dev2 to Team Alpha
        var teamAlpha = await _teamService.CreateTeamAsync(new CreateTeamRequest("Team Alpha", null, [dev1.Id, dev2.Id]), CancellationToken.None);
        var afterAlpha = await _teamService.GetUnassignedDevelopersAsync(CancellationToken.None);
        Assert.Equal(2, afterAlpha.Count);

        var afterAlphaNames = afterAlpha.Select(developer => developer.Name).ToList();
        Assert.Contains("Charlie Brown", afterAlphaNames);
        Assert.Contains("Diana Prince", afterAlphaNames);
        Assert.DoesNotContain("Alice Smith", afterAlphaNames);
        Assert.DoesNotContain("Bob Jones", afterAlphaNames);

        // 3. Assign dev3 to Team Beta
        var teamBeta = await _teamService.CreateTeamAsync(new CreateTeamRequest("Team Beta", null, [dev3.Id]), CancellationToken.None);
        var afterBeta = await _teamService.GetUnassignedDevelopersAsync(CancellationToken.None);
        var singleUnassigned = Assert.Single(afterBeta);
        Assert.Equal("Diana Prince", singleUnassigned.Name);

        // 4. Update Team Alpha to remove dev2
        await _teamService.UpdateTeamAsync(teamAlpha.Id, new UpdateTeamRequest("Team Alpha", null, [dev1.Id]), CancellationToken.None);
        var afterUpdate = await _teamService.GetUnassignedDevelopersAsync(CancellationToken.None);
        Assert.Equal(2, afterUpdate.Count);

        var afterUpdateNames = afterUpdate.Select(developer => developer.Name).ToList();
        Assert.Contains("Bob Jones", afterUpdateNames);
        Assert.Contains("Diana Prince", afterUpdateNames);

        // 5. Delete Team Beta (which unassigns dev3)
        await _teamService.DeleteTeamAsync(teamBeta.Id, CancellationToken.None);
        var afterDelete = await _teamService.GetUnassignedDevelopersAsync(CancellationToken.None);
        Assert.Equal(3, afterDelete.Count);

        var afterDeleteNames = afterDelete.Select(developer => developer.Name).ToList();
        Assert.Contains("Bob Jones", afterDeleteNames);
        Assert.Contains("Charlie Brown", afterDeleteNames);
        Assert.Contains("Diana Prince", afterDeleteNames);
        Assert.DoesNotContain("Alice Smith", afterDeleteNames);
    }

    [Fact]
    public async Task GetWorkloads_FilteredByTeam_CalculatesWorkloadsForTeamMembersOnly()
    {
        var dev1 = new Developer { AdoId = "dev-1", Name = "Alice Smith", Email = "alice@example.com" };
        var dev2 = new Developer { AdoId = "dev-2", Name = "Bob Jones", Email = "bob@example.com" };
        var dev3 = new Developer { AdoId = "dev-3", Name = "Charlie Brown", Email = "charlie@example.com" };
        _dbContext.Developers.AddRange(dev1, dev2, dev3);

        var project = new Project { AdoId = "proj-1", Name = "Core API" };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var iteration = new Iteration
                        {
                            AdoId        = "iter-1"
                          , Name         = "Sprint 10"
                          , ProjectAdoId = "proj-1"
                          , StartDate    = today.AddDays(-2)
                          , EndDate      = today.AddDays(12)
                        };
        _dbContext.Iterations.Add(iteration);
        await _dbContext.SaveChangesAsync();

        var cap1 = new Capacity { DeveloperId = dev1.Id, IterationId = iteration.Id, AvailableHours = 40.0, TimeOffHours = 0.0 };
        var cap2 = new Capacity { DeveloperId = dev2.Id, IterationId = iteration.Id, AvailableHours = 40.0, TimeOffHours = 40.0 };
        _dbContext.Capacities.AddRange(cap1, cap2);

        var workItem1 = new WorkItem
                        {
                            AdoId                 = "wi-1"
                          , Title                 = "Build Controller"
                          , Type                  = "Task"
                          , State                 = "Active"
                          , RemainingHours        = 32.0
                          , ProjectId             = project.Id
                          , AssignedToDeveloperId = dev1.Id
                          , IterationId           = iteration.Id
                        };

        var workItem2 = new WorkItem
                        {
                            AdoId                 = "wi-2"
                          , Title                 = "Fix UI Bug"
                          , Type                  = "Task"
                          , State                 = "Active"
                          , RemainingHours        = 30.0
                          , ProjectId             = project.Id
                          , AssignedToDeveloperId = dev2.Id
                          , IterationId           = iteration.Id
                        };

        _dbContext.WorkItems.AddRange(workItem1, workItem2);
        await _dbContext.SaveChangesAsync();

        var teamAlpha = await _teamService.CreateTeamAsync(new CreateTeamRequest("Team Alpha", null, [dev1.Id, dev2.Id]), CancellationToken.None);
        var teamBeta  = await _teamService.CreateTeamAsync(new CreateTeamRequest("Team Beta", null, [dev3.Id]), CancellationToken.None);

        var allWorkloads = await _dashboardService.GetWorkloadsAsync(CancellationToken.None);

        // Perform team filtering: Filter workloads for Team Alpha members
        var alphaMemberIds = teamAlpha.Members.Select(member => member.DeveloperId).ToHashSet();
        var alphaWorkloads = allWorkloads.Where(workload => alphaMemberIds.Contains(workload.DeveloperId)).ToList();

        Assert.Equal(2, alphaWorkloads.Count);
        var dev1Workload = Assert.Single(alphaWorkloads, workload => workload.DeveloperId == dev1.Id);
        Assert.Equal(40.0, dev1Workload.CapacityHours);
        Assert.Equal(32.0, dev1Workload.AssignedHours);
        Assert.Equal(WorkloadStatus.Balanced, dev1Workload.Status);

        var dev2Workload = Assert.Single(alphaWorkloads, workload => workload.DeveloperId == dev2.Id);
        Assert.Equal(0.0, dev2Workload.CapacityHours);
        Assert.Equal(30.0, dev2Workload.AssignedHours);
        Assert.Equal(WorkloadStatus.Overloaded, dev2Workload.Status);

        // Perform team filtering for Team Beta members
        var betaMemberIds = teamBeta.Members.Select(member => member.DeveloperId).ToHashSet();
        var betaWorkloads = allWorkloads.Where(workload => betaMemberIds.Contains(workload.DeveloperId)).ToList();
        Assert.Single(betaWorkloads, workload => workload.DeveloperId == dev3.Id);
    }

    [Fact]
    public async Task GetWorkloads_TeamWithNoMembers_ReturnsEmptyWorkloadSet()
    {
        var emptyTeam = await _teamService.CreateTeamAsync(new CreateTeamRequest("Empty Team", "No devs assigned", []), CancellationToken.None);
        var allWorkloads = await _dashboardService.GetWorkloadsAsync(CancellationToken.None);

        var emptyMemberIds = emptyTeam.Members.Select(member => member.DeveloperId).ToHashSet();
        var filteredWorkloads = allWorkloads.Where(workload => emptyMemberIds.Contains(workload.DeveloperId)).ToList();

        Assert.Empty(filteredWorkloads);
    }

    [Fact]
    public async Task GetWorkloads_TeamMemberWithNoWorkItems_ReturnsAvailableStatus()
    {
        var dev = new Developer { AdoId = "dev-idle", Name = "Idle Developer", Email = "idle@example.com" };
        _dbContext.Developers.Add(dev);
        await _dbContext.SaveChangesAsync();

        var team = await _teamService.CreateTeamAsync(new CreateTeamRequest("Idle Team", null, [dev.Id]), CancellationToken.None);
        var allWorkloads = await _dashboardService.GetWorkloadsAsync(CancellationToken.None);

        var memberIds = team.Members.Select(member => member.DeveloperId).ToHashSet();
        var teamWorkloads = allWorkloads.Where(workload => memberIds.Contains(workload.DeveloperId)).ToList();

        var idleWorkload = Assert.Single(teamWorkloads);
        Assert.Equal("Idle Developer", idleWorkload.Name);
        Assert.Equal(0.0, idleWorkload.AssignedHours);
        Assert.Equal(WorkloadStatus.Available, idleWorkload.Status);
    }
}

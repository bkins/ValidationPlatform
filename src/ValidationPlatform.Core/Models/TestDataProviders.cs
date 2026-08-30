namespace ValidationPlatform.Core.Models;

public static class TestDataProviders
{
    public static class Users
    {
        public static readonly TestUser Administrator = new()
                                                        {
                                                            Id       = "user-admin-01"
                                                          , Username = "admin"
                                                          , Role     = "Administrator"
                                                        };

        public static readonly TestUser StandardUser = new()
                                                       {
                                                           Id       = "user-std-01"
                                                         , Username = "ben"
                                                         , Role     = "User"
                                                       };
    }

    public static class Prompts
    {
        public const string Greeting               = "Hello Assistant";
        public const string AddTask                = "Add a task to review smoke tests";
        public const string DeleteTaskDestructive  = "delete task #999";
        public const string QueryMemory            = "What did we talk about earlier?";
        public const string ReportBug              = "Bug: UI element not responding";
        public const string SwitchWorkspace        = "/ws Work";
    }

    public static class Tasks
    {
        public static readonly TestTaskItem DefaultTask = new()
                                                          {
                                                              Id        = "task-smoke-01"
                                                            , Title     = "Verify UI Automation"
                                                            , DueDate   = DateTime.UtcNow.AddDays(1)
                                                            , Completed = false
                                                          };
    }
}

public class TestUser
{
    public string Id       { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role     { get; set; } = "User";
}

public class TestTaskItem
{
    public string   Id        { get; set; } = string.Empty;
    public string   Title     { get; set; } = string.Empty;
    public DateTime DueDate   { get; set; }
    public bool     Completed { get; set; }
}

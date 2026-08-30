namespace ValidationPlatform.Core.Models;

public record ElementQuery
{
    public string? AutomationId { get; init; }
    public string? Name { get; init; }
    public string? ClassName { get; init; }
    public TimeSpan? Timeout { get; init; }

    public static ElementQuery ById(string automationId, TimeSpan? timeout = null) => 
        new() { AutomationId = automationId, Timeout = timeout };

    public static ElementQuery ByName(string name, TimeSpan? timeout = null) => 
        new() { Name = name, Timeout = timeout };

    public static ElementQuery ByClassName(string className, TimeSpan? timeout = null) => 
        new() { ClassName = className, Timeout = timeout };

    public static ElementQuery ByNameAndClassName(string name, string className, TimeSpan? timeout = null) => 
        new() { Name = name, ClassName = className, Timeout = timeout };

    public static ElementQuery ByIdAndName(string automationId, string name, TimeSpan? timeout = null) => 
        new() { AutomationId = automationId, Name = name, Timeout = timeout };
}

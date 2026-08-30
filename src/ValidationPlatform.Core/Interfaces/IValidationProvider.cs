namespace ValidationPlatform.Core.Interfaces;

public interface IValidationProvider
{
    string Name { get; }
    Task InitializeAsync();
    Task ShutdownAsync();
}

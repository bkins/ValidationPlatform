namespace ValidationPlatform.Core.Interfaces;

public interface IPerformanceValidationProvider : IValidationProvider
{
    Task StartMonitoringAsync(string scenarioName);
    Task<PerformanceMetrics> StopMonitoringAsync();
}

public record PerformanceMetrics(
    TimeSpan ElapsedTime,
    long PeakMemoryBytes,
    double AverageCpuUsagePercentage
);

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Concurrency;

namespace OrleansNet7UrlShortener.HealthChecks;

public class GrainHealthCheck : IHealthCheck
{
    private readonly IClusterClient _clusterClient;

    public GrainHealthCheck(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _clusterClient.GetGrain<ILocalHealthCheckGrain>(0).PingAsync();
        }
        catch (Exception error)
        {
            return HealthCheckResult.Unhealthy("Grain health check failed", error);
        }
        return HealthCheckResult.Healthy();
    }
}

#region Ping Grain in Health Check

[StatelessWorker(1)]
public class LocalHealthCheckGrain : Grain, ILocalHealthCheckGrain
{
    public Task PingAsync() => Task.CompletedTask;
}

public interface ILocalHealthCheckGrain : IGrainWithIntegerKey
{
    Task PingAsync();
}

#endregion


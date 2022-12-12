using Orleans.TestingHost;

namespace OrleansNet7UrlShortener.Grains.Tests;

public class SiloBuilder : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryGrainStorage("url-store");
    }
}
using Orleans.TestingHost;

namespace OrleansNet7UrlShortener.Grains.Tests;

public class SiloClusterFixture
{
    public TestCluster SiloCluster { get; }

    public SiloClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloBuilder>();
        SiloCluster = builder.Build();
        SiloCluster.Deploy();
    }

    public void Dispose()
    {
        SiloCluster.StopAllSilos();
    }
}


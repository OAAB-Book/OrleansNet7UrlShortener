using Orleans.TestingHost;
using System;

namespace OrleansNet7UrlShortener.Grains.Tests
{
    [Collection(nameof(SiloClusterCollection))]
    public class UrlStoreGrainTest
    {
        private readonly TestCluster _cluster;

        public UrlStoreGrainTest(SiloClusterFixture fixture)
        {
            _cluster = fixture.SiloCluster;
        }


        [Fact]
        public async Task TestUrlStoreGrain_NormalRpc()
        {
            // Arrange
            const string grainKey = "a_token";
            const string expectedUrl = "https://www.google.com";

            // Act
            var urlStoreGrain = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainKey);
            await urlStoreGrain.SetUrl(grainKey, expectedUrl);

            var targetGrain = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainKey);
            var targetUrl = await targetGrain.GetUrl();

            // Assert
            Assert.Equal(expectedUrl, targetUrl);
        }

        [Fact]
        public async Task TestUrlStoreGrain_Handle_Sanitized_Url()
        {
            // Arrange
            const string grainAKey = "a_token";
            const string grainBKey = "b_token";
            const string grainCKey = "c_token";
            const string grainDKey = "d_token";

            // Act
            var urlStoreGrainA = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainAKey);
            await urlStoreGrainA.SetUrl(grainAKey, "http/www.google.com");
            var urlA = await urlStoreGrainA.GetUrl();

            var urlStoreGrainB = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainBKey);
            await urlStoreGrainB.SetUrl(grainAKey, "https/www.google.com");
            var urlB = await urlStoreGrainB.GetUrl();

            var urlStoreGrainC = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainCKey);
            await urlStoreGrainC.SetUrl(grainAKey, "http:/www.google.com");
            var urlC = await urlStoreGrainC.GetUrl();

            var urlStoreGrainD = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainDKey);
            await urlStoreGrainD.SetUrl(grainAKey, "https:/www.google.com");
            var urlD = await urlStoreGrainD.GetUrl();

            // Assert
            Assert.Equal(@"http://www.google.com", urlA);
            Assert.Equal(@"https://www.google.com", urlB);
            Assert.Equal(@"http://www.google.com", urlC);
            Assert.Equal(@"https://www.google.com", urlD);
        }

        [Fact]
        public async Task TestUrlStoreGrain_NotCallSetUrlFirst_ThrowException()
        {
            // Arrange
            const string grainKey = "not_init_grain_token";

            // Act
            var urlStoreGrain = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainKey);
            var expectedUrl = string.Empty;

            async Task CallGetUrlAction()
            {
                expectedUrl = await urlStoreGrain!.GetUrl();
            }

            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(CallGetUrlAction);

            // Assert
            Assert.Equal(expectedUrl, string.Empty);
            Assert.Equal($"Url key not exist: {grainKey}", exception.Message);
        }

        [Fact]
        public async Task TestUrlStoreGrain_EmptyOrNullOrInvalidUrl_ThrowException()
        {
            // Arrange
            const string grainKey01 = "empty_url_token";
            const string grainKey02 = "null_url_token";
            const string grainKey03 = "whitespace_url_token";


            // Act
            var emptyUrlKeyGrain = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainKey01);
            var exception01 = await Assert.ThrowsAsync<ArgumentException>(() => emptyUrlKeyGrain.SetUrl(grainKey01, string.Empty));

            var nullUrlKeyGrain = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainKey02);
            var exception02 = await Assert.ThrowsAsync<ArgumentException>(() => nullUrlKeyGrain.SetUrl(grainKey02, null!));

            var invalidUrlKeyGrain = _cluster.GrainFactory.GetGrain<IUrlStoreGrain>(grainKey03);
            var exception03 = await Assert.ThrowsAsync<ArgumentException>(() => invalidUrlKeyGrain.SetUrl(grainKey02, "   "));

            // Assert
            Assert.Equal("URL cannot be null or empty (Parameter 'inputUrl')", exception01.Message);
            Assert.Equal("URL cannot be null or empty (Parameter 'inputUrl')", exception02.Message);
            Assert.Equal("URL is invalid (Parameter 'inputUrl')", exception03.Message);
        }
    }
}
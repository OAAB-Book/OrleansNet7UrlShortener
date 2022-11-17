using Orleans.Runtime;

namespace OrleansNet7UrlShortener.Grains;

public interface IUrlStoreGrain : IGrainWithStringKey
{
    Task SetUrl(string shortenedRouteSegment, string fullUrl);
    Task<string> GetUrl();
}
public class UrlStoreGrain : IGrainBase, IUrlStoreGrain
{
    public IGrainContext GrainContext { get; }

    private readonly IPersistentState<KeyValuePair<string, string>> _cache;

    public UrlStoreGrain(IGrainContext grainContext,
        [PersistentState(stateName: "actual-url", storageName: "url-store")] IPersistentState<KeyValuePair<string, string>> cache)
    {
        GrainContext = grainContext;
        _cache = cache;
    }


    public async Task SetUrl(string shortenedRouteSegment, string fullUrl)
    {
        fullUrl = CleanupUrl(fullUrl);
        _cache.State = new KeyValuePair<string, string>(shortenedRouteSegment, fullUrl);
        await _cache.WriteStateAsync();
    }

    private static string CleanupUrl(string inputUrl)
    {
        if (string.IsNullOrEmpty(inputUrl))
        {
            throw new ArgumentException("URL cannot be null or empty", nameof(inputUrl));
        }

        const string httpPrefix = "http://";
        const string httpsPrefix = "https://";
        const string sanitizedPrefixPattern01 = "http/";
        const string sanitizedPrefixPattern02 = "http:/";
        const string sanitizedPrefixPattern03 = "https/";
        const string sanitizedPrefixPattern04 = "https:/";

        return (inputUrl switch
        {
            not null when inputUrl.TrimStart().StartsWith(httpPrefix) ||
                          inputUrl.TrimStart().StartsWith(httpsPrefix) => inputUrl.TrimStart(),

            not null when inputUrl.TrimStart().StartsWith(sanitizedPrefixPattern01) =>
                httpPrefix + inputUrl.TrimStart()[sanitizedPrefixPattern01.Length..],
            not null when inputUrl.TrimStart().StartsWith(sanitizedPrefixPattern02) =>
                httpPrefix + inputUrl.TrimStart()[sanitizedPrefixPattern02.Length..],

            not null when inputUrl.TrimStart().StartsWith(sanitizedPrefixPattern03) =>
                httpsPrefix + inputUrl.TrimStart()[sanitizedPrefixPattern03.Length..],
            not null when inputUrl.TrimStart().StartsWith(sanitizedPrefixPattern04) =>
                httpsPrefix + inputUrl.TrimStart()[sanitizedPrefixPattern04.Length..],

            //Prefix with "http://" if none of the http or https scheme is present
            not null when !string.IsNullOrEmpty(inputUrl.Trim()) => httpPrefix + inputUrl.TrimStart(),

            _ => throw new ArgumentException("URL is invalid", nameof(inputUrl))
        }).TrimEnd();
    }

    public Task<string> GetUrl()
    {
        if (_cache.RecordExists)
        {
            return Task.FromResult(_cache.State.Value);
        }

        throw new KeyNotFoundException("Url key not exist: " + GrainContext.GrainId.Key);
    }
}

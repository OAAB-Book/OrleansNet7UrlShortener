using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orleans.Configuration;
using OrleansNet7UrlShortener.Grains;
using OrleansNet7UrlShortener.HealthChecks;
using OrleansNet7UrlShortener.Options;
using System.Net;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

const string appInsightKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
const string orleansDashboardPath = @"orleansDashboard";

var builder = WebApplication.CreateBuilder(args);

// Create logger for application startup process
using var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddSimpleConsole(i => i.ColorBehavior = LoggerColorBehavior.Disabled);
    loggingBuilder.AddAzureWebAppDiagnostics();
});
var logger = loggerFactory.CreateLogger<Program>();

var isInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")?.Trim().ToLower() == "true";
if (isInContainer)
{
    // To receive SIGTERM signal in container, we need to use the following code
    builder.Host.UseConsoleLifetime();
}

#region Configure Orleans Silo

builder.Host.UseOrleans((hostBuilderContext, siloBuilder) =>
{
    // Azure web app will set these environment variables when it has virtual network integration configured
    // https://learn.microsoft.com/en-us/azure/app-service/reference-app-settings?tabs=kudu%2Cdotnet#networking
    var privateIpStr = Environment.GetEnvironmentVariable("WEBSITE_PRIVATE_IP");
    var privatePort = Environment.GetEnvironmentVariable("WEBSITE_PRIVATE_PORTS")?.Split(',');
    if (IPAddress.TryParse(privateIpStr, out var ipAddress) &&
        privatePort is { Length: >= 2 }
        && int.TryParse(privatePort[0], out var siloPort) && int.TryParse(privatePort[1], out var gatewayPort))
    {
        logger.LogInformation(
            "Using IP address {ipAddress} for silo port {siloPort} and gateway port {gatewayPort}, isInContainer={isInContainer}",
            ipAddress, siloPort, gatewayPort, isInContainer);
        siloBuilder.ConfigureEndpoints(ipAddress, siloPort, gatewayPort, listenOnAnyHostAddress: isInContainer);

        // Determine the proper cluster id for different Azure App Service Deployment Slots
        string clusterId;
        if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_DEPLOYMENT_ID")))
        {
            clusterId = $"cluster-{Environment.GetEnvironmentVariable("WEBSITE_DEPLOYMENT_ID")}";
        }
        else if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
        {
            clusterId = $"cluster-{Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")}";
        }
        else
        {
            clusterId = "cluster-single-slot";
        }

        const string serviceId = "OrleansUrlShortener";
        logger.LogInformation("Using cluster id '{clusterId}' and service id '{serviceId}'", clusterId, serviceId);
        siloBuilder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterId;
            options.ServiceId = serviceId;
        });

        var azureTableClusterOption = new AzureTableClusterOption();
        hostBuilderContext.Configuration.GetSection("AzureTableCluster").Bind(azureTableClusterOption);
        siloBuilder.UseAzureStorageClustering(options =>
        {
            options.TableName = azureTableClusterOption.TableName;
            options.ConfigureTableServiceClient(new Uri(azureTableClusterOption.ServiceUrl),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = azureTableClusterOption.ManagedIdentityClientId
                }));
        });
    }
    else if (hostBuilderContext.HostingEnvironment.IsDevelopment())
    {
        siloBuilder.UseLocalhostClustering();
    }

    var urlStoreGrainOption = new UrlStoreGrainOption();
    builder.Configuration.GetSection("UrlStoreGrain").Bind(urlStoreGrainOption);
    siloBuilder.AddAzureTableGrainStorage(
        name: "url-store",
        configureOptions: options =>
        {
            options.TableName =
                urlStoreGrainOption.TableName; // if not set, default will be "OrleansGrainState" table name
            // use this configuration if you only want to use local http only Azurite Azure Table Storage emulator
            // options.ConfigureTableServiceClient(
            //  "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;
            //  AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;
            //  TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;");
            options.ConfigureTableServiceClient(new Uri(urlStoreGrainOption.ServiceUrl),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = urlStoreGrainOption.ManagedIdentityClientId
                }));
        });

    var appInsightConnectionString = hostBuilderContext.Configuration.GetValue<string>(appInsightKey);
    if (!string.IsNullOrEmpty(appInsightConnectionString))
    {
        siloBuilder.ConfigureLogging(loggingBuilder =>
            loggingBuilder.AddApplicationInsights(
                configuration => { configuration.ConnectionString = appInsightConnectionString; },
                options => { options.FlushOnDispose = true; }));
    }

    // must declare HostSelf false for Orleans Silo Host load DashboardGrain properly on Azure Web App
    siloBuilder.UseDashboard(dashboardOptions => { dashboardOptions.HostSelf = false; });

    // enable distributed tracing for Orleans Silo
    siloBuilder.AddActivityPropagation();
});

#endregion

#region OpenTelemetry & Application Insight Instrumentation setup

builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
{
    metrics.AddAspNetCoreInstrumentation();
    metrics.AddMeter("Microsoft.Orleans");
    metrics.AddMeter("Microsoft.Orleans.Runtime");
    metrics.AddMeter("Microsoft.Orleans.Application");
});

builder.Services.AddOpenTelemetry().WithTracing(tracing =>
{
    tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OrleansUrlShortener", "Orleans NET7 Web App Demo"));
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddSource("Microsoft.Orleans");
    tracing.AddSource("Microsoft.Orleans.Runtime");
    tracing.AddSource("Microsoft.Orleans.Application");
});

var appInsightConnectionString = builder.Configuration.GetValue<string>(appInsightKey);
if (!string.IsNullOrEmpty(appInsightConnectionString))
{
    //we use Application insight's configuration to know if we are running on Azure Web App or locally
    builder.Services.AddApplicationInsightsTelemetry(options => options.ConnectionString = appInsightConnectionString);
    builder.Logging.AddApplicationInsights(config => { config.ConnectionString = appInsightConnectionString; },
        options => { options.FlushOnDispose = true; });
    builder.Logging.AddAzureWebAppDiagnostics();

    builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
    {
        metrics.AddAzureMonitorMetricExporter(options => { options.ConnectionString = appInsightConnectionString; });
    }).StartWithHost();

    builder.Services.AddOpenTelemetry().WithTracing(tracing =>
    {
        tracing.AddAzureMonitorTraceExporter(options => { options.ConnectionString = appInsightConnectionString; });
    }).StartWithHost();
}
else
{
    // Use Local OpenTelemetry Collector for development
    builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
    {
        metrics.AddConsoleExporter(options => { options.Targets = ConsoleExporterOutputTargets.Debug; });
    }).StartWithHost();
    builder.Services.AddOpenTelemetry().WithTracing(tracing =>
    {
        tracing.AddJaegerExporter(options =>
        {
            options.AgentHost = "localhost";
            options.AgentPort = 6831;
        });
    }).StartWithHost();
}

#endregion

#region ASP.NET Core Health Check integration

builder.Services.Configure<SiloDeployOption>(builder.Configuration.GetSection("SiloDeploy"));
// Add ASP.Net Core Check Healthy Service
builder.Services.AddHealthChecks()
    .AddCheck<GrainHealthCheck>("Orleans_GrainHealthCheck")
    .AddCheck<ClusterHealthCheck>("Orleans_ClusterHealthCheck")
    .AddCheck<SiloHealthCheck>("Orleans_SiloHealthCheck");

#endregion

var app = builder.Build();
app.MapHealthChecks("/healthz");

app.UseOrleansDashboard(new OrleansDashboard.DashboardOptions { BasePath = orleansDashboardPath });

app.UseHttpsRedirection();

#region Web Url/API Endpoints

app.MapGet("/", async (HttpContext context) =>
{
    //remove postfix query string that incur by Facebook sharing 
    var baseUrlBuilder = new UriBuilder(new Uri(context.Request.GetDisplayUrl())) { Query = "" };
    var baseUrl = baseUrlBuilder.Uri.ToString();

    await context.Response.WriteAsync(
        "<html lang=\"en\"><head><title>.NET 7 Orleans url shortener</title><meta http-equiv=\"content-language\" content=\"en-us\"/></head>" +
        $"<body>Type <code>\"{baseUrl}shorten/{{your original url}}\"</code> in address bar to get your shorten url.<br/><br/>" +
        $" Orleans Dashboard: <a href=\"{baseUrl}{orleansDashboardPath}\" target=\"_blank\">click here</a></body></html>");
});

app.MapMethods("/shorten/{*path}", new[] { "GET" },
    async (HttpRequest req, IGrainFactory grainFactory, string path) =>
    {
        var shortenedRouteSegment = Nanoid.Nanoid.Generate("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ", 8);
        var urlStoreGrain = grainFactory.GetGrain<IUrlStoreGrain>(shortenedRouteSegment);
        await urlStoreGrain.SetUrl(shortenedRouteSegment, path);
        var resultBuilder = new UriBuilder(req.GetEncodedUrl()) { Path = $"/go/{shortenedRouteSegment}" };

        return Results.Ok(resultBuilder.Uri);
    });

app.MapGet("/go/{shortenUriSegment}", async (string shortenUriSegment, IGrainFactory grainFactory) =>
{
    var urlStoreGrain = grainFactory.GetGrain<IUrlStoreGrain>(shortenUriSegment);
    try
    {
        var url = await urlStoreGrain.GetUrl();
        return Results.Redirect(url);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound("Url not found");
    }
});

#endregion

app.Run();
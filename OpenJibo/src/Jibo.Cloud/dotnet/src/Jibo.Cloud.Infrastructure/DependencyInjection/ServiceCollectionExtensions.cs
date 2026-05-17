using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Audio;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Media;
using Jibo.Cloud.Infrastructure.News;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Cloud.Infrastructure.Telemetry;
using Jibo.Cloud.Infrastructure.Weather;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenJiboCloud(this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var sttOptions = new BufferedAudioSttOptions();
        if (configuration is not null)
        {
            services.Configure<WebSocketTelemetryOptions>(configuration.GetSection("OpenJibo:Telemetry"));
            services.Configure<ProtocolTelemetryOptions>(configuration.GetSection("OpenJibo:ProtocolTelemetry"));
            services.Configure<TurnTelemetryOptions>(configuration.GetSection("OpenJibo:TurnTelemetry"));
            configuration.GetSection("OpenJibo:Stt").Bind(sttOptions);
        }

        var openWeatherOptions = new OpenWeatherOptions();
        if (configuration is not null)
            configuration.GetSection("OpenJibo:Weather:OpenWeather").Bind(openWeatherOptions);

        if (string.IsNullOrWhiteSpace(openWeatherOptions.ApiKey))
            openWeatherOptions.ApiKey = Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY");

        var newsApiOptions = new NewsApiOptions();
        if (configuration is not null) configuration.GetSection("OpenJibo:News:NewsApi").Bind(newsApiOptions);

        if (string.IsNullOrWhiteSpace(newsApiOptions.ApiKey))
            newsApiOptions.ApiKey = Environment.GetEnvironmentVariable("NEWSAPI_KEY");

        services.AddSingleton(sttOptions);
        services.AddSingleton(openWeatherOptions);
        services.AddSingleton(newsApiOptions);
        services.AddHttpClient<IWeatherReportProvider, OpenWeatherReportProvider>();
        services.AddHttpClient<INewsBriefingProvider, NewsApiBriefingProvider>();
        var statePersistencePath = configuration?["OpenJibo:State:PersistencePath"]
                                   ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "cloud-state.json");
        var personalMemoryPersistencePath = configuration?["OpenJibo:PersonalMemory:PersistencePath"]
                                            ?? Path.Combine(AppContext.BaseDirectory, "App_Data",
                                                "personal-memory.json");
        var stateBackendKind = ParseBackendKind(configuration?["OpenJibo:State:Backend"]);
        var personalMemoryBackendKind = ParseBackendKind(configuration?["OpenJibo:PersonalMemory:Backend"]);
        var stateConnectionString = configuration?["OpenJibo:State:ConnectionString"]
                                    ?? Environment.GetEnvironmentVariable("OPENJIBO_STATE_STORAGE_CONNECTION_STRING")
                                    ?? Environment.GetEnvironmentVariable("OPENJIBO_STATE_SQL_CONNECTION_STRING");
        var personalMemoryConnectionString = configuration?["OpenJibo:PersonalMemory:ConnectionString"]
                                             ?? Environment.GetEnvironmentVariable(
                                                 "OPENJIBO_PERSONAL_MEMORY_STORAGE_CONNECTION_STRING")
                                             ?? Environment.GetEnvironmentVariable(
                                                 "OPENJIBO_PERSONAL_MEMORY_SQL_CONNECTION_STRING");
        var mediaOptions = new MediaContentStoreOptions();
        if (configuration is not null)
            configuration.GetSection("OpenJibo:Media").Bind(mediaOptions);

        if (string.IsNullOrWhiteSpace(mediaOptions.ConnectionString))
            mediaOptions.ConnectionString = Environment.GetEnvironmentVariable("OPENJIBO_MEDIA_STORAGE_CONNECTION_STRING");

        services.AddSingleton<IPersistenceSnapshotStoreFactory, PersistenceSnapshotStoreFactory>();
        services.AddSingleton<IMediaContentStoreFactory, MediaContentStoreFactory>();
        services.AddSingleton<ICloudStateStore>(provider =>
        {
            var snapshotFactory = provider.GetRequiredService<IPersistenceSnapshotStoreFactory>();
            return new InMemoryCloudStateStore(snapshotFactory.Create(statePersistencePath, stateBackendKind,
                "cloud-state", stateConnectionString));
        });
        services.AddSingleton<IPersonalMemoryStore>(provider =>
        {
            var snapshotFactory = provider.GetRequiredService<IPersistenceSnapshotStoreFactory>();
            return new InMemoryPersonalMemoryStore(snapshotFactory.Create(personalMemoryPersistencePath,
                personalMemoryBackendKind, "personal-memory", personalMemoryConnectionString));
        });
        services.AddSingleton<IJiboExperienceContentRepository, InMemoryJiboExperienceContentRepository>();
        services.AddSingleton<JiboExperienceContentCache>();
        services.AddSingleton<IJiboRandomizer, DefaultJiboRandomizer>();
        services.AddSingleton<JiboInteractionService>();
        services.AddSingleton<IConversationBroker, DemoConversationBroker>();
        services.AddSingleton<IExternalProcessRunner, ExternalProcessRunner>();
        services.AddSingleton<ISttStrategy, SyntheticBufferedAudioSttStrategy>();
        services.AddSingleton<ISttStrategy, LocalWhisperCppBufferedAudioSttStrategy>();
        services.AddSingleton<ISttStrategySelector, DefaultSttStrategySelector>();
        services.AddSingleton<IWebSocketTelemetrySink, FileWebSocketTelemetrySink>();
        services.AddSingleton<IProtocolTelemetrySink, FileProtocolTelemetrySink>();
        services.AddSingleton<ITurnTelemetrySink, FileTurnTelemetrySink>();
        services.AddSingleton<IMediaContentStore>(provider =>
        {
            var factory = provider.GetRequiredService<IMediaContentStoreFactory>();
            return factory.Create(mediaOptions.DirectoryPath, mediaOptions.Backend, mediaOptions.ContainerName,
                mediaOptions.ConnectionString);
        });
        services.AddSingleton<ProtocolToTurnContextMapper>();
        services.AddSingleton<ResponsePlanToSocketMessagesMapper>();
        services.AddSingleton<WebSocketTurnFinalizationService>();
        services.AddSingleton<JiboCloudProtocolService>();
        services.AddSingleton<JiboWebSocketService>();

        return services;
    }

    private static PersistenceBackendKind ParseBackendKind(string? value)
    {
        return Enum.TryParse<PersistenceBackendKind>(value, true, out var backendKind)
            ? backendKind
            : PersistenceBackendKind.File;
    }
}

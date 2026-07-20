using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Audio;
using Jibo.Cloud.Infrastructure.Calendar;
using Jibo.Cloud.Infrastructure.Commute;
using Jibo.Cloud.Infrastructure.Conversions;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Dictionary;
using Jibo.Cloud.Infrastructure.FunFacts;
using Jibo.Cloud.Infrastructure.Holidays;
using Jibo.Cloud.Infrastructure.Media;
using Jibo.Cloud.Infrastructure.News;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Cloud.Infrastructure.Search;
using Jibo.Cloud.Infrastructure.Telemetry;
using Jibo.Cloud.Infrastructure.Weather;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// ReSharper disable UnusedMethodReturnValue.Global

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

        BufferedAudioSttPathResolver.ValidateResolvedDependencies(sttOptions);

        var openWeatherOptions = new OpenWeatherOptions();
        configuration?.GetSection("OpenJibo:Weather:OpenWeather").Bind(openWeatherOptions);

        if (string.IsNullOrWhiteSpace(openWeatherOptions.ApiKey))
            openWeatherOptions.ApiKey = Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY");

        var newsApiOptions = new NewsApiOptions();
        configuration?.GetSection("OpenJibo:News:NewsApi").Bind(newsApiOptions);

        if (string.IsNullOrWhiteSpace(newsApiOptions.ApiKey))
            newsApiOptions.ApiKey = Environment.GetEnvironmentVariable("NEWSAPI_KEY");

        var holidayOptions = new HolidayCalendarOptions();
        configuration?.GetSection("OpenJibo:Holiday").Bind(holidayOptions);

        var uselessFactsOptions = new UselessFactsOptions();
        configuration?.GetSection("OpenJibo:FunFacts:UselessFacts").Bind(uselessFactsOptions);

        var freeDictionaryApiOptions = new FreeDictionaryApiOptions();
        configuration?.GetSection("OpenJibo:Dictionary:FreeDictionaryApi").Bind(freeDictionaryApiOptions);

        var searchSection = configuration?.GetSection("OpenJibo:Search");
        var llmInstructions = SearchInstructionsResolver.Resolve(
            Environment.GetEnvironmentVariable("OPENJIBO_SEARCH_INSTRUCTIONS")
            ?? searchSection?["Instructions"],
            Environment.GetEnvironmentVariable("OPENJIBO_SEARCH_INSTRUCTIONS_FILE")
            ?? searchSection?["InstructionsFile"]);
        var searchBackendOptions = SearchBackendOptions.Create(
            Environment.GetEnvironmentVariable("OPENJIBO_SEARCH_BACKEND")
            ?? searchSection?["Primary"]
            ?? searchSection?["Backend"],
            Environment.GetEnvironmentVariable("OPENJIBO_SEARCH_FALLBACK")
            ?? searchSection?["Fallback"]
            ?? searchSection?["FallbackBackend"],
            searchSection?.GetValue("CacheTtlSeconds", 300) ?? 300,
            searchSection?.GetValue("FailureCacheTtlSeconds", 45) ?? 45,
            llmInstructions);

        services.AddSingleton(sttOptions);
        services.AddSingleton(openWeatherOptions);
        services.AddSingleton(newsApiOptions);
        services.AddSingleton(holidayOptions);
        services.AddSingleton(uselessFactsOptions);
        services.AddSingleton(freeDictionaryApiOptions);
        services.AddSingleton(searchBackendOptions);
        services.AddHttpClient<IWeatherReportProvider, OpenWeatherReportProvider>();
        services.AddHttpClient<INewsBriefingProvider, NewsApiBriefingProvider>();
        services.AddHttpClient<IFunFactProvider, UselessFactsFunFactProvider>();
        services.AddHttpClient<IWordDefinitionProvider, FreeDictionaryApiDefinitionProvider>();
        services.AddHttpClient<WolframAlphaSearchProvider>();
        services.AddHttpClient<OllamaSearchProvider>();
        services.AddHttpClient<ChatGptSearchProvider>();
        services.AddSingleton<IKnowledgeSearchProvider, WolframAlphaSearchProvider>();
        services.AddSingleton<IKnowledgeSearchProvider, OllamaSearchProvider>();
        services.AddSingleton<IKnowledgeSearchProvider, ChatGptSearchProvider>();
        services.AddSingleton<IKnowledgeSearchService, KnowledgeSearchService>();
        services.AddSingleton<IHolidayCalendarProvider>(provider =>
            new NagerDateHolidayCalendarProvider(provider.GetRequiredService<HolidayCalendarOptions>()));
        services.AddSingleton<IHolidayCountdownCatalog>(_ =>
        {
            var catalogPath = Path.Combine(AppContext.BaseDirectory, "Content", "HolidayCountdownCatalog.json");
            return new HolidayCountdownCatalogLoader().LoadFromFile(catalogPath);
        });
        services.AddSingleton<IMeasurementConversionCatalog>(_ =>
        {
            var catalogPath = Path.Combine(AppContext.BaseDirectory, "Content", "MeasurementConversionCatalog.json");
            return new MeasurementConversionCatalogLoader().LoadFromFile(catalogPath);
        });
        services.AddHttpClient<IIcalFeedFetcher, IcalFeedFetcher>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        services.AddSingleton<IcalCalendarFeedInspector>();
        services.AddSingleton<CloudStateCalendarReportProvider>();
        services.AddSingleton<ICalendarReportProvider>(provider =>
            new IcalCalendarReportProvider(
                provider.GetRequiredService<IUserIntegrationStore>(),
                provider.GetRequiredService<ICloudStateStore>(),
                provider.GetRequiredService<IIcalFeedFetcher>(),
                provider.GetRequiredService<CloudStateCalendarReportProvider>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IcalCalendarReportProvider>>()));
        services.AddSingleton<ICommuteReportProvider>(provider =>
            new CloudStateCommuteReportProvider(provider.GetRequiredService<ICloudStateStore>()));
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
        var ownerFirstName = configuration?["OpenJibo:OwnerFirstName"];
        var ownerLastName = configuration?["OpenJibo:OwnerLastName"];
        switch (stateBackendKind)
        {
            case PersistenceBackendKind.Sqlite when string.IsNullOrWhiteSpace(stateConnectionString):
            {
                var dbPath = Path.ChangeExtension(statePersistencePath, ".db");
                stateConnectionString = $"Data Source={dbPath}";
                break;
            }
            case PersistenceBackendKind.PostgreSql when string.IsNullOrWhiteSpace(stateConnectionString):
                stateConnectionString = BuildPostgreSqlConnectionString("openjibo_state");
                break;
        }

        switch (personalMemoryBackendKind)
        {
            case PersistenceBackendKind.Sqlite when
                string.IsNullOrWhiteSpace(personalMemoryConnectionString):
            {
                var dbPath = Path.ChangeExtension(personalMemoryPersistencePath, ".db");
                personalMemoryConnectionString = $"Data Source={dbPath}";
                break;
            }
            case PersistenceBackendKind.PostgreSql when
                string.IsNullOrWhiteSpace(personalMemoryConnectionString):
                personalMemoryConnectionString = BuildPostgreSqlConnectionString("openjibo_memory");
                break;
        }

        var mediaOptions = new MediaContentStoreOptions();
        configuration?.GetSection("OpenJibo:Media").Bind(mediaOptions);

        if (string.IsNullOrWhiteSpace(mediaOptions.ConnectionString))
            mediaOptions.ConnectionString =
                Environment.GetEnvironmentVariable("OPENJIBO_MEDIA_STORAGE_CONNECTION_STRING");

        services.AddSingleton<IPersistenceSnapshotStoreFactory, PersistenceSnapshotStoreFactory>();
        services.AddSingleton<IMediaContentStoreFactory, MediaContentStoreFactory>();
        var userIntegrationsPersistencePath = configuration?["OpenJibo:UserIntegrations:PersistencePath"]
                                              ?? Path.Combine(AppContext.BaseDirectory, "App_Data",
                                                  "user-integrations.json");
        services.AddSingleton<UserDataEncryptionService>();
        services.AddSingleton(provider =>
            new EncryptedUserDataSnapshotStore(
                userIntegrationsPersistencePath,
                provider.GetRequiredService<UserDataEncryptionService>()));
        services.AddSingleton<IUserIntegrationStore>(provider =>
            new InMemoryUserIntegrationStore(provider.GetRequiredService<EncryptedUserDataSnapshotStore>()));
        services.AddSingleton<ICloudStateStore>(provider =>
        {
            var snapshotFactory = provider.GetRequiredService<IPersistenceSnapshotStoreFactory>();
            var holidayCalendarProvider = provider.GetRequiredService<IHolidayCalendarProvider>();
            return new InMemoryCloudStateStore(
                snapshotFactory.Create(statePersistencePath, stateBackendKind, "cloud-state", stateConnectionString),
                holidayCalendarProvider,
                ownerFirstName,
                ownerLastName);
        });
        services.AddSingleton<ICloudAuthProtocolHandler, CloudAuthProtocolHandler>();
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
        services.AddHttpClient<AzureSpeechBufferedAudioSttStrategy>();
        services.AddSingleton<ISttStrategy>(provider =>
            provider.GetRequiredService<AzureSpeechBufferedAudioSttStrategy>());
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
        services.AddSingleton<JiboVerificationService>();
        services.AddSingleton<PortalSessionService>();
        services.AddSingleton<HomeAssistantConnectionRegistry>();
        services.AddSingleton<RobotPendingNotificationStore>();
        services.AddSingleton<RobotNotificationRegistry>();
        services.AddSingleton<RobotPresenceRegistry>();
        services.AddSingleton(provider => new OpenJiboServerIdentity(
            configuration?["OpenJibo:CanonicalApiHostname"]));
        services.AddSingleton<FleetNetworkPresenceRegistry>();
        services.AddSingleton<LoopUpdatedPushService>();
        services.AddSingleton<HomeAssistantCommandService>();

        return services;
    }

    private static PersistenceBackendKind ParseBackendKind(string? value)
    {
        return Enum.TryParse<PersistenceBackendKind>(value, true, out var backendKind)
            ? backendKind
            : PersistenceBackendKind.Sqlite;
    }

    private static string BuildPostgreSqlConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_HOST") ?? "postgres",
            Port = int.TryParse(Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_PORT"), out var port)
                ? port
                : 5432,
            Database = databaseName,
            Username = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_USER") ?? "openjibo"
        };

        var password = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_PASSWORD");
        if (!string.IsNullOrWhiteSpace(password))
            builder.Password = password;

        return builder.ConnectionString;
    }
}

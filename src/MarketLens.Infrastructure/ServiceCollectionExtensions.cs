using System.Net;
using System.Net.Http.Headers;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using MarketLens.Infrastructure.Sidecars;
using MarketLens.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMarketLensInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<MarketLensDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                o => o.UseVector()));

        services.Configure<EmbeddingOptions>(configuration.GetSection("Embedding"));
        services.Configure<TriageOptions>(configuration.GetSection("Triage"));
        services.Configure<WhisperOptions>(configuration.GetSection("Whisper"));
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaConcurrencyGate(o.MaxConcurrency, o.MinIntervalSeconds);
        });
        services.Configure<FinnhubOptions>(configuration.GetSection("Finnhub"));
        services.Configure<FredOptions>(configuration.GetSection("Fred"));
        services.Configure<ClusterOptions>(configuration.GetSection("Cluster"));

        services.AddHttpClient<IEmbeddingClient, EmbeddingClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient<ITriageClient, TriageClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient<WhisperClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(3600);
        });

        services.AddHttpClient<IEventExtractor, OllamaEventExtractor>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(300);
        });

        services.AddHttpClient<IStanceClassifier, OllamaStanceClassifier>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddHttpClient<IThesisPlanner, OllamaThesisPlanner>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(300);
        });

        services.AddHttpClient<IIdeaMemoGenerator, OllamaIdeaMemoGenerator>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(300);
        });

        services.AddHttpClient<IMarketDataClient, FinnhubMarketDataClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<ICompanyFundamentalsSource, FinnhubFundamentalsSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        services.Configure<PolygonOptions>(configuration.GetSection("Polygon"));
        services.Configure<ArticleBodyCacheOptions>(configuration.GetSection("ArticleBodyCache"));

        services.AddHttpClient<IPriceBarSource, YahooPriceBarSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<PolygonPriceBarSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<FinnhubPriceBarSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient<YahooQuoteSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddTransient<IQuoteSource>(sp => sp.GetRequiredService<YahooQuoteSource>());

        services.AddHttpClient("earnings_calendar", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MarketLens/1.0)");
        });

        services.AddHttpClient<FinnhubEarningsCalendarSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IEconomicCalendarSource>(sp => sp.GetRequiredService<FinnhubEarningsCalendarSource>());

        services.AddHttpClient<FredCalendarSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IEconomicCalendarSource>(sp => sp.GetRequiredService<FredCalendarSource>());

        services.AddScoped<ClusterAssigner>();
        services.AddScoped<PipelineRunRecorder>();
        services.AddScoped<ILocalWorkQueue, LocalWorkQueue>();
        services.AddScoped<ISourceCursorStore, SourceCursorStore>();
        services.AddScoped<ILocalFetchCache, LocalFetchCache>();
        services.AddSingleton<ImportanceCalculator>();
        services.AddSingleton<MarketReactionCalculator>();
        services.AddSingleton<IWatchlistProvider, DbWatchlistProvider>();

        var userAgent = configuration["Edgar:UserAgent"]
            ?? throw new InvalidOperationException("Edgar:UserAgent is not configured; EDGAR requires a User-Agent with a real contact email");

        services.AddHttpClient<ArticleBodyEnricher>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });

        services.AddHttpClient("historical_backfill", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });

        services.AddHttpClient("edge_media_server", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
        });

        services.AddHttpClient<EdgarSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<EdgarSource>());

        services.AddHttpClient<CourtListenerSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<CourtListenerSource>());

        services.AddHttpClient<FredSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<FredSource>());

        services.Configure<BlsOptions>(configuration.GetSection("Bls"));
        services.AddHttpClient<BlsSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<BlsSource>());

        services.Configure<CensusRetailSalesOptions>(configuration.GetSection("Census"));
        services.AddHttpClient<CensusRetailSalesSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<CensusRetailSalesSource>());

        services.Configure<EiaOptions>(configuration.GetSection("Eia"));
        services.AddHttpClient<EiaSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<EiaSource>());

        services.AddHttpClient<UsgsSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<UsgsSource>());

        services.AddHttpClient<FinnhubSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<FinnhubSource>());

        var rssConfigs = configuration.GetSection("RssFeeds").Get<List<RssFeedConfig>>() ?? [];
        services.AddHttpClient("rss", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });

        foreach (var cfg in rssConfigs)
        {
            services.AddSingleton<INewsSource>(sp =>
            {
                var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("rss");
                return new RssSource(
                    client,
                    sp.GetRequiredService<IWatchlistProvider>(),
                    sp.GetRequiredService<ILogger<RssSource>>(),
                    cfg);
            });
        }

        services.AddHttpClient<FederalRegisterSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<FederalRegisterSource>());

        var redditConfig = configuration.GetSection("Reddit").Get<RedditSourceConfig>() ?? new RedditSourceConfig
        {
            Subreddits = ["wallstreetbets", "AMD_Stock", "NVDA_Stock", "hardware", "SecurityAnalysis", "investing", "stocks"],
            LookbackDays = 7,
            DelayBetweenSubredditsMs = 1500
        };
        services.AddHttpClient("reddit_source", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });
        services.AddSingleton<INewsSource>(sp =>
            new RedditSource(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("reddit_source"),
                sp.GetRequiredService<IWatchlistProvider>(),
                sp.GetRequiredService<ILogger<RedditSource>>(),
                redditConfig));

        return services;
    }
}

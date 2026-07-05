using MarketLens.Api.Endpoints;
using MarketLens.Api.Endpoints.Research;
using MarketLens.Api.HostedServices;
using MarketLens.Api.Services;
using MarketLens.Api.Services.Ideas;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Infrastructure;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddMarketLensInfrastructure(builder.Configuration);

builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));
builder.Services.Configure<ExtractionOptions>(builder.Configuration.GetSection("Extraction"));
builder.Services.Configure<ResearchMatcherOptions>(builder.Configuration.GetSection("ResearchMatcher"));
builder.Services.Configure<StanceClassificationOptions>(builder.Configuration.GetSection("StanceClassification"));
builder.Services.Configure<ThesisBootstrapOptions>(builder.Configuration.GetSection("ThesisBootstrap"));
builder.Services.Configure<ThesisPlanRefreshOptions>(builder.Configuration.GetSection("ThesisPlanRefresh"));
builder.Services.Configure<ThesisBootstrapWorkOptions>(builder.Configuration.GetSection("ThesisBootstrapWork"));
builder.Services.Configure<MarketSnapshotOptions>(builder.Configuration.GetSection("MarketSnapshots"));
builder.Services.Configure<PriceBarBackfillOptions>(builder.Configuration.GetSection("PriceBarBackfill"));
builder.Services.Configure<EconomicCalendarOptions>(builder.Configuration.GetSection("EconomicCalendar"));
builder.Services.Configure<TranscriptIngestionOptions>(builder.Configuration.GetSection("TranscriptIngestion"));
builder.Services.Configure<EarningsCalendarOptions>(builder.Configuration.GetSection("EarningsCalendar"));
builder.Services.Configure<FilingChunkExtractionOptions>(builder.Configuration.GetSection("FilingChunkExtraction"));
builder.Services.Configure<MarketQuoteOptions>(builder.Configuration.GetSection("MarketQuotes"));
builder.Services.Configure<Form4ProcessingOptions>(builder.Configuration.GetSection("Form4Processing"));
builder.Services.Configure<ResearchSnapshotOptions>(builder.Configuration.GetSection("ResearchSnapshot"));
builder.Services.Configure<IdeaMemoOptions>(builder.Configuration.GetSection("IdeaMemo"));
builder.Services.Configure<ForwardIdeasOptions>(builder.Configuration.GetSection("ForwardIdeas"));
builder.Services.Configure<FundamentalsRefreshOptions>(builder.Configuration.GetSection("FundamentalsRefresh"));
builder.Services.Configure<QuietHoursOptions>(builder.Configuration.GetSection("QuietHours"));
builder.Services.AddSingleton<IQuietHoursPolicy, QuietHoursPolicy>();

builder.Services.AddHostedService<LocalIngestionRecoveryService>();
builder.Services.AddHostedService<NewsIngestionOrchestrator>();
builder.Services.AddHostedService<EventExtractionService>();
builder.Services.AddHostedService<ResearchMatcherService>();
builder.Services.AddHostedService<StanceClassificationService>();
builder.Services.AddHostedService<MarketSnapshotService>();
builder.Services.AddHostedService<PriceBarBackfillService>();
builder.Services.AddHostedService<EconomicCalendarService>();
builder.Services.AddHostedService<ThesisPlanRefreshService>();
builder.Services.AddHostedService<ThesisBootstrapWorkService>();
builder.Services.AddHostedService<TranscriptIngestionService>();
builder.Services.AddHostedService<EarningsCalendarService>();
builder.Services.AddHostedService<ThinArticleBackfillService>();
builder.Services.AddHostedService<HistoricalBackfillService>();
builder.Services.AddHostedService<FilingChunkExtractionService>();
builder.Services.AddHostedService<MarketQuoteService>();
builder.Services.AddHostedService<Form4ProcessingService>();
builder.Services.AddHostedService<ResearchSnapshotService>();
builder.Services.AddHostedService<FundamentalsRefreshService>();
builder.Services.AddHostedService<IdeaMemoRefreshService>();
builder.Services.AddScoped<ResearchMatcher>();
builder.Services.AddScoped<ThesisBootstrapper>();
builder.Services.AddScoped<ThesisKillCriterionEscalator>();
builder.Services.AddScoped<MarketLens.Api.Services.IdeaMemoService>();
builder.Services.AddScoped<MarketLens.Api.Services.CompanyFundamentalsService>();
builder.Services.AddScoped<NewsSourcePollHandler>();
builder.Services.AddScoped<ArticleBodyEnrichmentHandler>();
builder.Services.AddScoped<TranscriptIngestionHandler>();
builder.Services.AddScoped<FilingChunkExtractionHandler>();
builder.Services.AddScoped<Form4ProcessingHandler>();
builder.Services.AddScoped<EventExtractionClusterHandler>();
builder.Services.AddScoped<StanceClassificationHandler>();
builder.Services.AddScoped<ResearchMatchThesisHandler>();
builder.Services.AddScoped<IdeaMemoWorkHandler>();
builder.Services.AddScoped<EconomicCalendarSourceHandler>();
builder.Services.AddScoped<EarningsCalendarTickerHandler>();
builder.Services.AddScoped<FundamentalsRefreshHandler>();
builder.Services.AddScoped<ThesisPlanRefreshHandler>();
builder.Services.AddScoped<ThesisBootstrapWorkHandler>();
builder.Services.AddScoped<ResearchSnapshotHandler>();
builder.Services.AddScoped<MarketQuoteWorkHandler>();
builder.Services.AddScoped<PriceBarBackfillWorkHandler>();
builder.Services.AddScoped<MarketSnapshotWorkHandler>();
builder.Services.AddScoped<IdeaMarketDataLoader>();
builder.Services.AddScoped<IdeaMarketDataService>();
builder.Services.AddScoped<ForwardPipelineCatalog>();
builder.Services.AddScoped<ForwardIdeaScorer>();
builder.Services.AddScoped<ForwardIdeasService>();
builder.Services.AddSingleton<AudioReplayDiscovery>();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5216", "http://localhost:5217"];

builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("web");
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapEventsEndpoints();
app.MapArticlesEndpoints();
app.MapIdeasEndpoints();
app.MapResearchEndpoints();
app.MapChartEndpoints();
app.MapTranscriptEndpoints();
app.MapSourcesEndpoints();
app.MapPipelineEndpoints();
app.MapWeeklyOpenEndpoints();
app.MapMarketOverviewEndpoints();
app.MapAdminEndpoints();
app.MapCatalystEndpoints();
app.MapKillCriteriaEndpoints();
app.MapPositionEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
    await db.Database.MigrateAsync();
    await MarketLens.Infrastructure.Services.AssetRegistrySeed.EnsureCanonicalTickersAsync(db);
}

app.Run();

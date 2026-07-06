using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Config;
using YouTracker.Core.DependencyInjection;
using YouTracker.Infrastructure.Anthropic.DependencyInjection;
using YouTracker.Infrastructure.ClaudeCli.DependencyInjection;
using YouTracker.Infrastructure.Storage.DependencyInjection;
using YouTracker.Infrastructure.YouTrack;
using YouTracker.Infrastructure.YouTrack.DependencyInjection;
using YouTracker.Web;

// Config bootstrap: only the Storage module knows where/how config lives.
var bootstrap = new ServiceCollection().AddYouTrackerStorage();
AppConfig config;
#pragma warning disable ASP0000 // deliberate throwaway provider, same bootstrap as the TUI host
using (var bootstrapProvider = bootstrap.BuildServiceProvider())
#pragma warning restore ASP0000
{
    var configStore = bootstrapProvider.GetService<IConfigStore>();
    if (configStore is null)
    {
        Console.Error.WriteLine("IConfigStore is not registered by the Storage module.");
        return 2;
    }

    if (!configStore.Exists)
    {
        Console.WriteLine($"Config missing. Create {configStore.ConfigPath} with:");
        Console.WriteLine(configStore.Template);
        return 1;
    }

    config = configStore.Load();
}

// The Angular build lands in wwwroot; make sure the folder exists before the builder
// captures the web root (a missing dir would pin a NullFileProvider until restart).
var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
Directory.CreateDirectory(wwwroot);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5210");

builder.Services.AddYouTrackerCore();
builder.Services.AddYouTrackerYouTrack();
builder.Services.AddYouTrackerStorage();

// AI provider: real API key → Anthropic SDK; otherwise the local Claude Code CLI.
if (config.Anthropic.HasApiKey)
    builder.Services.AddYouTrackerAnthropic();
else
    builder.Services.AddYouTrackerClaudeCli();
builder.Services.AddSingleton(config);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()
    )
);

var app = builder.Build();

// Error envelope: every unhandled exception becomes { error: message }.
app.Use(
    async (context, next) =>
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var status = ex switch
            {
                InvalidOperationException => StatusCodes.Status409Conflict,
                YouTrackApiException => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError,
            };
            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    }
);

app.UseCors();

// --- SPA hosting: only active once the Angular build has produced wwwroot/index.html ---
if (File.Exists(Path.Combine(wwwroot, "index.html")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

var api = app.MapGroup("/api");

api.MapGet(
    "/meta",
    (AppConfig cfg) =>
        new
        {
            targetMinutesPerWorkday = cfg.TargetMinutesPerWorkday,
            timezone = cfg.Workday.Timezone,
            webBaseUrl = cfg.YouTrack.WebBaseUrl,
            aiProvider = cfg.Anthropic.HasApiKey ? "anthropic" : "claude-cli",
        }
);

api.MapGet(
    "/issues",
    (IDispatcher dispatcher, CancellationToken ct, bool refresh = false) =>
        dispatcher.QueryAsync(new GetMyOpenIssuesQuery(BypassCache: refresh), ct)
);

api.MapGet(
    "/time/overview",
    (
        IDispatcher dispatcher,
        DateOnly from,
        DateOnly to,
        CancellationToken ct,
        bool refresh = false
    ) => dispatcher.QueryAsync(new GetTimeOverviewQuery(from, to, BypassCache: refresh), ct)
);

api.MapGet(
    "/worktypes",
    (IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.QueryAsync(new GetWorkItemTypesQuery(), ct)
);

// WriteAsJsonAsync so a missing timer serializes as JSON null (not an empty 200/204).
api.MapGet(
    "/timer",
    async (IDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
    {
        var state = await dispatcher.QueryAsync(new GetTimerStateQuery(), ct);
        await http.Response.WriteAsJsonAsync(state, ct);
    }
);

api.MapPost(
    "/timer/start",
    (StartTimerRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new StartTimerCommand(request.IssueId, request.IssueSummary), ct)
);

api.MapPost(
    "/timer/stop",
    async (IDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
    {
        var result = await dispatcher.SendAsync(new StopTimerCommand(), ct);
        await http.Response.WriteAsJsonAsync(result, ct);
    }
);

api.MapPost(
    "/worklog",
    (CreateWorkLogRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(
            new CreateWorkItemCommand(
                request.IssueId,
                request.Date,
                request.Minutes,
                request.TypeId,
                request.Text
            ),
            ct
        )
);

api.MapPost(
    "/worklog/commit",
    (CommitWorkLogRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(
            new CommitWorkLogDraftsCommand(request.Drafts, request.DefaultTypeId),
            ct
        )
);

api.MapPost(
    "/ai/draft",
    (AiDraftRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.QueryAsync(new DraftWorkLogQuery(request.FreeText, request.Date), ct)
);

api.MapPost(
    "/ai/gapfills",
    (PeriodRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.QueryAsync(new SuggestGapFillsQuery(request.From, request.To), ct)
);

api.MapPost(
    "/ai/summary",
    async (PeriodRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        new
        {
            text = await dispatcher.QueryAsync(
                new SummarizePeriodQuery(request.From, request.To),
                ct
            ),
        }
);

api.MapPost(
    "/ai/triage",
    (IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.QueryAsync(new TriageIssuesQuery(), ct)
);

app.Run();
return 0;

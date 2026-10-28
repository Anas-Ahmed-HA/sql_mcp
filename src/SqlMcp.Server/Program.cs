using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Configuration;
using SqlMcp.Server.Data;
using SqlMcp.Server.Metadata;
using SqlMcp.Server.Query;
using SqlMcp.Server.Security;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // When run as a Windows service the working directory is System32,
    // so resolve appsettings.json next to the binary instead.
    ContentRootPath = AppContext.BaseDirectory,
});

// Machine-local settings such as the connection string with its password; gitignored, never committed.
// Inserted just before the (unprefixed) environment variables, so it overrides appsettings*.json and user
// secrets while a server's environment variables and the command line still override it.
var sources = builder.Configuration.Sources;
var environmentIndex = sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
sources.Insert(
    environmentIndex < 0 ? sources.Count : environmentIndex,
    new JsonConfigurationSource { Path = "appsettings.Local.json", Optional = true, ReloadOnChange = true });

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(
        o => o.ConnectionStrings.Count == 1 && !string.IsNullOrWhiteSpace(o.ConnectionString),
        "Phase 0 expects exactly one non-empty entry under Database:ConnectionStrings.")
    .ValidateOnStart();

builder.Services.AddOptions<QueryOptions>()
    .Bind(builder.Configuration.GetSection(QueryOptions.SectionName))
    .Validate(o => o.MaxRows >= 1 && o.DefaultRows >= 1 && o.DefaultRows <= o.MaxRows, "Query: need 1 <= DefaultRows <= MaxRows.")
    .Validate(o => o.MaxCellLength >= 1, "Query:MaxCellLength must be at least 1.")
    .ValidateOnStart();

builder.Services.AddOptions<MetadataOptions>()
    .Bind(builder.Configuration.GetSection(MetadataOptions.SectionName));

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<IDatabaseCatalog, DatabaseCatalog>();
builder.Services.AddSingleton<SelectQueryGuard>();
builder.Services.AddSingleton<SelectQueryExecutor>();
builder.Services.AddSingleton<IAuditLog, JsonFileAuditLog>();
builder.Services.AddSingleton<ICallerContext, HttpCallerContext>();
builder.Services.AddSingleton<IMetadataStore, MetadataStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHostedService<DatabaseStartupCheck>();
builder.Services.AddHealthChecks();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "ai-sql-mcp", Version = "0.3.0" };
        o.ServerInstructions =
            "Read-only access to a SQL Server database. First call get_business_rules for what the data means and " +
            "how views join, then list_objects to see the available tables/views, describe_object for the columns " +
            "of an object, and run_select with a single T-SQL SELECT. Prefer aggregates (COUNT, GROUP BY) over " +
            "fetching raw rows.";
    })
    .WithHttpTransport(o =>
    {
        // No per-client state in memory, so any instance behind a load balancer can serve any request.
        o.SessionMode = HttpServerSessionMode.Stateless;
    })
    // Registers every [McpServerToolType] class in this assembly (see Tools/).
    .WithToolsFromAssembly()
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        try
        {
            return await next(context, cancellationToken);
        }
        catch (McpException ex) when (ex.GetType() == typeof(McpException))
        {
            // Tools throw plain McpException for expected, agent-facing errors (rejected query, unknown object).
            // Return them as tool errors here so they aren't logged as server failures with a stack trace.
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = ex.Message }] };
        }
    }));

var app = builder.Build();

app.MapHealthChecks("/health");

// No authentication yet: keep this behind a trusted network until the auth layer lands.
app.MapMcp("/mcp");

app.Run();

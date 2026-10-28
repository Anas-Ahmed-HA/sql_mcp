# AI_SqlMCP

An MCP server that gives AI agents read-only access to whitelisted SQL Server views. It runs on
**localhost** for now; later it will be hosted on a server and agents will connect over HTTPS. The long-term
goal is access across many databases, with per-user visibility decided by deterministic policy, never by the agent.

**Status: phase 0.4.** Several databases, each fully separate, five tools, a business-rules document per database
whose **Allowed views** table is an enforced whitelist, guarded SELECT queries and an audit log. **No
authentication yet:** every caller can use every configured database.

| Document | For |
|---|---|
| [AGENTS.md](AGENTS.md) | Conventions (partial classes) and security rules for contributors and AI agents |
| [metadata/guidelines.md](metadata/guidelines.md) | The standard every metadata file follows |

## Tech stack

| Concern | Choice |
|---|---|
| Runtime | .NET 8 / ASP.NET Core (set once in `Directory.Build.props`) |
| MCP | `ModelContextProtocol.AspNetCore` 2.3.0 (official C# SDK), Streamable HTTP, stateless |
| Database | SQL Server via `Microsoft.Data.SqlClient` |
| SQL validation | `Microsoft.SqlServer.TransactSql.ScriptDom` (Microsoft's T-SQL parser) |

Stateless means no per-client session in memory: any instance can serve any request behind a load balancer.
The server can't send requests back to the client (sampling, elicitation), which a data gateway doesn't need.

## Tools

Every tool except `list_databases` takes a `database` argument: one of the names `list_databases` returns.

| Tool | Returns |
|---|---|
| `list_databases` | The configured databases, with what each is for (from its metadata file) |
| `get_business_rules` | The database's metadata document: allowed views, column meanings, rules, relationships |
| `list_objects` | The exposed views, with descriptions and column counts |
| `describe_object` | One view's columns, keys and constraints, plus its section of the metadata document |
| `run_select` | The rows of one guarded T-SQL `SELECT` (row cap, timeout, audited) |

Endpoints: `POST /mcp` (MCP, rate-limited per IP) and `GET /health` (also checks that SQL Server answers).

## Errors

A failed tool call returns a tool error whose text is JSON, so the agent can tell what went wrong and fix it:

```json
{ "code": "invalid_column", "message": "Invalid column name 'Compnay'.", "fix": "Call describe_object for the exact column names of the object and use one of them.", "retryable": false }
```

`retryable: true` means the same call may work later unchanged. Every code is defined in
[AgentErrors.cs](src/SqlMcp.Server/Errors/AgentErrors.cs); SQL Server errors are mapped by number in
[AgentErrors.Sql.cs](src/SqlMcp.Server/Errors/AgentErrors.Sql.cs).

| Code | When |
|---|---|
| `missing_argument`, `invalid_argument` | A tool argument is missing or malformed |
| `unknown_database` | The `database` isn't one `list_databases` returns |
| `database_unavailable` | SQL Server can't be reached (retryable) |
| `unknown_object`, `ambiguous_object` | The object isn't exposed, or needs its schema |
| `syntax_error`, `not_a_single_select`, `query_too_long`, `cross_database`, `blocked_construct` | The guard rejected the query; nothing ran |
| `invalid_column`, `ambiguous_column`, `not_in_group_by`, `conversion_failed`, `divide_by_zero` | SQL Server rejected the query |
| `timeout` | The query ran out of time |
| `deadlock` | SQL Server picked the query as a deadlock victim (retryable) |
| `sql_error` | Any other SQL Server error, with its message |

Objects and databases that aren't allowed get the same error as ones that don't exist.

## How access is controlled

0. **Databases are separate.** Each configured database has its own connection string (and login), metadata file,
   whitelist and cached catalog. A query runs against one database; the guard rejects database-qualified names,
   so it can't reach another one. Every tool resolves its `database` argument through
   [IDatabaseAccess](src/SqlMcp.Server/Security/IDatabaseAccess.cs) first. An unknown database gets "not found";
   per-agent rules (API keys, then user identity) will plug in there and use the same wording.
1. **Database login:** read-only. [sql/01_create_readonly_principal.sql](sql/01_create_readonly_principal.sql)
   creates one that can only `SELECT` views.
2. **Allowed views whitelist:** the `## Allowed views` table in `metadata/<ConnectionName>.md` is the only
   object-level access control in the server, one per database. Objects not listed don't exist for agents, and every tool treats them as missing.
   It **fails closed**: no file or no table means nothing is exposed. Changes apply on the next call. Every
   column of an allowed view can be queried, so to keep a column away from agents, whitelist a narrower view.
3. **SQL guard** ([SelectQueryGuard.cs](src/SqlMcp.Server/Query/SelectQueryGuard.cs)) parses the query and allows
   exactly one `SELECT` statement:
   - The only table sources are allowed views, subqueries, joins, CTEs, `VALUES`, `PIVOT`/`UNPIVOT`,
     `OPENJSON` and `STRING_SPLIT`/`GENERATE_SERIES`.
   - It rejects `SELECT INTO`, DML, DDL, `EXEC` and multiple statements or batches.
   - It rejects database-qualified names, `OPENROWSET`/`OPENQUERY`, table-valued and user-defined functions,
     metadata functions such as `OBJECT_ID`, variable assignment and `NEXT VALUE FOR`.
   - An object that isn't allowed gets SQL Server's own wording, `Invalid object name '…'.`, so it can't be
     told apart from a missing one.
4. **Execution limits:**
   - command timeout
   - row cap: the server stops sending rows at the limit, and the result says it was cut off
   - long text cut to a maximum length
5. **Audit:** every call writes one JSON line to `logs/audit-yyyy-MM-dd.jsonl`, including rejected and failed
   queries. Each line holds the caller IP and user agent, the database, the SQL, the outcome, the objects read,
   the rows returned and the duration.

## Metadata

One file per database, `metadata/<ConnectionName>.md`, written to [metadata/guidelines.md](metadata/guidelines.md).
A database without a file is listed by `list_databases` but exposes no objects:

- **Sections:** allowed views, then a section per view (columns, business rules, notes), then relationships,
  then additional metadata.
- **Deployment:** the files are copied next to the binary on build. The build replaces the deployed copy only
  when the repo copy is newer.
- **Live reload:** the server re-reads the deployed copy whenever it changes, so no restart is needed.
- **Checks logged at startup and after each change:**
  - whitelist entries that are malformed or match nothing
  - allowed views without a Columns table
  - columns listed that don't exist, and columns not described

## Configuration

| Section | Settings |
|---|---|
| `Database` | `ConnectionStrings` (one or more; each name is what agents pass as `database` and the metadata file name, letters, digits, `_`, `.` and `-` only), `CommandTimeoutSeconds`, `CatalogCacheMinutes` |
| `Query` | `DefaultRows` (100), `MaxRows` (1000), `MaxCellLength` (1000), `AuditDirectory` (`logs`) |
| `Metadata` | `Directory` (`metadata`, relative to the app folder) |
| `RateLimit` | `RequestsPerMinute` (120) per caller IP on `/mcp` |
| `AllowedHosts` | `*`; set to the server's host name on a server |

**Where each setting lives:**

| File | What goes there | Committed? |
|---|---|---|
| `appsettings.json` | Defaults that are the same everywhere: limits, timeouts, log levels | Yes, no secrets |
| `Properties/launchSettings.json` | How `dotnet run` starts it locally: `http://localhost:5100`, Development | Yes |
| `appsettings.Local.json` | This machine's settings: connection strings, and on a server `AllowedHosts` and `Query:AuditDirectory` | No: gitignored and never published |
| IIS | Address, HTTPS certificate, app pool | No |

`appsettings.json` has no connection strings, so the server won't start until `appsettings.Local.json` lists at
least one. To create it, copy
[appsettings.Local.example.json](src/SqlMcp.Server/appsettings.Local.example.json) to `appsettings.Local.json` and
fill it in. Give each database a login that can open only that database, so the separation also holds in SQL
Server. Server host names and paths go there too, not in a committed file, because the repository is public.

At startup the server logs an error if it can't write to the audit folder, and outside Development a warning if
`AllowedHosts` is still `*`.

Precedence, lowest to highest: `appsettings.json` → user secrets → `appsettings.Local.json` → environment
variables → command line.

**TLS:** SqlClient validates the SQL Server's certificate. With a self-signed or internal-CA certificate, either
trust the CA on the host or, on a trusted network only, add `TrustServerCertificate=True`.

## Run

```bash
dotnet run --project src/SqlMcp.Server
```

```bash
curl http://localhost:5100/health
```

```bash
dotnet test
```

Connect Claude Code:

```bash
claude mcp add --transport http ai-sql-mcp http://localhost:5100/mcp
```

`dotnet run` takes the address from `launchSettings.json`. To run a second instance, change the port with
`--urls`, or with the `Urls` or `ASPNETCORE_URLS` environment variable.

## Deploying to IIS

**Exposure:** until auth exists, anyone who can reach the URL can use every tool, limited only by the per-IP
rate limit. Keep the site on the internal network. Agents inside the network, such as Claude Code, can use an
internal URL. Cloud-hosted connectors, such as claude.ai custom connectors, need a public URL, so don't use
them yet.

**Once per server:**

1. Install the **ASP.NET Core 8 Hosting Bundle**, then restart IIS.
2. Create an application pool with **.NET CLR version: No Managed Code**, and a site whose physical path is the
   deploy folder (for example `C:\inetpub\ai-sql-mcp`). Bind HTTPS with a certificate for the host name. IIS
   bindings decide the address.
3. Copy [appsettings.Local.example.json](src/SqlMcp.Server/appsettings.Local.example.json) into the deploy folder
   as `appsettings.Local.json`. Fill in the host name, the audit folder and one connection string per database.
   It's the server's own copy and is never published. Limit its NTFS permissions to administrators and
   `IIS AppPool\<pool name>` (read).
4. Give `IIS AppPool\<pool name>` **Modify** rights on the audit directory. The server checks this at startup and
   logs an error if it can't write there. Queries still run in that case, without an audit record.

**Each deploy:**

```bash
dotnet publish src/SqlMcp.Server -c Release -o <publish folder>
```

Stop the site, copy the publish folder over the deploy folder, and start the site again. The publish output
has no `appsettings.Local.json`, so the server's copy is kept. Don't delete the deploy folder before copying.

**Metadata:** the deployed metadata file is replaced only when the published copy is newer. An edit made on
the server can be overwritten by the next deploy, so make edits in the repo.

**Check it:**

```bash
curl https://<host>/health
```

`Healthy` means the server is running and every configured database answers. `Unhealthy` (HTTP 503) means at least one can't be
reached; the event log names it. The other databases keep working. Then connect an agent:

```bash
claude mcp add --transport http ai-sql-mcp https://<host>/mcp
```

**Logs:** warnings and errors, including whitelist and metadata problems, go to the Windows **Application**
event log. If the site doesn't start at all, set `stdoutLogEnabled="true"` in the deployed `web.config` for a
moment and read the `logs\stdout` file.

**Rate limit:** `RateLimit:RequestsPerMinute` (120) per caller IP; more requests get HTTP 429. IIS passes the
real client IP. If a load balancer or proxy is put in front of IIS, every caller shares the proxy's IP (in the
rate limit and the audit log) until forwarded headers are configured.

## Identity model (decided)

Agents act **on behalf of end users**. Authentication is open for now and will arrive as delegated user tokens
(OAuth / Entra ID). All caller identity comes from
[ICallerContext](src/SqlMcp.Server/Security/ICallerContext.cs), which currently holds IP and user agent, so
auth slots in there without touching the tools.

## Layout

Every class is `partial`. Classes with several concerns are split into `ClassName.Concern.cs` files, with
one file per MCP tool (see [AGENTS.md](AGENTS.md)).

```
metadata/                  guidelines.md + one <ConnectionName>.md per connection
sql/                       read-only principal script
src/SqlMcp.Server/
  Catalog/                 exposed objects (whitelist applied), object descriptions, ObjectName parsing
  Configuration/           Database / Query / Metadata options
  Data/                    connection factory, startup and health checks
  Errors/                  every agent-facing error: code, message, fix
  Metadata/                metadata file loading, parsing, Allowed views whitelist
  Query/                   SQL guard, executor, audit log
  Security/                caller identity, which databases a caller may use
  Tools/                   MCP tools and their response shapes
  Program.cs               host, configuration, MCP registration
tests/SqlMcp.Tests/        guard, metadata parsing, whitelist, name parsing, database names
```

## Roadmap

| Phase | Scope |
|---|---|
| **0** | HTTP host on localhost, config, DB connectivity, read-only principal *(done)* |
| **0.1** | Catalog, `list_objects`, `describe_object` *(done)* |
| **0.2** | `run_select`: SQL guard, row cap, timeout, audit log *(done)* |
| **0.3** | Metadata files, `get_business_rules`, enforced Allowed views whitelist *(done)* |
| **0.4** | Multiple separate databases, `list_databases`, `database` argument on every tool *(done)* |
| 1 | Per-agent API keys mapped to allowed databases; then delegated user tokens (Entra ID) |
| 2 | Per-user entitlements and policy; row filters; SQL Server RLS |
| 3 | Identity mapping across systems; cross-system join rules |

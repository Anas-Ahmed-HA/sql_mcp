# AI_SqlMCP

An MCP server that gives AI agents governed, read-only access to SQL Server tables and views.
For now it runs on **localhost**. Later it will be hosted on a server, and agents will connect over HTTPS.
The long-term goal is access across many databases and solutions, with per-caller visibility decided by
deterministic policy, never by the agent.

**Current phase: 0.2.** The server serves MCP over Streamable HTTP for one database, with the object
scope, object/column exclusions, and three tools: `list_objects`, `describe_object` and `run_select`.
Agents can read data from the exposed views through guarded SELECT queries, and every query is audited.
**There is no authentication yet.**

## Tech stack

| Concern | Choice |
|---|---|
| Runtime | .NET 8 / ASP.NET Core (set once in `Directory.Build.props`) |
| MCP | `ModelContextProtocol.AspNetCore` 2.3.0 (official C# SDK), Streamable HTTP, stateless |
| Database | SQL Server via `Microsoft.Data.SqlClient` |
| SQL validation (planned) | `Microsoft.SqlServer.TransactSql.ScriptDom` (the T-SQL parser SSMS uses) |
| DB principal | Read-only login, ideally views only (`sql/01_create_readonly_principal.sql`) |

### Why stateless

The server keeps no per-client session in memory (`HttpServerSessionMode.Stateless`, which is also the
SDK default since the 2026-07-28 protocol revision). Any instance can serve any request, so it scales
behind a load balancer without sticky sessions. The trade-off: the server can't send requests back to
the client (sampling, elicitation, roots). A data gateway doesn't need those.

## Endpoints

| Path | Purpose |
|---|---|
| `POST /mcp` | MCP Streamable HTTP endpoint (JSON-RPC in, SSE-framed response out) |
| `GET /health` | Liveness check for load balancers and monitoring |

## Tools

| Tool | Status |
|---|---|
| `list_objects` | Implemented (0.1) |
| `describe_object` | Implemented (0.1) |
| `run_select` | Implemented (0.2) |

### Object scope (server-enforced)

A server setting decides which object types the MCP exposes at all:

```json
"Database": { "ObjectScope": "Views" }   // Views | Tables | All, default Views
```

The agent can't change it. Every tool applies it: listing, describing, and the query guard, which
rejects any query that references an object outside the scope. A tool's `kind` parameter can only
**narrow** within the scope, never widen it. Asking for an out-of-scope kind returns an error.
Describing an out-of-scope object returns "not found", the same as an object that doesn't exist, so the
response never reveals that it exists.

Out-of-scope objects are never loaded into the catalog. The list of in-scope objects is cached in memory
for `CatalogCacheMinutes` (default 10), so new or dropped objects show up within that time.

### Exclusions (server-enforced)

Inside the scope, specific objects and columns can be hidden:

```json
"Database": {
  "ExcludeObjects": [ "dbo.SomeView" ],
  "ExcludeColumns": {
    "dbo.V_TWEBWF_Employee": [ "NationalID", "PayrollPass", "Birthdate" ],
    "*": [ "SomeColumnHiddenEverywhere" ]
  }
}
```

- **Names:** `schema.object`, or just `object` to match it in any schema. `*` as the `ExcludeColumns` key applies
  to every object. Matching is case-insensitive. Brackets are allowed.
- **Hidden objects** behave exactly like objects that don't exist: absent from `list_objects`, "not found" in
  `describe_object`, and not listed as anything's dependency.
- **Hidden columns** are absent from `describe_object`, and column counts exclude them. Keys, foreign keys
  and check constraints that involve a hidden column are dropped too, because they would reveal its name.
  `describe_object` notes how many columns are hidden, but not which ones.
- **Typos are logged.** An entry that matches no object or column logs a warning at each catalog load,
  because a misspelled entry hides nothing.
- **`run_select`** applies the same lists: it rejects queries that name a hidden column, and `SELECT *`
  on an object with hidden columns.

The `appsettings.json` in this repo ships with the `V_TWEBWF_Employee` list **empty, by decision**, so every
column of that view is currently visible and queryable. Candidates to hide if that changes, based on
column names only: `NationalID`, `PayrollPass`, `Alg_AccountNo`, `Alg_Passno`, `Alg_AddressPass`,
`Alg_VisaNo`, `Alg_StayNo`, `Alg_Warrantyno`, `Birthdate`, `Children_count`, `CarNo`, `PersonalEmail`,
`Alg_personalEmail`, `Alg_personalPhone`, `Mobile`, `Mobile1`, `Phone`, `Address`, `Street`.

Exclusions shape what this MCP exposes. They don't replace database permissions: the login can still read
the hidden columns, so a DBA should still narrow the login where possible.

This matters because the database login is often broader than what should be exposed. For example,
`Agent` on `Apps_InformationTechnology` is in `db_datareader` and can read 83 tables, while the
intended surface is 5 views. The scope keeps the MCP to the views either way. The DB permission is still
the real boundary, so narrow the login too.

### 1. `list_objects`

Lists the tables and/or views the agent may use.

| Parameter | Meaning |
|---|---|
| `kind` | `views`, `tables` or `all`, limited to the server's `ObjectScope` |
| `schema` (optional) | Only this schema |
| `search` (optional) | Name contains this text |

Returns, per object: schema, name, type, description (`MS_Description`), column count, and approximate row
count for tables (from `sys.partitions`). Views don't get a row count, because counting a view runs its
whole query.

### 2. `describe_object`

Returns the schema of one table or view.

- **Columns:** name, type, length/precision, nullable, identity, computed, default, description
- **Constraints:** primary key, unique, check
- **Relationships:** foreign keys in both directions, limited to objects inside the scope
- **For views:** the base objects the view reads (`sys.sql_expression_dependencies`)
- **Business metadata:** the matching section of the connection's metadata file (see below), once that exists

SQL Server views have no keys, constraints or foreign keys. Since the exposed surface is mainly views,
most relationship information has to come from the metadata files, not from the database.

Accepted names: `PBI_PH_Assets`, `dbo.PBI_PH_Assets`, `[dbo].[PBI_PH_Assets]`. Database-qualified names
are rejected. Foreign keys are only returned when both ends are in scope.

**Permissions this tool can use:** with plain `SELECT`, the login sees columns, types, keys and foreign
keys. Default and check-constraint definitions, computed-column formulas, and the objects a view reads
from also need `VIEW DEFINITION`. Without it those fields are empty, and `describe_object` adds a note
saying so. `Agent` on `Apps_InformationTechnology` currently doesn't have it.

### 3. `run_select`

Runs one read-only `SELECT` against objects in scope. It's meant for statistics and exploration
(`COUNT`, `GROUP BY`, `AVG`, `TOP n`), so no separate statistics tool is needed.

| Parameter | Meaning |
|---|---|
| `sql` | A single T-SQL `SELECT` statement (CTEs allowed) |
| `maxRows` (optional) | Row limit; defaults to `Query:DefaultRows`, capped at `Query:MaxRows` |

Returns `columns` (name and SQL type), `rows` (arrays of values), `rowCount`, `truncated`, `elapsedMs`, and a
`note` when the result was cut off.

**Guard** ([SelectQueryGuard.cs](src/SqlMcp.Server/Query/SelectQueryGuard.cs)), applied before anything
reaches the database. It parses with ScriptDom's `TSql170Parser` (SQL Server 2025 syntax), never with text
patterns, so comments, casing, brackets and whitespace make no difference.

- exactly one batch with exactly one statement, and it must be a `SELECT`: no `SELECT INTO`, DML, DDL, `EXEC`,
  `WAITFOR` or `GO`-separated batches
- every referenced object must exist in the catalog **and** be inside `ObjectScope`, and excluded objects don't count
  (CTE names are recognized)
- no hidden column may be referenced anywhere (select list, `WHERE`, `JOIN`, `ORDER BY`, subqueries, CTEs), and no
  `SELECT *` on an object that has hidden columns (`COUNT(*)` is fine)
- no three- or four-part names (no cross-database or linked-server access)
- table sources limited to tables/views, subqueries, joins, `VALUES`, `PIVOT`/`UNPIVOT`, `OPENJSON`,
  `STRING_SPLIT`/`GENERATE_SERIES`: so no `OPENROWSET`, `OPENQUERY`, `OPENDATASOURCE` or table-valued functions
- no user-defined function or method calls, no metadata functions (`OBJECT_ID`, `OBJECT_DEFINITION`, `COL_NAME`,
  `HAS_PERMS_BY_NAME`, …), no variable assignment, no `NEXT VALUE FOR`

Hidden columns and hidden objects are rejected with SQL Server's own wording (`Invalid column name 'X'.`,
`Invalid object name 'X'.`), so an agent can't tell a hidden column from one that doesn't exist.

Known limits of the guard:
- Hidden columns are matched **by name** across all objects in the query. A visible column that happens to
  share a hidden column's name in another object is rejected too, so the query has to avoid that name.
- `SELECT t.*` is rejected if **any** object in the query has hidden columns, even when `t` itself has none.

**Execution** ([SelectQueryExecutor.cs](src/SqlMcp.Server/Query/SelectQueryExecutor.cs)): command timeout
(`Database:CommandTimeoutSeconds`). Rows stop at the limit, and the server is told to stop sending the rest.
Long text is cut to `Query:MaxCellLength` characters, and binary values are summarized. SQL Server errors
(bad column, conversion) are passed back unchanged so the agent can fix its query.

**Audit** ([AuditLog.cs](src/SqlMcp.Server/Query/AuditLog.cs)): every call writes one JSON line to
`<Query:AuditDirectory>/audit-yyyy-MM-dd.jsonl`, with these fields:
- timestamp, caller (IP and user agent until auth exists, then the user)
- SQL text, outcome (`ok`, `rejected`, `error`, `cancelled`) and reason
- objects read, row count, truncated, duration

Rejected and failed queries are audited too. A failed audit write is logged but doesn't block the query.
On a server, set `AuditDirectory` to an absolute path outside the app folder. Behind a reverse proxy, the
recorded IP is the proxy's until forwarded headers are configured.

**Errors:** expected errors (rejected query, unknown object) are returned to the agent as tool errors. They
are not logged as server failures.

## Business-rules metadata (planned)

Each connection string will get a Markdown file with the knowledge the database itself doesn't hold:

```
metadata/
└─ Apps_InformationTechnology.md     one file per entry under Database:ConnectionStrings
```

Suggested sections (the examples are illustrative, not verified facts about the data):

| Section | Example content |
|---|---|
| Purpose | What the database is for and who owns it |
| Objects | Per view or table: what one row means ("one row = one IT asset"), key columns |
| Relationships | Logical joins between views and tables, e.g. `<ViewA>.<Column> → <ViewB>.<Column>` |
| Business rules | e.g. "Inactive assets are excluded from counts unless asked for" |
| Codes and values | e.g. `AssignType`: `Emp` = assigned to an employee, `Loc` = assigned to a location |
| Sensitive columns | Columns holding personal data, e.g. employee names and HR codes |

How the server will use them:

- exposed as MCP **resources** (`metadata://<ConnectionName>`) so an agent can read them in full
- the relevant section included in `describe_object` output
- the server's MCP instructions tell agents to read the metadata before writing queries

The metadata files help the agent **understand** the data and write correct queries. They don't
**enforce** anything: an agent can ignore text. Rules that restrict who sees what belong in code and
policy (roadmap phase 2), never only in these files.

## Layout

```
AI_SqlMCP/
├─ Directory.Build.props          shared target framework / language settings
├─ AI_SqlMCP.sln
├─ sql/
│  └─ 01_create_readonly_principal.sql   login + user + [mcp_view_reader] role, views only
└─ src/SqlMcp.Server/
   ├─ Program.cs                  ASP.NET Core host, options, MCP over HTTP, endpoints
   ├─ appsettings.json            includes "Urls": http://localhost:5100
   ├─ Properties/launchSettings.json      local dev: http://localhost:5100, Development env
   ├─ Configuration/
   │  └─ DatabaseOptions.cs       "Database" config section, ObjectScope enum
   ├─ Data/
   │  ├─ ISqlConnectionFactory.cs single place connections are opened
   │  ├─ SqlConnectionFactory.cs
   │  ├─ SqlDataReaderExtensions.cs
   │  └─ DatabaseStartupCheck.cs  logs server/db/login/view count, warns if the login can write
   ├─ Catalog/
   │  ├─ IDatabaseCatalog.cs      snapshot of in-scope objects (cached) + describe one object
   │  ├─ DatabaseCatalog.cs       queries sys.objects / sys.columns / sys.indexes / sys.foreign_keys / ...
   │  ├─ CatalogSnapshot.cs       in-scope objects, name resolution
   │  ├─ ExclusionPolicy.cs       ExcludeObjects / ExcludeColumns matching + typo detection
   │  ├─ SqlNames.cs              multi-part name parsing ([schema].[name])
   │  └─ CatalogModels.cs
   ├─ Query/
   │  ├─ SelectQueryGuard.cs      parses and validates agent SQL (ScriptDom)
   │  ├─ SelectQueryExecutor.cs   runs approved SQL: timeout, row cap, JSON-friendly values
   │  └─ AuditLog.cs              JSON-lines audit file + log summary
   ├─ Security/
   │  └─ ICallerContext.cs        the single source of caller identity (IP / user agent until auth)
   └─ Tools/
      ├─ CatalogTools.cs          list_objects, describe_object
      ├─ CatalogToolResults.cs    response shapes returned to agents
      └─ QueryTools.cs            run_select
tests/SqlMcp.Tests/
   └─ SelectQueryGuardTests.cs    what the guard allows and rejects
```

Planned additions:

```
├─ metadata/      <ConnectionName>.md business-rules files
└─ src/SqlMcp.Server/
   ├─ Metadata/   loads metadata/*.md, serves them as MCP resources
   ├─ Auth/       token validation, fills ICallerContext with the user
   └─ Security/   entitlement adapters, policy, row/column enforcement
```

## Setup

### 1. Create the read-only principal

Edit the `:setvar` values at the top of `sql/01_create_readonly_principal.sql`, then run it as a
sysadmin or db_owner, in SSMS's SQLCMD mode or with:

```bash
sqlcmd -S <server> -E -C -i sql/01_create_readonly_principal.sql
```

The role gets `SELECT` on each view in one schema, and nothing on tables. Ownership chaining lets
views read their base tables. The script ends with a check that should show `VisibleTables = 0`
and `CanInsert = 0`.

### 2. Configure the connection string

Connection strings are named per database under `Database:ConnectionStrings`. Phase 0 requires exactly one:

```json
"Database": {
  "ConnectionStrings": {
    "Apps_InformationTechnology": "Server=<server>;Database=Apps_InformationTechnology;UID=<login>;PWD=<pwd>;"
  },
  "ObjectScope": "Views",
  "ExcludeObjects": [],
  "ExcludeColumns": { "dbo.V_TWEBWF_Employee": [ "NationalID", "..." ] },
  "CommandTimeoutSeconds": 30,
  "CatalogCacheMinutes": 10
}
```

Limits for `run_select` live in their own section:

```json
"Query": {
  "DefaultRows": 100,
  "MaxRows": 1000,
  "MaxCellLength": 1000,
  "AuditDirectory": "logs"
}
```

`appsettings.json` is committed, so it keeps the connection-string **key with an empty value**. Put the real
value somewhere that isn't committed:

- **Local development:** `src/SqlMcp.Server/appsettings.Local.json`, which is gitignored, loaded in every
  environment and copied next to the binary on build:

  ```json
  {
    "Database": {
      "ConnectionStrings": {
        "Apps_InformationTechnology": "Server=<server>;Database=<db>;UID=<login>;PWD=<pwd>;"
      }
    }
  }
  ```

  User secrets also work, but only in the Development environment:

  ```bash
  dotnet user-secrets --project src/SqlMcp.Server set "Database:ConnectionStrings:Apps_InformationTechnology" "Server=<server>;Database=<db>;UID=<login>;PWD=<pwd>;"
  ```

- **Server:** the environment variable `Database__ConnectionStrings__Apps_InformationTechnology`, or your
  secret store (Azure Key Vault, IIS app pool environment, etc.).

Precedence, lowest to highest: `appsettings.json` → `appsettings.{Environment}.json` → user secrets →
`appsettings.Local.json` → environment variables → command line.

**TLS:** SqlClient encrypts and validates the server certificate by default. If the SQL Server uses a
self-signed or internal-CA certificate, the login fails with *"certificate chain was issued by an
authority that is not trusted"*. Fix it properly by trusting the issuing CA on the MCP host, or, on a
trusted network only, add `TrustServerCertificate=True` to the connection string.

### 3. Run locally

```bash
dotnet run --project src/SqlMcp.Server
```

It listens on `http://localhost:5100` only, set by `Urls` in `appsettings.json`, so other machines can't
reach it. Check it with:

```bash
curl http://localhost:5100/health
```

Run the guard tests:

```bash
dotnet test
```

### 4. Connect an agent

Claude Code:

```bash
claude mcp add --transport http ai-sql-mcp http://localhost:5100/mcp
```

Any other MCP client: point its Streamable HTTP transport at `http://localhost:5100/mcp`.

## Deploying to a server (later)

- **Hosting:** IIS (ASP.NET Core Hosting Bundle 8), a Windows service, or a container. Change `Urls`
  to the address it should listen on, or override it with the environment variable `Urls` or `--urls` on the
  command line. `ASPNETCORE_URLS` does **not** work here, because the `Urls` value in `appsettings.json`
  takes precedence over it.
- **TLS:** terminate HTTPS at IIS or a reverse proxy, or configure a Kestrel certificate. Agents should
  only connect over `https://`.
- **Response buffering:** responses are SSE (`text/event-stream`). Proxies that buffer responses
  (IIS ARR, nginx `proxy_buffering on`) must not buffer this path.
- **Host filtering:** set `AllowedHosts` in `appsettings.json` to the server's real host names instead of `*`.
- **Exposure:** the endpoint is open by decision until the auth phase (see below). Anyone who can reach
  the URL can use every tool with the read-only principal's full access, so prefer an internal network
  or VPN over a public address in the meantime.
- **Which agents can connect:** agents running inside your network (such as Claude Code) can use an
  internal URL. Cloud-hosted connectors (such as custom connectors in claude.ai) connect from the
  provider's cloud and need a public HTTPS address, so don't use them until auth exists.

## Identity model (decided)

Agents act **on behalf of end users**: what an agent sees is what its user may see. Authentication is
**deliberately open for now** and will arrive as delegated user tokens (OAuth / Entra ID, with
on-behalf-of where needed). Until then the server cannot tell callers apart, so per-user rules can't be
enforced. The code should still keep a single "current caller" abstraction, so auth slots in without
touching the tools.

## Design rules (hold for every phase)

1. **The agent never decides access.** Visibility is enforced in code and in the database.
2. **The DB principal is the last line of defense.** It can only read, whatever the query layer allows.
3. **All connections go through `ISqlConnectionFactory`.** This is where per-caller `SESSION_CONTEXT` (RLS) is set later.
4. **Stateless.** No per-caller state in process memory. Anything shared (catalog cache, entitlements) must be
   safe to rebuild on any instance.
5. **Metadata explains, policy enforces.** The Markdown metadata files never act as access control.

## Roadmap

| Phase | Scope |
|---|---|
| **0** | Skeleton: HTTP host on localhost, config, DB connectivity, read-only principal *(done)* |
| **0.1** | `ObjectScope` setting + catalog + `list_objects` / `describe_object` tools + object/column exclusions *(done)* |
| **0.2** | `run_select` tool: ScriptDom guard, row cap, timeout, audit log (caller IP until auth exists) *(done)* |
| 0.3 | Per-connection metadata files (`metadata/<ConnectionName>.md`) as MCP resources, merged into `describe_object` |
| 1 | Authentication: delegated user tokens (Entra ID), caller identity, `/.well-known/oauth-protected-resource` |
| 2 | Entitlement model + policy engine; row filters / column masking; SQL Server RLS |
| 3 | Multiple databases / solutions; identity mapping; cross-system join rules |

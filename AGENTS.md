# AGENTS.md

Instructions for AI coding agents (and people) working in this repository. See [README.md](README.md) for
what the server does and why.

## Commands

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/SqlMcp.Server
```

`dotnet run` listens on `http://localhost:5100` (from `Properties/launchSettings.json`; MCP endpoint `/mcp`,
health `/health`). To run a second instance for testing, override the port with `--urls` or the `Urls`
environment variable. It needs `src/SqlMcp.Server/appsettings.Local.json` with at least one connection string;
copy `appsettings.Local.example.json`.

## C# conventions

### Partial classes (required)

- **Every class, record and struct is declared `partial`**: `public sealed partial class`,
  `internal static partial class`, `public sealed partial record`, `private readonly partial record struct`.
  Interfaces and enums are exempt.
- **Split a class by concern** into files named `ClassName.Concern.cs`, next to `ClassName.cs`:
  - `ClassName.cs` holds the declaration: XML summary, attributes (e.g. `[McpServerToolType]`), base types,
    constructor, fields, and the main entry points.
  - Each `ClassName.Concern.cs` file holds one concern: SQL text (`.Sql.cs`), one MCP tool per file
    (`CatalogTools.ListObjects.cs`), a nested type (`SelectQueryGuard.Visitor.cs`), static rule tables
    (`.Rules.cs`), diagnostics, value conversion.
  - Don't split a small class with a single concern just to have more files; still mark it `partial`.
- **Primary constructors:** their parameters are only in scope in the part that declares them. In a class
  split over several files, assign the parameters to `private readonly` fields in `ClassName.cs` and use the
  fields everywhere else.
- Only `ClassName.cs` repeats base types, attributes and the XML summary. Other parts are just
  `public sealed partial class ClassName`, with a one-line comment saying what the part contains.
- `Program.cs` uses top-level statements and is exempt.

### Style

- File-scoped namespaces matching the folder (`SqlMcp.Server.Catalog`).
- Records for data shapes. Shapes returned to agents live in `Tools/*Results.cs` and omit empty optional
  fields with `[JsonIgnore(Condition = ...)]`.
- Comments explain why, not what. Keep the existing comment density.
- Nullable reference types are on. The build must stay at zero warnings.

## Security rules (do not weaken)

1. **The agent never decides access.** Visibility is enforced in code and in the database, never in prompts
   or metadata text.
2. **All agent SQL goes through `SelectQueryGuard`** before `SelectQueryExecutor` runs it. Never execute
   unvalidated text.
3. **Every tool resolves `database` through `IDatabaseAccess` before anything else**, and uses only that
   database's connection, metadata file and catalog. Databases never share a snapshot or a connection, and a
   query never names another database (the guard rejects it). Unknown and not-allowed databases get the same
   "not found" wording.
4. **The Allowed views whitelist is the only object access control.** The `## Allowed views` table in
   each database's `metadata/<ConnectionName>.md` (format: [metadata/guidelines.md](metadata/guidelines.md)) decides which
   objects exist for agents. It fails closed: no file or no table means nothing is exposed. Don't add other
   object or column filters; a column that must stay away from agents belongs in a narrower database view.
   Everything else in the metadata files is explanation and is never used for access control.
5. **Not allowed must look like missing.** The catalog snapshot only contains whitelisted objects. Anything
   else gets SQL Server's own wording (`Invalid object name '…'.`) and never appears in keys, relationships
   or dependencies.
6. **No secrets or server details in Git.** Connection strings, host names and server paths go in
   `src/SqlMcp.Server/appsettings.Local.json` (gitignored, never published). `appsettings.json` holds only
   defaults that are the same everywhere, and `appsettings.Local.example.json` only placeholders.
7. **Every `run_select` call is audited**, including rejected and failed ones, with its database.

## Errors

- Agent-facing errors are `AgentError`s created in `Errors/AgentErrors*.cs` and thrown with `.ToException()`.
  Don't throw `McpException` or return error text directly: the call-tool filter in `Program.cs` turns
  `AgentErrorException` into the JSON agents rely on (`code`, `message`, `fix`, `retryable`).
- A new kind of error gets a new factory method with a stable `code` and a `fix` that says what to do next.
  Don't change existing codes; agents may branch on them.
- Errors for objects or databases that aren't allowed must be identical to the ones for missing ones.

## Tests

- Any change to `SelectQueryGuard` needs cases in `tests/SqlMcp.Tests/SelectQueryGuardTests.*.cs`: one that
  should pass and one that should be rejected.
- For tool changes, run the server on a spare port and call the tools over HTTP. Stop the process
  afterwards, so it doesn't keep holding the port and the build output.

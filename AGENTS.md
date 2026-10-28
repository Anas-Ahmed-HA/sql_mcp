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

The server listens on `http://localhost:5100` (MCP endpoint `/mcp`, health `/health`). To run a second
instance for testing, override the port with the `Urls` environment variable or `--urls`.
`ASPNETCORE_URLS` does not work, because `Urls` in `appsettings.json` takes precedence over it.

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
3. **Hidden must look like missing.** Excluded objects and hidden columns return the same wording SQL Server
   uses for objects and columns that don't exist (`Invalid object name '…'.`, `Invalid column name '…'.`),
   and never appear in metadata, keys, relationships or dependencies.
4. **Out of scope is never loaded.** The catalog snapshot only contains in-scope, non-excluded objects.
5. **Metadata explains, policy enforces.** The `metadata/*.md` files help agents understand the data. They
   are never used for access control.
6. **No secrets in Git.** Connection strings with passwords go in `src/SqlMcp.Server/appsettings.Local.json`
   (gitignored) or environment variables. `appsettings.json` keeps empty values.
7. **Every `run_select` call is audited**, including rejected and failed ones.

## Tests

- Any change to `SelectQueryGuard` needs cases in `tests/SqlMcp.Tests/SelectQueryGuardTests.*.cs`: one that
  should pass and one that should be rejected.
- For tool changes, run the server on a spare port and call the tools over HTTP. Stop the process
  afterwards, so it doesn't keep holding the port and the build output.

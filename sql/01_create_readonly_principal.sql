/*
    AI_SqlMCP - least-privilege principal for the MCP server.

    What it creates:
      - a login + database user for the MCP server
      - a role [mcp_view_reader] that can SELECT the views in one schema, and nothing else
        (no base tables, no writes, no EXEC)

    Why views only: with ownership chaining (view and tables owned by the same owner, usually dbo)
    the user can read through the views without any permission on the underlying tables.
    Views that read from ANOTHER database break the chain; the user then needs access there too.

    How to run:
      - SSMS: Query > SQLCMD Mode, edit the :setvar values below, execute as a sysadmin / db_owner.
      - sqlcmd: sqlcmd -S <server> -E -C -i sql\01_create_readonly_principal.sql   (after editing the values)

    Re-runnable. Re-run it after adding new views (or switch to the schema-level grant at the bottom).
*/

:setvar DatabaseName  "YourDatabase"
:setvar LoginName     "ai_sql_mcp_reader"
:setvar LoginPassword "Change_Me_To_A_Strong_Password_1"
:setvar ViewSchema    "dbo"

SET NOCOUNT ON;
USE [master];
GO

/* ---- 1. Login ------------------------------------------------------------------------------
   Option A (default): SQL authentication. Requires the server to allow mixed-mode auth.
   Option B: Windows / Entra ID account. Comment out option A and use:
       IF SUSER_ID(N'DOMAIN\svc-ai-sql-mcp') IS NULL CREATE LOGIN [DOMAIN\svc-ai-sql-mcp] FROM WINDOWS;
   and set LoginName to that account name.                                                       */
IF SUSER_ID(N'$(LoginName)') IS NULL
    CREATE LOGIN [$(LoginName)]
        WITH PASSWORD = N'$(LoginPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = [$(DatabaseName)];
GO

USE [$(DatabaseName)];
GO

/* ---- 2. User + role ---------------------------------------------------------------------- */
IF USER_ID(N'$(LoginName)') IS NULL
    CREATE USER [$(LoginName)] FOR LOGIN [$(LoginName)] WITH DEFAULT_SCHEMA = [$(ViewSchema)];

IF DATABASE_PRINCIPAL_ID(N'mcp_view_reader') IS NULL
    CREATE ROLE [mcp_view_reader];

IF IS_ROLEMEMBER(N'mcp_view_reader', N'$(LoginName)') = 0
    ALTER ROLE [mcp_view_reader] ADD MEMBER [$(LoginName)];
GO

/* ---- 3. Explicit denies -------------------------------------------------------------------
   Database-level DENY wins over any GRANT the user might pick up later through another role.   */
DENY INSERT, UPDATE, DELETE, EXECUTE, ALTER, REFERENCES TO [mcp_view_reader];
GO

/* ---- 4. SELECT on every view in the schema (not on tables) --------------------------------- */
DECLARE @sql nvarchar(max) = N'';

SELECT @sql += N'GRANT SELECT ON ' + QUOTENAME(s.name) + N'.' + QUOTENAME(v.name) + N' TO [mcp_view_reader];' + NCHAR(10)
FROM sys.views AS v
JOIN sys.schemas AS s ON s.schema_id = v.schema_id
WHERE v.is_ms_shipped = 0
  AND s.name = N'$(ViewSchema)';

EXEC sys.sp_executesql @sql;
PRINT CONCAT(N'Granted SELECT on ', (LEN(@sql) - LEN(REPLACE(@sql, NCHAR(10), N''))), N' view(s) in schema $(ViewSchema).');
GO

/* ---- Alternative to step 4 ----------------------------------------------------------------
   If the schema holds ONLY views (recommended: a dedicated schema such as [mcp] or [reporting]),
   grant on the schema instead; new views are then covered automatically:
       GRANT SELECT ON SCHEMA::[$(ViewSchema)] TO [mcp_view_reader];
   Do NOT do this on a schema that also contains tables - it would expose them.                  */

/* ---- Verify ------------------------------------------------------------------------------- */
EXECUTE AS USER = N'$(LoginName)';
SELECT
    SUSER_SNAME() AS LoginName,
    (SELECT COUNT(*) FROM sys.views  WHERE is_ms_shipped = 0) AS VisibleViews,
    (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS VisibleTables,   -- expect 0
    HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT') AS CanInsert;              -- expect 0
REVERT;
GO

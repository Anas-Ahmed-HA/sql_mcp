using SqlMcp.Server.Catalog;
using SqlMcp.Server.Query;

namespace SqlMcp.Tests;

public class SelectQueryGuardTests
{
    // Two visible views; V_Employee has hidden columns. Anything else (PH_Assets, sys.*) is invisible.
    private static readonly CatalogSnapshot Snapshot = new(
    [
        new CatalogObject(1, "dbo", "PBI_PH_Assets", DbObjectKind.View, null,
            ["ID", "Assined_HRCode", "typename", "Company", "Is_Active"], Hidden(), null),
        new CatalogObject(2, "dbo", "V_Employee", DbObjectKind.View, null,
            ["User_HR_Code", "Name", "Department_ID"], Hidden("NationalID", "PayrollPass"), null),
    ]);

    private static IReadOnlySet<string> Hidden(params string[] columns) =>
        columns.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static GuardResult Validate(string sql) => new SelectQueryGuard().Validate(sql, Snapshot);

    [Theory]
    [InlineData("SELECT COUNT(*) FROM dbo.PBI_PH_Assets")]
    [InlineData("select typename, count(*) as n from pbi_ph_assets group by typename order by n desc")]
    [InlineData("SELECT TOP 10 [ID], [Company] FROM [dbo].[PBI_PH_Assets] WHERE Is_Active = 1 ORDER BY ID DESC")]
    [InlineData("SELECT a.Company, e.Name FROM dbo.PBI_PH_Assets AS a JOIN dbo.V_Employee AS e ON e.User_HR_Code = a.Assined_HRCode")]
    [InlineData("WITH x AS (SELECT Company FROM dbo.PBI_PH_Assets) SELECT Company, COUNT(*) FROM x GROUP BY Company")]
    [InlineData("SELECT ID FROM dbo.PBI_PH_Assets WHERE Assined_HRCode IN (SELECT User_HR_Code FROM dbo.V_Employee)")]
    [InlineData("SELECT * FROM dbo.PBI_PH_Assets")]
    [InlineData("SELECT 1 AS one")]
    [InlineData("SELECT Company FROM dbo.PBI_PH_Assets -- ; DROP TABLE dbo.PH_Assets")]
    [InlineData("SELECT value FROM STRING_SPLIT('a,b', ',')")]
    [InlineData("SELECT Company, COUNT(*) AS n FROM dbo.PBI_PH_Assets GROUP BY Company HAVING COUNT(*) > 5 ORDER BY n DESC OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY")]
    public void Allows_read_only_selects_on_visible_objects(string sql)
    {
        var result = Validate(sql);
        Assert.True(result.IsAllowed, result.Error);
    }

    [Fact]
    public void Reports_the_objects_a_query_reads()
    {
        var result = Validate("SELECT a.ID FROM dbo.PBI_PH_Assets a JOIN V_Employee e ON e.User_HR_Code = a.Assined_HRCode");
        Assert.Equal(["dbo.PBI_PH_Assets", "dbo.V_Employee"], result.Objects.Select(o => o.FullName).Order());
    }

    [Theory]
    [InlineData("INSERT INTO dbo.PBI_PH_Assets (ID) VALUES (1)", "Only SELECT")]
    [InlineData("UPDATE dbo.PBI_PH_Assets SET Company = 'x'", "Only SELECT")]
    [InlineData("DELETE FROM dbo.PBI_PH_Assets", "Only SELECT")]
    [InlineData("MERGE dbo.PBI_PH_Assets AS t USING dbo.V_Employee AS s ON 1 = 0 WHEN NOT MATCHED THEN INSERT (ID) VALUES (1);", "Only SELECT")]
    [InlineData("EXEC sp_who", "Only SELECT")]
    [InlineData("DROP VIEW dbo.PBI_PH_Assets", "Only SELECT")]
    [InlineData("TRUNCATE TABLE dbo.PH_Assets", "Only SELECT")]
    [InlineData("WAITFOR DELAY '00:00:10'", "Only SELECT")]
    [InlineData("SELECT 1; SELECT 2", "single SELECT")]
    [InlineData("SELECT 1 /* */; DROP TABLE dbo.PH_Assets", "single SELECT")]
    [InlineData("SELECT 1\nGO\nSELECT 2", "single SELECT")]
    [InlineData("SELECT ID INTO dbo.Copy FROM dbo.PBI_PH_Assets", "INTO")]
    [InlineData("SELEC 1", "Only SELECT")] // parses as a bare procedure call, which T-SQL allows
    [InlineData("SELECT FROM WHERE", "Syntax error")]
    [InlineData("   ", "empty")]
    public void Rejects_anything_but_one_select(string sql, string expected)
    {
        var result = Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.PH_Assets", "Invalid object name 'dbo.PH_Assets'")]
    [InlineData("SELECT name FROM sys.objects", "Invalid object name 'sys.objects'")]
    [InlineData("SELECT ID FROM dbo.PBI_PH_Assets WHERE ID IN (SELECT Asset_ID FROM dbo.PH_Assets_Attaches)", "Invalid object name")]
    [InlineData("SELECT * FROM Apps_InformationTechnology.dbo.PBI_PH_Assets", "database- or server-qualified")]
    [InlineData("SELECT * FROM LinkedSrv.OtherDb.dbo.Secrets", "database- or server-qualified")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1')", "not allowed as a table source")]
    [InlineData("SELECT * FROM OPENQUERY(LinkedSrv, 'SELECT 1')", "not allowed as a table source")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Server=x;').db.dbo.t", "not allowed")]
    [InlineData("SELECT * FROM dbo.fn_GetAssets(1)", "not allowed as a table source")]
    [InlineData("SELECT dbo.fn_Secret(ID) FROM dbo.PBI_PH_Assets", "user-defined functions")]
    [InlineData("SELECT OBJECT_ID('dbo.PH_Assets')", "metadata function")]
    [InlineData("SELECT OBJECT_DEFINITION(1)", "metadata function")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Seq", "NEXT VALUE FOR")]
    [InlineData("SELECT @x = ID FROM dbo.PBI_PH_Assets", "variables")]
    public void Rejects_objects_outside_the_catalog_and_side_channels(string sql, string expected)
    {
        var result = Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT NationalID FROM dbo.V_Employee")]
    [InlineData("SELECT e.[PayrollPass] FROM dbo.V_Employee AS e")]
    [InlineData("SELECT Name FROM dbo.V_Employee WHERE nationalid LIKE '2%'")]
    [InlineData("SELECT Name FROM dbo.V_Employee ORDER BY NationalID")]
    [InlineData("SELECT COUNT(DISTINCT NationalID) FROM dbo.V_Employee")]
    [InlineData("WITH x AS (SELECT NationalID AS n FROM dbo.V_Employee) SELECT n FROM x")]
    [InlineData("SELECT a.ID FROM dbo.PBI_PH_Assets a WHERE EXISTS (SELECT 1 FROM dbo.V_Employee e WHERE e.NationalID = a.Assined_HRCode)")]
    public void Rejects_hidden_columns_with_the_same_error_as_a_missing_column(string sql)
    {
        var result = Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.StartsWith("Invalid column name '", result.Error);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.V_Employee")]
    [InlineData("SELECT e.* FROM dbo.V_Employee e")]
    [InlineData("SELECT n FROM (SELECT * FROM dbo.V_Employee) AS d(n, m, o)")]
    public void Rejects_select_star_on_objects_with_hidden_columns(string sql)
    {
        var result = Validate(sql);
        Assert.False(result.IsAllowed);
        Assert.Contains("SELECT * is not allowed", result.Error);
    }

    [Fact]
    public void Treats_an_unqualified_cte_name_as_the_cte_not_a_table()
    {
        Assert.True(Validate("WITH PH_Assets AS (SELECT ID FROM dbo.PBI_PH_Assets) SELECT ID FROM PH_Assets").IsAllowed);
        Assert.False(Validate("WITH PH_Assets AS (SELECT ID FROM dbo.PBI_PH_Assets) SELECT ID FROM dbo.PH_Assets").IsAllowed);
    }
}

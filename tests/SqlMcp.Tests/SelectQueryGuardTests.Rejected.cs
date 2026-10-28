namespace SqlMcp.Tests;

// Statements that aren't a single SELECT, and reads outside the catalog or through side channels.
public partial class SelectQueryGuardTests
{
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
        Assert.Contains(expected, result.Error!.Message, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains(expected, result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT FROM WHERE", "syntax_error")]
    [InlineData("DELETE FROM dbo.PBI_PH_Assets", "not_a_single_select")]
    [InlineData("SELECT 1; SELECT 2", "not_a_single_select")]
    [InlineData("SELECT * FROM dbo.PH_Assets", "unknown_object")]
    [InlineData("SELECT * FROM OtherDb.dbo.PBI_PH_Assets", "cross_database")]
    [InlineData("SELECT OBJECT_ID('dbo.PH_Assets')", "blocked_construct")]
    [InlineData("SELECT * FROM OPENQUERY(LinkedSrv, 'SELECT 1')", "blocked_construct")]
    public void Rejections_carry_a_code_and_a_fix(string sql, string code)
    {
        var error = Validate(sql).Error!;
        Assert.Equal(code, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Fix));
    }
}

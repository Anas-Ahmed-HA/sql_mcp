namespace SqlMcp.Tests;

// Queries the guard must let through.
public partial class SelectQueryGuardTests
{
    [Theory]
    [InlineData("SELECT COUNT(*) FROM dbo.PBI_PH_Assets")]
    [InlineData("select typename, count(*) as n from pbi_ph_assets group by typename order by n desc")]
    [InlineData("SELECT TOP 10 [ID], [Company] FROM [dbo].[PBI_PH_Assets] WHERE Is_Active = 1 ORDER BY ID DESC")]
    [InlineData("SELECT a.Company, e.Name FROM dbo.PBI_PH_Assets AS a JOIN dbo.V_Employee AS e ON e.User_HR_Code = a.Assined_HRCode")]
    [InlineData("WITH x AS (SELECT Company FROM dbo.PBI_PH_Assets) SELECT Company, COUNT(*) FROM x GROUP BY Company")]
    [InlineData("SELECT ID FROM dbo.PBI_PH_Assets WHERE Assined_HRCode IN (SELECT User_HR_Code FROM dbo.V_Employee)")]
    [InlineData("SELECT * FROM dbo.PBI_PH_Assets")]
    [InlineData("SELECT e.* FROM dbo.V_Employee AS e")]
    [InlineData("SELECT 1 AS one")]
    [InlineData("SELECT Company FROM dbo.PBI_PH_Assets -- ; DROP TABLE dbo.PH_Assets")]
    [InlineData("SELECT value FROM STRING_SPLIT('a,b', ',')")]
    [InlineData("SELECT Company, COUNT(*) AS n FROM dbo.PBI_PH_Assets GROUP BY Company HAVING COUNT(*) > 5 ORDER BY n DESC OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY")]
    public void Allows_read_only_selects_on_exposed_objects(string sql)
    {
        var result = Validate(sql);
        Assert.True(result.IsAllowed, result.Error?.Message);
    }

    [Fact]
    public void Reports_the_objects_a_query_reads()
    {
        var result = Validate("SELECT a.ID FROM dbo.PBI_PH_Assets a JOIN V_Employee e ON e.User_HR_Code = a.Assined_HRCode");
        Assert.Equal(["dbo.PBI_PH_Assets", "dbo.V_Employee"], result.Objects.Select(o => o.FullName).Order());
    }

    [Fact]
    public void Treats_an_unqualified_cte_name_as_the_cte_not_a_table()
    {
        Assert.True(Validate("WITH PH_Assets AS (SELECT ID FROM dbo.PBI_PH_Assets) SELECT ID FROM PH_Assets").IsAllowed);
        Assert.False(Validate("WITH PH_Assets AS (SELECT ID FROM dbo.PBI_PH_Assets) SELECT ID FROM dbo.PH_Assets").IsAllowed);
    }
}

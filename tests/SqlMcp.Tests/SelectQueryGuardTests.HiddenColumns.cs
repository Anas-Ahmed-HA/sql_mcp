namespace SqlMcp.Tests;

// Hidden columns: never readable, and rejected with the same wording as a column that doesn't exist.
public partial class SelectQueryGuardTests
{
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
}

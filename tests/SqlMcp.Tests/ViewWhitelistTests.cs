using SqlMcp.Server.Metadata;

namespace SqlMcp.Tests;

/// <summary>The Allowed views whitelist: exact schema-qualified names, and nothing allowed without a list.</summary>
public partial class ViewWhitelistTests
{
    [Fact]
    public void Fails_closed_without_a_list()
    {
        var whitelist = new ViewWhitelist(null);
        Assert.False(whitelist.IsConfigured);
        Assert.False(whitelist.Allows("dbo", "PBI_PH_Assets"));
    }

    [Fact]
    public void An_empty_list_allows_nothing()
    {
        var whitelist = new ViewWhitelist([]);
        Assert.True(whitelist.IsConfigured);
        Assert.False(whitelist.Allows("dbo", "PBI_PH_Assets"));
    }

    [Theory]
    [InlineData("dbo", "PBI_PH_Assets", true)]
    [InlineData("DBO", "pbi_ph_assets", true)]   // case-insensitive, like SQL Server
    [InlineData("hr", "PBI_PH_Assets", false)]   // schema must match
    [InlineData("dbo", "PBI_PH", false)]         // whole names only
    [InlineData("dbo", "V_TWEBWF_Employee", false)]
    public void Allows_only_listed_objects(string schema, string name, bool allowed)
    {
        var whitelist = new ViewWhitelist(["dbo.PBI_PH_Assets", "[sales].[Orders]"]);
        Assert.Equal(allowed, whitelist.Allows(schema, name));
    }

    [Fact]
    public void Accepts_bracketed_names()
    {
        Assert.True(new ViewWhitelist(["[sales].[Orders]"]).Allows("sales", "Orders"));
    }

    [Theory]
    [InlineData("PBI_PH_Assets")]                              // not schema-qualified
    [InlineData("Apps_InformationTechnology.dbo.PBI_PH_Assets")] // database-qualified
    [InlineData("dbo.")]
    public void Reports_entries_that_are_not_schema_qualified_and_allows_nothing_for_them(string entry)
    {
        var whitelist = new ViewWhitelist([entry]);
        Assert.Equal([entry], whitelist.InvalidEntries);
        Assert.False(whitelist.Allows("dbo", "PBI_PH_Assets"));
    }
}

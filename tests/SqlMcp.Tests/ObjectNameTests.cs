using SqlMcp.Server.Catalog;

namespace SqlMcp.Tests;

/// <summary>Parsing and matching object names, the one place the server compares names.</summary>
public partial class ObjectNameTests
{
    [Theory]
    [InlineData("PBI_PH_Assets", null, "PBI_PH_Assets")]
    [InlineData("dbo.PBI_PH_Assets", "dbo", "PBI_PH_Assets")]
    [InlineData("[dbo].[PBI PH Assets]", "dbo", "PBI PH Assets")]
    [InlineData("\"dbo\".\"PBI_PH_Assets\"", "dbo", "PBI_PH_Assets")]
    [InlineData("`dbo.PBI_PH_Assets`", "dbo", "PBI_PH_Assets")]
    [InlineData(" dbo . PBI_PH_Assets ", "dbo", "PBI_PH_Assets")]
    public void Parses_one_and_two_part_names(string text, string? schema, string name)
    {
        Assert.Equal(new ObjectName(schema, name), ObjectName.Parse(text));
    }

    [Theory]
    [InlineData("db.dbo.PBI_PH_Assets")]
    [InlineData("dbo.")]
    [InlineData("[dbo")]
    [InlineData("")]
    public void Rejects_anything_else(string text)
    {
        Assert.Null(ObjectName.Parse(text));
    }

    [Theory]
    [InlineData("dbo", "PBI_PH_Assets", "dbo", "PBI_PH_Assets", true)]
    [InlineData("dbo", "PBI_PH_Assets", "DBO", "pbi_ph_assets", true)]
    [InlineData(null, "PBI_PH_Assets", "hr", "PBI_PH_Assets", true)]  // no schema matches any schema
    [InlineData("dbo", "PBI_PH_Assets", "hr", "PBI_PH_Assets", false)]
    [InlineData("dbo", "PBI_PH", "dbo", "PBI_PH_Assets", false)]
    public void Matches_case_insensitively(string? schema, string name, string otherSchema, string otherName, bool expected)
    {
        Assert.Equal(expected, new ObjectName(schema, name).Matches(otherSchema, otherName));
    }
}

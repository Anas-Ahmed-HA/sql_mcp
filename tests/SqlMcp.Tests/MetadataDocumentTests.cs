using SqlMcp.Server.Metadata;

namespace SqlMcp.Tests;

/// <summary>Splitting business-rules Markdown into sections and finding the section for an object.</summary>
public partial class MetadataDocumentTests
{
    private const string Sample = """
        # Db

        Intro.

        ## Objects

        ### dbo.PBI_PH_Assets

        One row per asset.

        ```sql
        # not a heading
        SELECT 1
        ```

        #### Columns

        - `ID`

        ### `V_TWEBWF_Employee`

        Employees.

        ## Relationships

        Joins.
        """;

    private static readonly MetadataDocument Document = MetadataDocument.Parse("Db", Sample);

    [Fact]
    public void Finds_headings_but_not_lines_inside_code_fences()
    {
        Assert.Equal(
            ["Db", "Objects", "dbo.PBI_PH_Assets", "Columns", "`V_TWEBWF_Employee`", "Relationships"],
            Document.Sections.Select(s => s.Title));
    }

    [Fact]
    public void A_section_runs_until_the_next_heading_of_the_same_or_higher_level()
    {
        var assets = Document.FindObjectSection("dbo", "PBI_PH_Assets")!;

        Assert.StartsWith("### dbo.PBI_PH_Assets", assets.Markdown);
        Assert.Contains("#### Columns", assets.Markdown);        // deeper heading stays inside
        Assert.Contains("# not a heading", assets.Markdown);     // code fence stays inside
        Assert.DoesNotContain("Employees.", assets.Markdown);    // next ### ends it
    }

    [Theory]
    [InlineData("dbo", "PBI_PH_Assets", "### dbo.PBI_PH_Assets")]
    [InlineData("DBO", "pbi_ph_assets", "### dbo.PBI_PH_Assets")]
    [InlineData("dbo", "V_TWEBWF_Employee", "### `V_TWEBWF_Employee`")] // unqualified heading, in backticks
    [InlineData("hr", "V_TWEBWF_Employee", "### `V_TWEBWF_Employee`")]  // unqualified matches any schema
    public void Matches_object_headings_case_insensitively(string schema, string name, string expectedStart)
    {
        Assert.StartsWith(expectedStart, Document.FindObjectSection(schema, name)?.Markdown);
    }

    [Theory]
    [InlineData("hr", "PBI_PH_Assets")] // a schema-qualified heading needs the same schema
    [InlineData("dbo", "PBI_PH")]       // whole names only, no prefix matches
    [InlineData("dbo", "Missing")]
    public void Returns_null_when_no_heading_names_the_object(string schema, string name)
    {
        Assert.Null(Document.FindObjectSection(schema, name));
    }
}

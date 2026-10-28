using SqlMcp.Server.Metadata;

namespace SqlMcp.Tests;

// The Allowed views table and each view's Columns table, in the layout of metadata/guidelines.md.
public partial class MetadataDocumentTests
{
    private const string Standard = """
        # Db

        Purpose.

        ## Allowed views

        | View | Description |
        |---|---|
        | dbo.PBI_PH_Assets | One row per asset |
        | `dbo.V_Projects` | Projects |

        ```markdown
        | View | Description |
        |---|---|
        | dbo.Example_Only | inside a code fence, not part of the whitelist |
        ```

        ## dbo.PBI_PH_Assets

        One row per asset.

        ### Columns

        | Column | Description |
        |:---|---|
        | ID | ID |
        | `AssignType` | `Emp` = employee |

        ### Business rules

        - Count rows.

        ## dbo.V_Projects

        No columns table yet.

        ## Relationships

        None.
        """;

    private static readonly MetadataDocument StandardDocument = MetadataDocument.Parse("Db", Standard);

    [Fact]
    public void Reads_the_allowed_views_table_but_not_tables_in_code_fences()
    {
        Assert.Equal(["dbo.PBI_PH_Assets", "dbo.V_Projects"], StandardDocument.AllowedViews);
    }

    [Fact]
    public void Allowed_views_is_null_without_the_section_and_empty_without_rows()
    {
        Assert.Null(MetadataDocument.Parse("Db", "# Db\n\n## Relationships\n").AllowedViews);
        Assert.Empty(MetadataDocument.Parse("Db", "# Db\n\n## Allowed views\n\nNone yet.\n").AllowedViews!);
    }

    [Fact]
    public void Reads_a_views_columns_table()
    {
        Assert.Equal(["ID", "AssignType"], StandardDocument.ColumnsOf("dbo", "PBI_PH_Assets"));
    }

    [Fact]
    public void Columns_is_null_when_the_view_has_no_section_or_no_columns_table()
    {
        Assert.Null(StandardDocument.ColumnsOf("dbo", "V_Projects"));
        Assert.Null(StandardDocument.ColumnsOf("dbo", "Missing"));
    }
}

using SqlMcp.Server.Configuration;

namespace SqlMcp.Tests;

/// <summary>Database names are also metadata file names, so only file-name-safe names are accepted.</summary>
public partial class DatabaseOptionsTests
{
    [Theory]
    [InlineData("Apps_InformationTechnology")]
    [InlineData("Apps_InformationTechnology_OLD")]
    [InlineData("hr-system.v2")]
    public void Accepts_simple_names(string name) =>
        Assert.True(Options((name, "Server=x;")).IsValid());

    [Theory]
    [InlineData(@"..\secrets")]
    [InlineData("a/b")]
    [InlineData("with space")]
    public void Rejects_names_that_are_not_plain_file_names(string name) =>
        Assert.False(Options((name, "Server=x;")).IsValid());

    [Fact]
    public void Rejects_no_databases_and_empty_connection_strings()
    {
        Assert.False(Options().IsValid());
        Assert.False(Options(("A", "Server=x;"), ("B", " ")).IsValid());
    }

    private static DatabaseOptions Options(params (string Name, string ConnectionString)[] databases)
    {
        var options = new DatabaseOptions();
        foreach (var (name, connectionString) in databases)
        {
            options.ConnectionStrings[name] = connectionString;
        }

        return options;
    }
}

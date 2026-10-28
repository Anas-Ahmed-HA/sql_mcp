using SqlMcp.Server.Catalog;
using SqlMcp.Server.Query;

namespace SqlMcp.Tests;

/// <summary>What SelectQueryGuard allows and rejects. Cases are split by concern across SelectQueryGuardTests.*.cs.</summary>
public partial class SelectQueryGuardTests
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
}

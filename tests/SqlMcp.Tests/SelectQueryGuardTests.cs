using SqlMcp.Server.Catalog;
using SqlMcp.Server.Query;

namespace SqlMcp.Tests;

/// <summary>What SelectQueryGuard allows and rejects. Cases are split by concern across SelectQueryGuardTests.*.cs.</summary>
public partial class SelectQueryGuardTests
{
    // Two exposed (whitelisted) views. Anything else (PH_Assets, sys.*) doesn't exist for agents.
    private static readonly CatalogSnapshot Snapshot = new(
    [
        new CatalogObject(1, "dbo", "PBI_PH_Assets", DbObjectKind.View, null,
            ["ID", "Assined_HRCode", "typename", "Company", "Is_Active"], null),
        new CatalogObject(2, "dbo", "V_Employee", DbObjectKind.View, null,
            ["User_HR_Code", "Name", "Department_ID"], null),
    ]);

    private static GuardResult Validate(string sql) => new SelectQueryGuard().Validate(sql, Snapshot);
}

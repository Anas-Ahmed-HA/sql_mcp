# Apps_InformationTechnology

Business rules and relationships for the `Apps_InformationTechnology` database, for agents querying it
through AI_SqlMCP. This file explains the data; it does not control access.

**How to read the markers:**
- *Measured (date):* counted in the data on that date. Can change as data changes.
- *Unconfirmed:* inferred from names, types or data; the data owner has not confirmed it yet.
- No marker: confirmed by the data owner.

## Purpose

IT asset management for Hassan Allam companies: which IT assets exist (monitors, PCs, laptops, phones,
network gear, …), who or where they're assigned to, plus employee, department and project reference data
and an item-catalog duplicate report.

*Owner: TODO (name / team).*

## Objects

### dbo.PBI_PH_Assets

One row per IT asset, already joined with model, brand, type, company, project, department and assigned
employee names (built for Power BI). 16,348 rows *(measured 2026-10-28)*.

- `ID`: asset identifier.
- `typename`: asset type. Largest groups: Monitor 5,153; PC 4,996; Laptop 3,679; IP Phone 680; Printer 416
  *(measured 2026-10-28)*. 65 rows have an empty type.
- `AssignType`: what the asset is assigned to. See *Codes and values*.
- `Assined_HRCode` (sic, misspelled in the source): HR code of the assigned employee, when `AssignType = 'Emp'`.
- `Empname`, `Department_Name`, `ProjectName`, `Company`: names already resolved for reporting, so for most
  questions no join is needed.
- `Is_Active`: every row is currently 1 *(measured 2026-10-28)*, so it doesn't separate active from retired
  assets today. *Unconfirmed:* whether retired assets are removed from the view rather than flagged.
- `Unit_Price`, `Unit_Tax`, `Unit_TotalPrice`: often empty. Don't sum them as asset value without checking
  how many are filled.
- `Warranty`: warranty end date. `AssinDate` (sic): assignment date.

### dbo.V_TWEBWF_Employee

One row per employee record: 28,470 rows *(measured 2026-10-28)*. Key: `User_HR_Code`.
Contains personal data; see *Sensitive data*.

- `IsActive`, `Status`, `Account_Status`: employment / account state. *Unconfirmed:* which one defines a
  current employee. Use it as a filter when counting current staff.
- `Department_ID`, `Project_ID`, `Location_ID`, `Manager_Code`: references, see *Relationships*.

### dbo.V_TWEBWF_Department

One row per department: 44 rows *(measured 2026-10-28)*. Key: `Department_ID`. `ISProject` marks
departments that are projects *(unconfirmed meaning)*.

### dbo.V_TWEBWF_Projects

One row per project: 1,004 rows *(measured 2026-10-28)*. Key: `Project_ID` (int).

### dbo.ItmCat_Duplicate_Item_Code

Item-catalog cleanup report: pairs of catalog items sharing an `Item_Code`, with the kept item and its
duplicate. 142 rows *(measured 2026-10-28)*. `Same_Name` = 1 when both items also have the same name
*(unconfirmed)*. Not related to the asset views.

## Relationships

SQL Server views carry no foreign keys, so the joins below are the reference.

| From | To | Notes |
|---|---|---|
| `PBI_PH_Assets.Assined_HRCode` | `V_TWEBWF_Employee.User_HR_Code` | All 9,598 assets with `AssignType = 'Emp'` match an employee *(measured 2026-10-28)* |
| `V_TWEBWF_Employee.Department_ID` | `V_TWEBWF_Department.Department_ID` | *Unconfirmed*, both `nvarchar(20)` |
| `V_TWEBWF_Employee.Project_ID` | `V_TWEBWF_Projects.Project_ID` | *Unconfirmed*: `nvarchar(20)` vs `int`, so cast before joining |
| `V_TWEBWF_Employee.Manager_Code` | `V_TWEBWF_Employee.User_HR_Code` | *Unconfirmed*, manager hierarchy |

`PBI_PH_Assets` already has `Department_Name` and `ProjectName` as text. Prefer those over joining through
the employee view, which gives the employee's *current* department rather than the one the asset is
recorded under.

## Business rules

- Asset counts are per row of `PBI_PH_Assets`: one row is one physical asset.
- Asset ownership comes from `AssignType`: `Emp` means assigned to the person in `Assined_HRCode`, `Loc` means
  assigned to a location, not a person.
- TODO (data owner): rules for counting current employees, retired assets, and which company an asset belongs to.

## Codes and values

| Column | Value | Meaning | Rows *(measured 2026-10-28)* |
|---|---|---|---|
| `PBI_PH_Assets.AssignType` | `Emp` | Assigned to an employee | 9,598 |
| | `Loc` | Assigned to a location | 6,685 |
| | *(empty)* | Not recorded | 65 |
| `PBI_PH_Assets.Isstore` | `0` / empty | *Unconfirmed:* 1 would mean the asset is in a store room | 1,230 / 15,118 |

## Sensitive data

`V_TWEBWF_Employee` holds personal data: national ID, passport, visa and residence numbers, bank account,
payroll password, birthdate, personal phone numbers, emails and home address. These columns are currently
**not** hidden by the server. Don't return them unless the question needs them, and prefer counts over row lists.

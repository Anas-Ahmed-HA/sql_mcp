# Apps_InformationTechnology

IT asset management for Hassan Allam companies: which IT assets exist (monitors, PCs, laptops, phones,
network gear, …) and who or where they're assigned to.

## Allowed views

| View | Description |
|---|---|
| dbo.PBI_PH_Assets | One row per IT asset, with model, brand, type, company, project, department and assigned employee already resolved |

## dbo.PBI_PH_Assets

One row is one physical IT asset. Built for Power BI, so names are already joined in. 16,348 rows as of
2026-10-28.

### Columns

| Column | Description |
|---|---|
| ID | Asset ID, unique per row |
| DeliveryDet_ID | Delivery line the asset arrived on (unconfirmed) |
| Delivery_ID | Delivery the asset arrived on (unconfirmed) |
| PO_No | Purchase order number (unconfirmed) |
| Location_ID | Location the asset is assigned to, when `AssignType = 'Loc'` (unconfirmed) |
| Assined_HRCode | HR code of the employee the asset is assigned to, when `AssignType = 'Emp'`. Name misspelled in the source |
| Warranty | Warranty end date |
| AssinDate | Date the asset was assigned. Name misspelled in the source |
| BarCodeNo | Numeric part of the barcode (unconfirmed) |
| BarCode | Asset barcode label, e.g. `HACIP0294` |
| SerialNo | Manufacturer serial number |
| Model_ID | Model ID; the name is in `Modelname` |
| Unit_Price | Purchase price of this one asset, probably before tax (unconfirmed); empty on most rows |
| Unit_Tax | Tax per unit; empty on every row as of 2026-10-28 |
| Unit_TotalPrice | Total of the whole delivery line (`Unit_Price` × quantity ordered), repeated on every asset of that line. Not a per-unit price |
| DescGroup_ID | Description group ID (unconfirmed) |
| Comment | Free-text comment |
| Is_Active | Active flag; 1 on every row as of 2026-10-28 |
| In_User | User who created the record |
| In_Date | When the record was created |
| Up_User | User who last updated the record |
| Up_Date | When the record was last updated |
| AssignType | What the asset is assigned to: `Emp` = an employee, `Loc` = a location, empty = not recorded |
| Modelname | Model name, e.g. `CP-7841` |
| Brandname | Brand name, e.g. `CISCO`, `Dell`, `HP` |
| Company | Company the asset belongs to (unconfirmed: owning company or the assigned employee's company) |
| Shortcut | Company short code (unconfirmed) |
| Empname | Name of the assigned employee |
| typename | Asset type: `Monitor`, `PC`, `Laptop`, `IP Phone`, `Printer`, `Access Point`, `Switch`, `Server`, … |
| ProjectName | Project the asset is recorded under |
| Department_Name | Department the asset is recorded under |
| Isstore | Whether the asset is in a store room (unconfirmed); 0 or empty on every row as of 2026-10-28 |

### Business rules

- Count assets as rows: one row is one physical asset.
- `Is_Active` is 1 on every row, so it doesn't separate active from retired assets. Don't filter on it.
- `AssignType` decides who has the asset. `Emp`: the employee in `Assined_HRCode` / `Empname`. `Loc`: a location,
  with no person. As of 2026-10-28: 9,598 `Emp`, 6,685 `Loc`, 65 empty.
- For asset value, sum `Unit_Price`. Never sum `Unit_TotalPrice`: it repeats the line total on every asset, so
  a line of 190 PCs counts its total 190 times.
- Prices are on 1,155 rows (13 delivery lines, all 2025–2026) as of 2026-10-28. Always say how many rows have a
  price when reporting a value.
- Prices mix currencies and there is no currency column. Some lines are in the tens of thousands per PC, others
  in the hundreds (unconfirmed: EGP and USD). Report value per `DeliveryDet_ID` or price range; don't add the two
  together.
- Group by `typename`, `Company`, `ProjectName` or `Department_Name` directly; no joins are needed.

### Notes

- `Empname` and `Assined_HRCode` identify people. Prefer counts over lists of named employees.
- 65 rows have an empty `typename`.
- Many delivery lines have fewer assets recorded than were ordered (e.g. 163 of 218 laptops), so
  `Unit_TotalPrice ÷ Unit_Price` can be larger than the line's row count.

## Relationships

None. `dbo.PBI_PH_Assets` is the only allowed view.

## Additional metadata

- Owner: TODO (name / team).
- Refresh schedule: TODO.

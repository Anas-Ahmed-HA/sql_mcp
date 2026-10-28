# Metadata file guidelines

Every database connection has one Markdown file in this folder. Agents read it (`get_business_rules`,
and their own view's section in `describe_object`), and the server enforces its **Allowed views** list.
Follow this standard exactly: the server finds sections by their headings.

## File

- **Name:** `<ConnectionName>.md`, where `<ConnectionName>` is the key under `Database:ConnectionStrings`.
  Example: `Apps_InformationTechnology.md`.
- **Sections, in this order, with headings written exactly like this:**

| # | Heading | Required | Content |
|---|---|---|---|
| 1 | `# <ConnectionName>` | yes | One or two sentences: what this database is for |
| 2 | `## Allowed views` | yes | The views agents may use (enforced) |
| 3 | `## <schema>.<view>` | yes, one per allowed view | Same order as the Allowed views table |
| 3a | `### Columns` | yes | Every column of the view, with a description |
| 3b | `### Business rules` | no | Rules for using this view |
| 3c | `### Notes` | no | Any other metadata about this view |
| 4 | `## Relationships` | yes | Joins between views |
| 5 | `## Additional metadata` | no | Anything that applies to all views |

## Allowed views (enforced)

```markdown
## Allowed views

| View | Description |
|---|---|
| dbo.PBI_PH_Assets | One row per IT asset |
```

- **This is a whitelist.** The server exposes only the views in this table. Every tool treats any other view
  as if it doesn't exist.
- **It's the only access control** in the server. A listed view must also be visible to the database login.
  Every column of a listed view can be queried; to keep a column away from agents, whitelist a view (created in
  the database) that doesn't select it.
- **Use exact, schema-qualified names:** `dbo.PBI_PH_Assets`, not `PBI_PH_Assets`. The server logs a warning
  for any name that doesn't match a view.
- **It fails closed.** If the file is missing, can't be read, or has no Allowed views table, no views are
  exposed.
- **This file controls access.** Review changes to it like configuration changes. The server re-reads the
  deployed copy on every call, so whoever can edit that copy decides what agents can see. A build replaces the
  deployed copy only when the repo copy is newer.

## View sections

One `## <schema>.<view>` section per allowed view, named exactly as in the Allowed views table. Start with one
sentence saying what one row represents.

### Columns

```markdown
### Columns

| Column | Description |
|---|---|
| ID | ID |
| AssignType | Who the asset is assigned to: `Emp` = employee, `Loc` = location |
```

- List **every column** of the view, in the view's column order, so the server can check the file is complete.
- **Obvious columns:** the description can be the column name itself (`| ID | ID |`).
- **Code values** go in the description: `` `Emp` = employee, `Loc` = location ``.
- **Don't add data types.** Agents get those from the database through `describe_object`.
- The server logs a warning for columns the table lists that don't exist, and for columns it leaves out.

### Business rules

Optional. A bullet list of rules for using this view: how to count, which filters always apply, which values
to ignore.

```markdown
### Business rules

- Count assets as rows; one row is one physical asset.
- `Is_Active` is 1 on every row; don't use it to filter.
```

### Notes

Optional. Any other metadata about this view only: known data issues, refresh timing, where the data comes from.

## Relationships

```markdown
## Relationships

| From | To | Cardinality | Notes |
|---|---|---|---|
| dbo.PBI_PH_Assets.Assined_HRCode | dbo.V_TWEBWF_Employee.User_HR_Code | many-to-one | Only when `AssignType = 'Emp'` |
```

- List the joins between views. SQL Server views have no foreign keys, so every join an agent should know
  about goes here.
- **From / To:** `<schema>.<view>.<column>`. For a join on several columns, list them comma-separated in the
  same order on both sides.
- **Cardinality:** `one-to-one`, `one-to-many`, `many-to-one` or `many-to-many`, read from From to To.
- Both views must be in the Allowed views table.
- If there are no joins, write `None.` under the heading.

## Additional metadata

Optional. Facts that apply to all views or span several of them: data owner, refresh schedule, time zone,
currency, which data is personal or sensitive, rules involving more than one view.

## Writing conventions

- Mark anything the data owner hasn't confirmed with `(unconfirmed)` at the end of the sentence or cell.
- Put names and values in backticks in prose: `` `AssignType` ``, `` `'Emp'` ``.
- Keep it short. Agents read the whole file before querying, so every line costs them context.

## Template

```markdown
# <ConnectionName>

<What this database is for, in one or two sentences.>

## Allowed views

| View | Description |
|---|---|
| <schema>.<view> | <What one row is> |

## <schema>.<view>

<What one row represents.>

### Columns

| Column | Description |
|---|---|
| <Column> | <Description, or the column name if obvious> |

### Business rules

- <Rule>

### Notes

- <Note>

## Relationships

| From | To | Cardinality | Notes |
|---|---|---|---|
| <schema>.<view>.<column> | <schema>.<view>.<column> | many-to-one | <Condition or caveat> |

## Additional metadata

- <Fact that applies to all views>
```

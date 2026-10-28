namespace SqlMcp.Server.Catalog;

// SQL text run against SQL Server's catalog views.
public sealed partial class DatabaseCatalog
{
    // Two result sets: objects, then the column names of every object.
    private const string ObjectsSql = """
        SELECT
            o.object_id,
            s.name,
            o.name,
            CASE o.type WHEN 'U' THEN 0 ELSE 1 END AS Kind,
            CAST(ep.value AS nvarchar(4000))       AS Description,
            CASE WHEN o.type = 'U' THEN
                (SELECT SUM(p.rows) FROM sys.partitions AS p WHERE p.object_id = o.object_id AND p.index_id IN (0, 1))
            END                                    AS ApproxRowCount
        FROM sys.objects AS o
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        LEFT JOIN sys.extended_properties AS ep
            ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
        WHERE o.type IN ('U', 'V')
          AND o.is_ms_shipped = 0
          AND o.name <> N'sysdiagrams'
        ORDER BY s.name, o.name;

        SELECT c.object_id, c.name
        FROM sys.columns AS c
        JOIN sys.objects AS o ON o.object_id = c.object_id
        WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
        ORDER BY c.object_id, c.column_id;
        """;

    // One round trip, five result sets: columns, unique keys, check constraints, foreign keys, dependencies.
    private const string DescribeSql = """
        SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale,
               c.is_nullable, c.is_identity, c.is_computed,
               dc.definition, cc.definition, CAST(ep.value AS nvarchar(4000))
        FROM sys.columns AS c
        LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
        LEFT JOIN sys.computed_columns AS cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.extended_properties AS ep
            ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = N'MS_Description'
        WHERE c.object_id = @id
        ORDER BY c.column_id;

        SELECT i.name, i.is_primary_key, col.name
        FROM sys.indexes AS i
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
        JOIN sys.columns AS col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
        WHERE i.object_id = @id AND i.is_unique = 1 AND i.is_hypothetical = 0
        ORDER BY i.is_primary_key DESC, i.name, ic.key_ordinal;

        SELECT ck.name, col.name, ck.definition, ck.is_disabled
        FROM sys.check_constraints AS ck
        LEFT JOIN sys.columns AS col ON col.object_id = ck.parent_object_id AND col.column_id = ck.parent_column_id
        WHERE ck.parent_object_id = @id
        ORDER BY ck.name;

        SELECT fk.name,
               fk.parent_object_id, ps.name + N'.' + po.name, pc.name,
               fk.referenced_object_id, rs.name + N'.' + ro.name, rc.name,
               fk.delete_referential_action_desc, fk.update_referential_action_desc, fk.is_disabled
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.objects AS po ON po.object_id = fk.parent_object_id
        JOIN sys.schemas AS ps ON ps.schema_id = po.schema_id
        JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.objects AS ro ON ro.object_id = fk.referenced_object_id
        JOIN sys.schemas AS rs ON rs.schema_id = ro.schema_id
        JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = @id OR fk.referenced_object_id = @id
        ORDER BY fk.name, fkc.constraint_column_id;

        SELECT DISTINCT d.referenced_id
        FROM sys.sql_expression_dependencies AS d
        WHERE d.referencing_id = @id AND d.referenced_id IS NOT NULL;
        """;
}

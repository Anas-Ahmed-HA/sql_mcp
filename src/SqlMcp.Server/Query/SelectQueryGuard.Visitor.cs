using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlMcp.Server.Catalog;
using SqlMcp.Server.Errors;

namespace SqlMcp.Server.Query;

// The syntax-tree walk: every table source must be an exposed object, and no side channels are allowed.
public sealed partial class SelectQueryGuard
{
    private sealed partial class Visitor(CatalogSnapshot snapshot, HashSet<string> cteNames) : TSqlFragmentVisitor
    {
        public AgentError? Error { get; private set; }

        public List<CatalogObject> Objects { get; } = [];

        private void Fail(AgentError error) => Error ??= error;

        public override void Visit(TableReference node)
        {
            if (!AllowedTableReferences.Contains(node.GetType()))
            {
                Fail(AgentErrors.BlockedConstruct(
                    $"{node.GetType().Name.Replace("TableReference", string.Empty)} is not allowed as a table source."));
            }
        }

        public override void Visit(NamedTableReference node)
        {
            var name = node.SchemaObject;
            var display = string.Join('.', name.Identifiers.Select(i => i.Value));

            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            {
                Fail(AgentErrors.CrossDatabase(display));
                return;
            }

            var schema = name.SchemaIdentifier?.Value;
            var objectName = name.BaseIdentifier.Value;
            if (schema is null && cteNames.Contains(objectName))
            {
                return;
            }

            var resolution = snapshot.Resolve(new ObjectName(schema, objectName));
            if (resolution.Match is { } obj)
            {
                Objects.Add(obj);
            }
            else if (resolution.Candidates.Count > 1)
            {
                Fail(AgentErrors.AmbiguousObject(display, resolution.Candidates.Select(c => c.FullName)));
            }
            else
            {
                // Same wording as SQL Server, so an object that isn't allowed is indistinguishable from a missing one.
                Fail(AgentErrors.UnknownObject(display));
            }
        }

        public override void Visit(FunctionCall node)
        {
            if (node.CallTarget is not null)
            {
                Fail(AgentErrors.BlockedConstruct(
                    $"Calling '{node.FunctionName.Value}' on a schema or object is not allowed (user-defined functions and methods are blocked)."));
            }
            else if (BlockedFunctions.Contains(node.FunctionName.Value))
            {
                Fail(AgentErrors.BlockedConstruct($"The metadata function {node.FunctionName.Value.ToUpperInvariant()} is not allowed."));
            }
        }

        public override void Visit(SelectSetVariable node) => Fail(AgentErrors.BlockedConstruct("Assigning to variables is not allowed."));

        public override void Visit(NextValueForExpression node) => Fail(AgentErrors.BlockedConstruct("NEXT VALUE FOR is not allowed (it changes a sequence)."));
    }
}

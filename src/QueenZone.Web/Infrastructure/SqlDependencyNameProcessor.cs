using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace QueenZone.Web;

internal sealed class SqlDependencyNameProcessor : BaseProcessor<Activity>
{
    private const string Identifier = @"(?:\[\w[\w$#]*\]|[\p{L}_#][\w$#]*)(?:\s*\.\s*(?:\[\w[\w$#]*\]|[\p{L}_#][\w$#]*))*";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private static readonly Regex BareIdentifier = new(@"^\s*(?<name>" + Identifier + @")\s*;?\s*$", Options);
    private static readonly Regex Execute = new(@"^\s*EXEC(?:UTE)?\s+(?:@\w+\s*=\s*)?(?<name>" + Identifier + @")(?:\s|;|$)", Options);
    private static readonly Regex Operation = new(@"^\s*(?<operation>SELECT|INSERT|UPDATE|DELETE|MERGE)\b", Options);
    private static readonly Regex Table = new(@"\b(?:FROM|INTO|UPDATE)\s+(?<name>" + Identifier + @")", Options);
    private static readonly Regex LiteralsAndComments = new(@"'(?:''|[^'])*'|--[^\r\n]*|/\*[\s\S]*?\*/", Options);
    private static readonly Regex IdentifierWhitespace = new(@"\s+", Options);

    internal static TracerProviderBuilder AddTo(TracerProviderBuilder builder) =>
        builder.AddProcessor(new SqlDependencyNameProcessor());

    public override void OnEnd(Activity data)
    {
        if (data.Kind != ActivityKind.Client
            || (data.GetTagItem("db.system.name") is not "microsoft.sql_server"
                && data.GetTagItem("db.system") is not "mssql"))
        {
            return;
        }

        var name = data.GetTagItem("db.stored_procedure.name") is string procedure
            ? GetIdentifier(BareIdentifier.Match(procedure))
            : GetCommandName(data.GetTagItem("db.query.text") as string ?? data.GetTagItem("db.statement") as string);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        name = name.Length > 128 ? name[..128] : name;
        data.DisplayName = name;
        if (data.GetTagItem("db.operation.name") is null)
        {
            data.SetTag("db.operation.name", name);
        }
    }

    private static string? GetCommandName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        // Mask values and comments so a FROM inside either cannot become a table name.
        command = LiteralsAndComments.Replace(command, "?");
        var procedure = GetIdentifier(Execute.Match(command));
        if (procedure is not null)
        {
            return procedure;
        }

        var operation = Operation.Match(command);
        if (!operation.Success)
        {
            return GetIdentifier(BareIdentifier.Match(command));
        }

        var keyword = operation.Groups["operation"].Value.ToUpperInvariant();
        var table = GetIdentifier(Table.Match(command));
        return table is null ? keyword : $"{keyword} {table}";
    }

    private static string? GetIdentifier(Match match) => match.Success
        ? IdentifierWhitespace.Replace(match.Groups["name"].Value.Replace("[", "").Replace("]", ""), "")
        : null;
}

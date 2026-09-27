using QueenZone.Data;
using QueenZone.Tools;

// Regex reads REGEX_DEFAULT_MATCH_TIMEOUT once in its static constructor.
RegexDefaults.ApplyProcessDefault();
return await ToolsApp.RunAsync(args);

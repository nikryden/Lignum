using System.Text.Json;
using System.Text.Json.Serialization;
using Lignum.Core.Syntax;

namespace Lignum.App.Settings;

/// <summary>
/// The contents of <c>syntax-schemes.json</c> (ticket 25). <see cref="Schemes"/> is null on first run;
/// <see cref="ViewModels.SyntaxViewModel"/> then fills in <see cref="BuiltInSyntaxSchemes"/>.
/// Import and export use the same shape.
/// </summary>
public sealed class SyntaxSchemesFile
{
    public const string FileName = "syntax-schemes.json";

    public List<SyntaxScheme>? Schemes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

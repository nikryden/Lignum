using Lignum.Core;

namespace Lignum.App.Viewer;

/// <summary>A query's result set as the rows of a viewer: row i shows the i-th matching file line.</summary>
public sealed class ResultLines : ILineList
{
    private readonly QueryResult _result;

    public ResultLines(QueryResult result)
    {
        _result = result;
        result.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public long Count => _result.Count;

    public long this[long row] => _result[row];

    public event EventHandler? Changed;
}

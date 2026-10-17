namespace Lignum.App.ViewModels;

// Find all and the results pane for this tab's document (ticket 10).
public sealed partial class LogTabViewModel
{
    private SearchViewModel? _search;

    /// <summary>The find box, the result set shown in the results pane and F3 navigation.</summary>
    public SearchViewModel Search => _search ??= new SearchViewModel(_uiContext) { History = History };

    /// <summary>The app-wide find / filter history; null in isolation.</summary>
    public SearchHistory? History => _services.History;
}

using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Api.Catalogue;

/// <summary>
/// Every listed entry's facet values, held in memory and read again every <see cref="Every"/>. A list
/// that narrows nothing but the filter drawer's picks names every entry before its picks, and its
/// facets are counted from these rather than from a scan of the whole catalogue per request; any
/// other list's are counted from the titles it names. A catalogue that cannot be read leaves the last
/// rows held.
/// </summary>
public sealed class FacetIndex(CatalogueQueries queries, CatalogueState state, TimeProvider clock, ILogger<FacetIndex> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);
    private IReadOnlyList<FacetRow>? _rows;

    public Facets Facets(TitleQuery query) => CatalogueQueries.Count(query, query.Narrows ? queries.FacetRows(query) : Rows());

    private IReadOnlyList<FacetRow> Rows() => _rows ?? Refresh();

    private IReadOnlyList<FacetRow> Refresh()
    {
        var rows = queries.FacetRows(new TitleQuery());
        _rows = rows;
        return rows;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (state.Problem() is null)
                {
                    try
                    {
                        Refresh();
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex)
                    {
                        logger.LogWarning("The catalogue's facets could not be read: {Reason}", ex.Message);
                    }
                }
                await Task.Delay(Every, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

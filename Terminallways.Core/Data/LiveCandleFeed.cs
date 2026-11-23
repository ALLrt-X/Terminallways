using System.Diagnostics;
using Terminallways.Core.Models;

namespace Terminallways.Core.Data;

public record TailUpdateResult(int FetchedCount, int AddedCount, bool UpdatedLast, long ElapsedMs);

public class LiveCandleFeed
{
    private readonly IMarketDataProvider _provider;
    private readonly List<Candle> _candles = new();
    private bool _isUpdating;

    public string Ticker { get; private set; } = string.Empty;
    public int Interval { get; private set; } = 1;
    public DateTime AnchorFrom { get; private set; }
    public IReadOnlyList<Candle> Candles => _candles;

    public LiveCandleFeed(IMarketDataProvider provider)
    {
        _provider = provider;
    }

    public async Task<int> InitializeAsync(string ticker, int daysBack = 1, int interval = 1)
    {
        Ticker = ticker;
        Interval = interval;
        AnchorFrom = DateTime.Today.AddDays(-Math.Max(1, daysBack));

        var loaded = await _provider.GetCandlesAsync(Ticker, AnchorFrom, null, Interval);
        _candles.Clear();
        _candles.AddRange(loaded);

        return _candles.Count;
    }

    public async Task<TailUpdateResult?> UpdateTailAsync(int overlap = 2)
    {
        if (_isUpdating || _candles.Count == 0)
            return null;

        _isUpdating = true;
        var sw = Stopwatch.StartNew();

        try
        {
            int startOffset = Math.Max(0, _candles.Count - overlap);
            var tail = await _provider.GetTailCandlesAsync(Ticker, AnchorFrom, startOffset, Interval);

            int added = 0;
            bool updatedLast = false;

            foreach (var fresh in tail)
            {
                int existingIdx = _candles.FindLastIndex(c => c.Begin == fresh.Begin);
                if (existingIdx >= 0)
                {
                    if (_candles[existingIdx] != fresh)
                    {
                        _candles[existingIdx] = fresh;
                        if (existingIdx == _candles.Count - 1)
                            updatedLast = true;
                    }
                }
                else if (fresh.Begin > _candles[^1].Begin)
                {
                    _candles.Add(fresh);
                    added++;
                }
            }

            sw.Stop();
            return new TailUpdateResult(tail.Count, added, updatedLast, sw.ElapsedMilliseconds);
        }
        finally
        {
            _isUpdating = false;
        }
    }
}
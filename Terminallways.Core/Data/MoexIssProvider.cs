// Работа с iss.moex.com (история свечей, быстрый хвост и инфо о бумаге)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Terminallways.Core.Models;

namespace Terminallways.Core.Data
{
    public class MoexIssProvider : IMarketDataProvider
    {
        private const int MoexPageSize = 500;

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public string ProviderName => "MOEX ISS (TQBR)";

        public async Task<List<Candle>> GetCandlesAsync(
            string ticker,
            DateTime from,
            DateTime? till = null,
            int interval = 1,
            CancellationToken ct = default)
        {
            var result = new List<Candle>();
            int start = 0;

            string fromStr = from.ToString("yyyy-MM-dd");
            bool useTill = till.HasValue && till.Value.Date < DateTime.Today;
            string tillPart = useTill ? $"&till={till!.Value:yyyy-MM-dd}" : string.Empty;
            string cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            while (true)
            {
                string url =
                    $"https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/candles.json" +
                    $"?interval={interval}&from={fromStr}{tillPart}&start={start}&iss.meta=off&_={cacheBuster}";

                string json = await Http.GetStringAsync(url, ct);
                var page = ParseCandlesPage(json);

                if (page.Count == 0)
                    break;

                result.AddRange(page);

                // Если пришло меньше 500 свечей — это последняя страница, лишний запрос не нужен
                if (page.Count < MoexPageSize)
                    break;

                start += page.Count;
            }

            return result;
        }

        public async Task<List<Candle>> GetTailCandlesAsync(
            string ticker,
            DateTime anchorFrom,
            int startOffset,
            int interval = 1,
            CancellationToken ct = default)
        {
            var result = new List<Candle>();
            int start = Math.Max(0, startOffset);

            string fromStr = anchorFrom.ToString("yyyy-MM-dd");
            string cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            while (true)
            {
                string url =
                    $"https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities/{ticker}/candles.json" +
                    $"?interval={interval}&from={fromStr}&start={start}&iss.meta=off&_={cacheBuster}";

                string json = await Http.GetStringAsync(url, ct);
                var page = ParseCandlesPage(json);

                if (page.Count == 0)
                    break;

                result.AddRange(page);

                if (page.Count < MoexPageSize)
                    break;

                start += page.Count;
            }

            return result;
        }

        public async Task<SecurityInfo?> GetSecurityInfoAsync(string ticker, CancellationToken ct = default)
        {
            string url =
                $"https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json" +
                $"?iss.meta=off&iss.only=securities,marketdata";

            string json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var secObj = doc.RootElement.GetProperty("securities");
            var secCols = secObj.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();
            var secRows = secObj.GetProperty("data").EnumerateArray().ToList();

            if (secRows.Count == 0)
                return null;

            var secRow = secRows[0];
            int lotSize = secRow[secCols.IndexOf("LOTSIZE")].GetInt32();
            double minStep = secRow[secCols.IndexOf("MINSTEP")].GetDouble();

            var mdObj = doc.RootElement.GetProperty("marketdata");
            var mdCols = mdObj.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();
            var mdRows = mdObj.GetProperty("data").EnumerateArray().ToList();

            double? lastPrice = null;
            string updateTime = "—";

            if (mdRows.Count > 0)
            {
                var mdRow = mdRows[0];

                var lastEl = mdRow[mdCols.IndexOf("LAST")];
                if (lastEl.ValueKind == JsonValueKind.Number)
                    lastPrice = lastEl.GetDouble();

                var timeEl = mdRow[mdCols.IndexOf("UPDATETIME")];
                if (timeEl.ValueKind == JsonValueKind.String)
                    updateTime = timeEl.GetString() ?? "—";
            }

            return new SecurityInfo(ticker, lotSize, minStep, lastPrice, updateTime);
        }

        private static List<Candle> ParseCandlesPage(string json)
        {
            var page = new List<Candle>();
            using var doc = JsonDocument.Parse(json);

            var candlesObj = doc.RootElement.GetProperty("candles");
            var cols = candlesObj.GetProperty("columns")
                .EnumerateArray()
                .Select(c => c.GetString()!)
                .ToList();

            int iOpen = cols.IndexOf("open");
            int iHigh = cols.IndexOf("high");
            int iLow = cols.IndexOf("low");
            int iClose = cols.IndexOf("close");
            int iVol = cols.IndexOf("volume");
            int iBegin = cols.IndexOf("begin");
            int iEnd = cols.IndexOf("end");

            foreach (var row in candlesObj.GetProperty("data").EnumerateArray())
            {
                page.Add(new Candle(
                    Begin: DateTime.Parse(row[iBegin].GetString()!, CultureInfo.InvariantCulture),
                    End: DateTime.Parse(row[iEnd].GetString()!, CultureInfo.InvariantCulture),
                    Open: row[iOpen].GetDouble(),
                    High: row[iHigh].GetDouble(),
                    Low: row[iLow].GetDouble(),
                    Close: row[iClose].GetDouble(),
                    Volume: row[iVol].GetDouble()
                ));
            }

            return page;
        }

        public async Task<string?> GetTradingStatusAsync(string ticker, CancellationToken ct = default)
        {
            string url =
                $"https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities/{ticker}.json" +
                $"?iss.meta=off&iss.only=marketdata&marketdata.columns=TRADINGSTATUS";

            string json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var data = doc.RootElement.GetProperty("marketdata").GetProperty("data");
            if (data.GetArrayLength() == 0)
                return null;

            var cell = data[0][0];
            return cell.ValueKind == JsonValueKind.String ? cell.GetString() : null;
        }

    }
}
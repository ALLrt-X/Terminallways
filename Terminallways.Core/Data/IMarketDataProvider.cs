// Общий интерфейс (чтобы позже легко добавить API брокера или QUIK)

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terminallways.Core.Models;

namespace Terminallways.Core.Data
{
    public interface IMarketDataProvider
    {
        string ProviderName { get; }

        /// <summary>
        /// Загружает историю свечей за период.
        /// Если till не указан (null) или равен сегодняшней дате, грузит до самой актуальной свечи.
        /// </summary>
        Task<List<Candle>> GetCandlesAsync(
            string ticker,
            DateTime from,
            DateTime? till = null,
            int interval = 1,
            CancellationToken ct = default
        );

        /// <summary>
        /// Быстро догружает только хвост свечей начиная со смещения startOffset от якорной даты anchorFrom.
        /// </summary>
        Task<List<Candle>> GetTailCandlesAsync(
            string ticker,
            DateTime anchorFrom,
            int startOffset,
            int interval = 1,
            CancellationToken ct = default
        );

        Task<SecurityInfo?> GetSecurityInfoAsync(string ticker, CancellationToken ct = default);

        Task<string?> GetTradingStatusAsync(string ticker, CancellationToken ct = default);
    }
}
namespace Terminallways.Core.Models;

public record TimeframeSpec(
    string Code,
    int? HistoryMoexInterval, // null = только из 1m-базы; 60 / 24 = история с MOEX + склейка живого 1m хвоста
    int HistoryDaysBack,      // глубина истории (если HistoryMoexInterval != null)
    int ResampleMinutes,      // 1..60, 1440 для 1D
    bool IsDailyOrHigher
)
{
    public static readonly IReadOnlyDictionary<string, TimeframeSpec> Presets =
        new Dictionary<string, TimeframeSpec>
        {
            ["1M"] = new("1M", HistoryMoexInterval: null, HistoryDaysBack: 0, ResampleMinutes: 1, IsDailyOrHigher: false),
            ["2M"] = new("2M", HistoryMoexInterval: null, HistoryDaysBack: 0, ResampleMinutes: 2, IsDailyOrHigher: false),
            ["3M"] = new("3M", HistoryMoexInterval: null, HistoryDaysBack: 0, ResampleMinutes: 3, IsDailyOrHigher: false),
            ["5M"] = new("5M", HistoryMoexInterval: null, HistoryDaysBack: 0, ResampleMinutes: 5, IsDailyOrHigher: false),
            ["15M"] = new("15M", HistoryMoexInterval: null, HistoryDaysBack: 0, ResampleMinutes: 15, IsDailyOrHigher: false),
            // Если позже захочешь 3-4 недели истории на 30M — просто поставь HistoryMoexInterval: 10, HistoryDaysBack: 25
            ["30M"] = new("30M", HistoryMoexInterval: null, HistoryDaysBack: 0, ResampleMinutes: 30, IsDailyOrHigher: false),
            ["1H"] = new("1H", HistoryMoexInterval: 60, HistoryDaysBack: 60, ResampleMinutes: 60, IsDailyOrHigher: false),
            ["1D"] = new("1D", HistoryMoexInterval: 24, HistoryDaysBack: 365, ResampleMinutes: 1440, IsDailyOrHigher: true),
        };
}
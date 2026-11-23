namespace Terminallways.Core.Models;

public record TimeframeSpec(
    string Code,
    int BaseMoexInterval,   // Какой интервал качать с MOEX (1, 60, 24, 7)
    int DaysBack,           // Сколько дней истории грузить
    int ResampleMinutes,    // Во сколько минут склеивать (1 = не склеивать)
    bool IsDailyOrHigher    // Скрывать ли разделители дней и время HH:mm на оси X
)
{
    public static readonly IReadOnlyDictionary<string, TimeframeSpec> Presets =
        new Dictionary<string, TimeframeSpec>
        {
            ["1M"] = new("1M", BaseMoexInterval: 1, DaysBack: 5, ResampleMinutes: 1, IsDailyOrHigher: false),
            ["2M"] = new("2M", BaseMoexInterval: 1, DaysBack: 5, ResampleMinutes: 2, IsDailyOrHigher: false),
            ["3M"] = new("3M", BaseMoexInterval: 1, DaysBack: 5, ResampleMinutes: 3, IsDailyOrHigher: false),
            ["5M"] = new("5M", BaseMoexInterval: 1, DaysBack: 5, ResampleMinutes: 5, IsDailyOrHigher: false),
            ["15M"] = new("15M", BaseMoexInterval: 1, DaysBack: 5, ResampleMinutes: 15, IsDailyOrHigher: false),
            ["30M"] = new("30M", BaseMoexInterval: 1, DaysBack: 5, ResampleMinutes: 30, IsDailyOrHigher: false),
            // Более глубокая версия 30 М, смотрит дальше но загружаетса не мгновенно (пересбор свечей)
            // ["30M"] = new("30M", BaseMoexInterval: 10, DaysBack: 25, ResampleMinutes: 30, IsDailyOrHigher: false),
            ["1H"] = new("1H", BaseMoexInterval: 60, DaysBack: 60, ResampleMinutes: 1, IsDailyOrHigher: false),
            ["1D"] = new("1D", BaseMoexInterval: 24, DaysBack: 365, ResampleMinutes: 1, IsDailyOrHigher: true),
        };
}
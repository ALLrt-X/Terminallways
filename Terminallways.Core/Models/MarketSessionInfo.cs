namespace Terminallways.Core.Models;

public enum MarketSessionStatus
{
    Active,         // Зелёный: торги идут
    PauseOrAuction, // Жёлтый: аукцион, перерыв или пауза в сделках
    Closed          // Серый (полупрозрачный): сессия закрыта
}

public record MarketSessionInfo(
    MarketSessionStatus Status,
    string RawCode,
    string Tooltip
);

public static class MarketSessionEvaluator
{
    public static MarketSessionInfo Evaluate(string? rawStatus, DateTime? lastCandleEnd, bool isDaily)
    {
        string code = string.IsNullOrWhiteSpace(rawStatus) ? "?" : rawStatus.Trim().ToUpperInvariant();
        TimeSpan? lag = lastCandleEnd.HasValue ? DateTime.Now - lastCandleEnd.Value : null;

        // 1. Официальные фазы аукциона или перерыва на MOEX
        if (code is "S" or "C" or "D" or "B" or "F")
        {
            string phase = code switch
            {
                "S" => "Аукцион открытия",
                "C" => "Аукцион закрытия",
                "D" => "Дискретный аукцион",
                "B" => "Перерыв в торгах",
                "F" => "Период закрытия",
                _ => "Аукцион / перерыв"
            };
            return new(MarketSessionStatus.PauseOrAuction, code, $"{phase} (MOEX: {code})");
        }

        // 2. Живые внутридневные свечи (обходит 15-минутный лаг marketdata на открытии)
        if (!isDaily && lag.HasValue && lag.Value >= TimeSpan.FromSeconds(-30) && lag.Value <= TimeSpan.FromMinutes(3))
        {
            return new(MarketSessionStatus.Active, code, "Активная сессия (идут сделки)");
        }

        // 3. Официально торги открыты ("T")
        if (code == "T")
        {
            if (isDaily)
                return new(MarketSessionStatus.Active, code, "Активная сессия (MOEX: T)");

            if (lag.HasValue && lag.Value > TimeSpan.FromMinutes(3) && lag.Value <= TimeSpan.FromMinutes(16))
            {
                return new(
                    MarketSessionStatus.PauseOrAuction,
                    code,
                    $"Пауза в сделках ({(int)lag.Value.TotalMinutes} мин) / ожидание статуса");
            }

            if (!lag.HasValue || lag.Value <= TimeSpan.FromMinutes(3))
                return new(MarketSessionStatus.Active, code, "Активная сессия (MOEX: T)");
        }

        // 4. Сессия закрыта
        string lagNote = lag.HasValue && lag.Value.TotalMinutes >= 1
            ? $", тишина {(int)lag.Value.TotalMinutes} мин"
            : "";
        return new(MarketSessionStatus.Closed, code, $"Спящая сессия (MOEX: {code}{lagNote})");
    }
}
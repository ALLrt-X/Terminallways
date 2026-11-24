using Terminallways.Core.Models;

namespace Terminallways.Core.Data;

public static class TimeframeResampler
{
    /// <summary>
    /// Собирает свечи заданного таймфрейма (1..60, 120, 240 или 1440 для 1D) из более мелких свечей.
    /// </summary>
    public static List<Candle> Resample(IReadOnlyList<Candle> sourceCandles, int targetMinutes)
    {
        if (targetMinutes <= 1 || sourceCandles.Count == 0)
            return sourceCandles.ToList();

        var result = new List<Candle>();

        DateTime currentBucket = GetBucketStart(sourceCandles[0].Begin, targetMinutes);
        DateTime bucketEnd = sourceCandles[0].End;
        double open = sourceCandles[0].Open;
        double high = sourceCandles[0].High;
        double low = sourceCandles[0].Low;
        double close = sourceCandles[0].Close;
        double volume = sourceCandles[0].Volume;

        for (int i = 1; i < sourceCandles.Count; i++)
        {
            var c = sourceCandles[i];
            DateTime bucket = GetBucketStart(c.Begin, targetMinutes);

            if (bucket == currentBucket)
            {
                if (c.High > high) high = c.High;
                if (c.Low < low) low = c.Low;
                close = c.Close;
                volume += c.Volume;
                bucketEnd = c.End;
            }
            else
            {
                result.Add(new Candle(currentBucket, bucketEnd, open, high, low, close, volume));

                currentBucket = bucket;
                bucketEnd = c.End;
                open = c.Open;
                high = c.High;
                low = c.Low;
                close = c.Close;
                volume = c.Volume;
            }
        }

        result.Add(new Candle(currentBucket, bucketEnd, open, high, low, close, volume));
        return result;
    }

    /// <summary>
    /// Склеивает глубокую историю (например, 1H за 60 дней или 1D за год) с живыми свечами,
    /// собранными из 1m-потока. Последняя потенциально отстающая свеча истории заменяется живой.
    /// </summary>
    public static List<Candle> MergeHistoryWithLive(
        IReadOnlyList<Candle> history,
        IReadOnlyList<Candle> live)
    {
        if (history.Count == 0) return live.ToList();
        if (live.Count == 0) return history.ToList();

        if (history[^1].Begin > live[^1].Begin)
            return history.ToList();

        DateTime cutoff = history[^1].Begin;
        if (cutoff < live[0].Begin)
            cutoff = live[0].Begin;

        var result = new List<Candle>(history.Count + 8);

        foreach (var c in history)
        {
            if (c.Begin < cutoff)
                result.Add(c);
        }

        foreach (var c in live)
        {
            if (c.Begin >= cutoff)
                result.Add(c);
        }

        return result;
    }

    private static DateTime GetBucketStart(DateTime dt, int tfMinutes)
    {
        if (tfMinutes >= 1440)
            return dt.Date;

        int totalMinutes = dt.Hour * 60 + dt.Minute;
        int bucketMinute = (totalMinutes / tfMinutes) * tfMinutes;
        return dt.Date.AddMinutes(bucketMinute);
    }
}
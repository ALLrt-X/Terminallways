using Terminallways.Core.Models;

namespace Terminallways.Core.Data;

public static class TimeframeResampler
{
    /// <summary>
    /// Собирает свечи заданного таймфрейма (в минутах: 1, 3, 5, 15, 30, 60) из 1m-свечей.
    /// Привязка идёт к ровной сетке времени (например, для 3m: 10:00, 10:03, 10:06...).
    /// </summary>
    public static List<Candle> Resample(IReadOnlyList<Candle> oneMinuteCandles, int targetMinutes)
    {
        if (targetMinutes <= 1 || oneMinuteCandles.Count == 0)
            return oneMinuteCandles.ToList();

        var result = new List<Candle>();

        DateTime currentBucket = GetBucketStart(oneMinuteCandles[0].Begin, targetMinutes);
        DateTime bucketEnd = oneMinuteCandles[0].End;
        double open = oneMinuteCandles[0].Open;
        double high = oneMinuteCandles[0].High;
        double low = oneMinuteCandles[0].Low;
        double close = oneMinuteCandles[0].Close;
        double volume = oneMinuteCandles[0].Volume;

        for (int i = 1; i < oneMinuteCandles.Count; i++)
        {
            var c = oneMinuteCandles[i];
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

    private static DateTime GetBucketStart(DateTime dt, int tfMinutes)
    {
        int totalMinutes = dt.Hour * 60 + dt.Minute;
        int bucketMinute = (totalMinutes / tfMinutes) * tfMinutes;
        return dt.Date.AddMinutes(bucketMinute);
    }
}
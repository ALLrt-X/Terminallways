using Terminallways.Core.Models;

namespace Terminallways.Core.Indicators;

public static class HeikinAshiConverter
{
    public static List<Candle> Convert(IReadOnlyList<Candle> rawCandles)
    {
        var result = new List<Candle>(rawCandles.Count);
        if (rawCandles.Count == 0)
            return result;

        double prevOpen = (rawCandles[0].Open + rawCandles[0].Close) / 2.0;
        double prevClose = (rawCandles[0].Open + rawCandles[0].High + rawCandles[0].Low + rawCandles[0].Close) / 4.0;
        double firstHigh = Math.Max(rawCandles[0].High, Math.Max(prevOpen, prevClose));
        double firstLow = Math.Min(rawCandles[0].Low, Math.Min(prevOpen, prevClose));

        result.Add(new Candle(
            rawCandles[0].Begin,
            rawCandles[0].End,
            prevOpen,
            firstHigh,
            firstLow,
            prevClose,
            rawCandles[0].Volume
        ));

        for (int i = 1; i < rawCandles.Count; i++)
        {
            var c = rawCandles[i];
            double haClose = (c.Open + c.High + c.Low + c.Close) / 4.0;
            double haOpen = (prevOpen + prevClose) / 2.0;
            double haHigh = Math.Max(c.High, Math.Max(haOpen, haClose));
            double haLow = Math.Min(c.Low, Math.Min(haOpen, haClose));

            result.Add(new Candle(c.Begin, c.End, haOpen, haHigh, haLow, haClose, c.Volume));

            prevOpen = haOpen;
            prevClose = haClose;
        }

        return result;
    }
}
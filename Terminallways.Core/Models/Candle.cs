// Структура одной свечи (OHLCV + время)

using System;
using System.Collections.Generic;
using System.Text;

namespace Terminallways.Core.Models
{
    public record Candle(
        DateTime Begin,
        DateTime End,
        double Open,
        double High,
        double Low,
        double Close,
        double Volume
    );
}

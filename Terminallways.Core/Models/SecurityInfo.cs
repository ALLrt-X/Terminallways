// Справка по бумаге (лотность, шаг, цена, задержка)

using System;
using System.Collections.Generic;
using System.Text;

namespace Terminallways.Core.Models
{
    public record SecurityInfo(
        string Ticker,
        int LotSize,
        double MinStep,
        double? LastPrice,
        string UpdateTime
    );
}

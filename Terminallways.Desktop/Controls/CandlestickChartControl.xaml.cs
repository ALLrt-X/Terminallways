using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Terminallways.Core.Models;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;


namespace Terminallways.Desktop.Controls;

public partial class CandlestickChartControl : UserControl
{
    private const double BaseCandleWidth = 10.0;
    private const double RightMarginPx = 45.0;

    // Замороженные кисти (создаются один раз)
    private static readonly Brush StdUpBrush = FreezeBrush("#26A69A");
    private static readonly Brush StdDownBrush = FreezeBrush("#EF5350");
    private static readonly Brush HaUpBrush = FreezeBrush("#4FA3FF");    // Голубые (рост)
    private static readonly Brush HaDownBrush = FreezeBrush("#5C6473");  // Серые (падение)
    private static readonly Brush GridBrush = FreezeBrush(Color.FromArgb(22, 255, 255, 255));
    private static readonly Brush DaySepBrush = FreezeBrush(Color.FromArgb(75, 255, 255, 255));
    private static readonly Brush TextBrush = FreezeBrush("#9AA4B2");
    private static readonly Brush LastLineBrush = FreezeBrush("#60A5FA");
    private static readonly FontFamily MonoFont = new("Consolas");

    private IReadOnlyList<Candle> _candles = Array.Empty<Candle>();
    private bool _isHeikinAshi;

    // Добавка для TimeframeSpec.cs
    private bool _isDaily;

    // Добавка AutoScale
    public event Action<bool>? AutoScaleChanged;

    // Добавка Burn
    public event Action<bool>? BurnModeToggled;

    // Добавка настроек терминала
    public event Action? SettingsRequested;


    // Быстрый переключатель косметики: поставь false, чтобы скрыть бирку тикера
    public bool ShowTickerBadge { get; set; } = true;
    public string Ticker { get; set; } = string.Empty;

    public bool IsAutoScale
    {
        get => _isAutoScale;
        set => SetAutoScale(value);
    }

    private static readonly Brush TickerBadgeBrush = FreezeBrush("#243247");

    private bool _isAutoScale = true;
    private double _lastMinPrice, _lastMaxPrice;
    private bool _hasLastRange;
    private double _manualMinPrice, _manualMaxPrice;
    private bool _hasManualRange;
    // ---

    private double _zoomX = 1.0;
    private double _scaleY = 1.0;
    private double _panX = 0.0;
    private double _panY = 0.0;

    private bool _isPanning;

    // --- Настройки инерции перемещения ---
    public bool EnablePanInertia { get; set; } = true;
    //private const double InertiaFriction = 2.0; // Чем выше число, тем короче и жёстче торможение (6..12)
    public double InertiaFriction { get; set; } = 2.0;

    private readonly Stopwatch _inertiaSw = Stopwatch.StartNew();
    private double _velX;
    private double _velY;
    private double _lastDragTimeSec;
    //private double _lastFrameTimeSec; -- Не используетса после фикса на плавность
    private bool _isCoasting;

    private Point _lastMousePos;

    private bool _isScalingPrice;
    private Point _lastPriceMousePos;

    // Доп фикс для плавности
    private readonly Queue<(double Time, Point Pos)> _dragHistory = new();
    private TimeSpan _lastRenderTime = TimeSpan.Zero;

    // --- Настройки темы перекрестия (меняй цвета и штрихпунктир здесь) ---
    private static readonly Brush CrosshairLineBrush = FreezeBrush("#7A94B8");
    private static readonly Brush CrosshairTagBgBrush = FreezeBrush("#2A3548");
    private static readonly Brush CrosshairTagTextBrush = FreezeBrush("#E6EDF7");
    private static readonly DoubleCollection CrosshairDash = CreateFrozenDash(4, 3);
    public bool SnapCrosshairToCandle { get; set; } = true;

    private ChartToolMode _toolMode = ChartToolMode.Cursor;
    private bool _isBarInfoEnabled = true;

    // Кэш параметров последнего кадра (чтобы перекрестие считало цену/бар без DrawChart)
    private double _viewMinPrice;
    private double _viewRange = 1.0;
    private string _viewPriceFmt = "F2";

    private bool _isMouseOverChart;
    private Point _lastCrosshairPos;
    private int _hoveredCandleIndex = -1;
    private int _lastInfoIndex = -2;
    private Candle? _lastInfoCandle;

    private readonly DispatcherTimer _toolbarHideTimer;
    private bool _isToolbarVisible;

    // Конструктор CandlestickChartControl()
    public CandlestickChartControl()
    {
        InitializeComponent();
        RenderOptions.SetEdgeMode(ChartCanvas, EdgeMode.Aliased);
        ChartCanvas.SnapsToDevicePixels = true;

        // Инерция пермещения
        Unloaded += (_, _) => StopInertia();

        // Применяем тему перекрестия к оверлейным элементам один раз
        CrosshairV.Stroke = CrosshairLineBrush;
        CrosshairH.Stroke = CrosshairLineBrush;
        CrosshairV.StrokeDashArray = CrosshairDash;
        CrosshairH.StrokeDashArray = CrosshairDash;
        CrosshairPriceTag.Background = CrosshairTagBgBrush;
        CrosshairPriceText.Foreground = CrosshairTagTextBrush;
        CrosshairDateTag.Background = CrosshairTagBgBrush;
        CrosshairDateText.Foreground = CrosshairTagTextBrush;

        // Подписка на модульную панель инструментов
        ToolBar.ToolModeChanged += mode =>
        {
            _toolMode = mode;
            ChartCanvas.Cursor = mode == ChartToolMode.Crosshair ? Cursors.Cross : Cursors.Arrow;
            if (mode != ChartToolMode.Crosshair)
            {
                _hoveredCandleIndex = -1;
                HideCrosshair();
            }
            else if (_isMouseOverChart)
            {
                UpdateCrosshairVisuals();
            }
            UpdateBarInfo(force: true);
        };

        ToolBar.BarInfoToggled += enabled =>
        {
            _isBarInfoEnabled = enabled;
            UpdateBarInfo(force: true);
        };

        // Добавка Burn
        ToolBar.BurnModeToggled += enabled => BurnModeToggled?.Invoke(enabled);

        _toolbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _toolbarHideTimer.Tick += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            if (!ToolbarTriggerZone.IsMouseOver && !ToolBar.IsMouseOver)
                SetToolbarVisible(false);
        };

        // Добавка настроек терминала
        ToolBar.SettingsClicked += () => SettingsRequested?.Invoke();
    }

    //public void SetCandles(IReadOnlyList<Candle> candles, bool isHeikinAshi, bool resetView = false)
    //{
    //    _candles = candles;
    //    _isHeikinAshi = isHeikinAshi;
    //    if (resetView)
    //        ResetView();
    //    else
    //        DrawChart();
    //}

    // Добавка для TimeframeSpec.cs
    public void SetCandles(IReadOnlyList<Candle> candles, bool isHeikinAshi, bool resetView = false, bool isDaily = false)
    {
        _candles = candles;
        _isHeikinAshi = isHeikinAshi;
        _isDaily = isDaily;
        if (resetView)
            ResetView();
        else
            DrawChart();
    }

    // Метод AutoScale
        public void SetAutoScale(bool enabled, bool redraw = true)
    {
        if (_isAutoScale == enabled) return;

        _isAutoScale = enabled;
        if (enabled)
        {
            _scaleY = 1.0;
            _panY = 0.0;
        }
        else if (_hasLastRange)
        {
            // Замораживаем текущий видимый диапазон, чтобы график не прыгнул
            _manualMinPrice = _lastMinPrice;
            _manualMaxPrice = _lastMaxPrice;
            _hasManualRange = true;
        }

        AutoScaleChanged?.Invoke(_isAutoScale);
        if (redraw) DrawChart();
    }

    public void ResetView()
    {
        StopInertia();
        _zoomX = 1.0;
        _scaleY = 1.0;
        _panX = 0.0;
        _panY = 0.0;
        _hasManualRange = false; // При смене тикера/ТФ пересчитает базу даже в ручном режиме
        DrawChart();
    }

    //public void ResetView()
    //{
    //    _zoomX = 1.0;
    //    _scaleY = 1.0;
    //    _panX = 0.0;
    //    _panY = 0.0;
    //    DrawChart();
    //}

    private void Control_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        PriceAxisCanvas.Children.Clear();
        DateAxisCanvas.Children.Clear();

        if (_candles.Count == 0) return;

        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int n = _candles.Count;
        double slotWidth = BaseCandleWidth * _zoomX;
        double bodyWidth = Math.Max(1.0, slotWidth * 0.78);

        double GetX(int index)
        {
            int fromRight = (n - 1) - index;
            return w - RightMarginPx - (fromRight * slotWidth) - (slotWidth / 2.0) + _panX;
        }

        // 1. Поиск min/max только среди видимых свечей

        //
        //double maxPrice = double.MinValue;
        //double minPrice = double.MaxValue;
        //bool hasVisible = false;

        //for (int i = 0; i < n; i++)
        //{
        //    double x = GetX(i);
        //    if (x + slotWidth >= 0 && x - slotWidth <= w)
        //    {
        //        var c = _candles[i];
        //        if (c.High > maxPrice) maxPrice = c.High;
        //        if (c.Low < minPrice) minPrice = c.Low;
        //        hasVisible = true;
        //    }
        //}

        //if (!hasVisible)
        //{
        //    maxPrice = _candles.Max(c => c.High);
        //    minPrice = _candles.Min(c => c.Low);
        //}

        //double range = maxPrice - minPrice;
        //if (range <= 0) range = Math.Max(maxPrice * 0.01, 0.01);

        //maxPrice += range * 0.1;
        //minPrice -= range * 0.1;
        //range = maxPrice - minPrice;
        //

        double minPrice, maxPrice;

        if (!_isAutoScale && _hasManualRange)
        {
            minPrice = _manualMinPrice;
            maxPrice = _manualMaxPrice;
        }
        else
        {
            maxPrice = double.MinValue;
            minPrice = double.MaxValue;
            bool hasVisible = false;

            for (int i = 0; i < n; i++)
            {
                double x = GetX(i);
                if (x + slotWidth >= 0 && x - slotWidth <= w)
                {
                    var c = _candles[i];
                    if (c.High > maxPrice) maxPrice = c.High;
                    if (c.Low < minPrice) minPrice = c.Low;
                    hasVisible = true;
                }
            }

            if (!hasVisible)
            {
                maxPrice = _candles.Max(c => c.High);
                minPrice = _candles.Min(c => c.Low);
            }

            double rawRange = maxPrice - minPrice;
            if (rawRange <= 0) rawRange = Math.Max(maxPrice * 0.01, 0.01);

            maxPrice += rawRange * 0.1;
            minPrice -= rawRange * 0.1;

            _lastMinPrice = minPrice;
            _lastMaxPrice = maxPrice;
            _hasLastRange = true;

            if (!_isAutoScale)
            {
                _manualMinPrice = minPrice;
                _manualMaxPrice = maxPrice;
                _hasManualRange = true;
            }
        }

        double range = maxPrice - minPrice;
        if (range <= 0) range = 0.01;

        // --

        double GetY(double price)
        {
            double baseY = h - ((price - minPrice) / range) * h;
            return (baseY - h / 2.0) * _scaleY + (h / 2.0) + _panY;
        }

        string priceFmt = maxPrice < 50 ? "F3" : "F2";

        _viewMinPrice = minPrice;
        _viewRange = range;
        _viewPriceFmt = priceFmt;

        var upWicks = new GeometryGroup();
        var downWicks = new GeometryGroup();
        var upBodies = new GeometryGroup();
        var downBodies = new GeometryGroup();
        var daySeps = new GeometryGroup();
        var xGrid = new GeometryGroup();

        // Фикс для плавности пермещения (без дёрганий)
        //double lastLabelX = double.MaxValue;
        int labelStep = Math.Max(1, (int)Math.Ceiling(115.0 / slotWidth));

        // 2. Проход справа налево (от новых свечей к старым)
        for (int i = n - 1; i >= 0; i--)
        {
            var c = _candles[i];
            double xCenter = GetX(i);

            if (xCenter - slotWidth > w) continue;
            if (xCenter + slotWidth < 0) break;

            // Разделитель торговых дней
            //if (i > 0 && c.Begin.Date != _candles[i - 1].Begin.Date)

            // Добавка для TimeframeSpec.cs
            if (!_isDaily && i > 0 && c.Begin.Date != _candles[i - 1].Begin.Date)
            {
                double sepX = Math.Round(xCenter - slotWidth / 2.0) + 0.5;
                if (sepX >= 0 && sepX <= w)
                    daySeps.Children.Add(new LineGeometry(new Point(sepX, 0), new Point(sepX, h)));
            }

            // Метки оси X

            // Фикс для плавности пермещения (без дёрганий)
            if (i % labelStep == 0)
            //if (lastLabelX - xCenter > 115)
            {
                double gx = Math.Round(xCenter) + 0.5;
                xGrid.Children.Add(new LineGeometry(new Point(gx, 0), new Point(gx, h)));

                var lbl = new TextBlock
                {
                    //Text = c.Begin.ToString("dd.MM HH:mm"),

                    // Добавка для TimeframeSpec.cs
                    Text = _isDaily ? c.Begin.ToString("dd.MM.yy") : c.Begin.ToString("dd.MM HH:mm"),
                    Foreground = TextBrush,
                    FontSize = 11,
                    FontFamily = MonoFont
                };
                Canvas.SetLeft(lbl, xCenter - 38);
                Canvas.SetTop(lbl, 6);
                DateAxisCanvas.Children.Add(lbl);

                // Убрано ибо - Фикс для плавности пермещения (без дёрганий)
                //lastLabelX = xCenter;
            }

            double yHigh = GetY(c.High);
            double yLow = GetY(c.Low);
            double yOpen = GetY(c.Open);
            double yClose = GetY(c.Close);

            bool isUp = c.Close >= c.Open;
            double wx = Math.Round(xCenter) + 0.5;
            var wick = new LineGeometry(new Point(wx, yHigh), new Point(wx, yLow));

            double top = Math.Min(yOpen, yClose);
            double bodyH = Math.Max(Math.Abs(yOpen - yClose), 1.0);
            double left = Math.Round(xCenter - bodyWidth / 2.0);
            var body = new RectangleGeometry(new Rect(left, Math.Round(top), Math.Max(1, Math.Round(bodyWidth)), Math.Round(bodyH)));

            if (isUp)
            {
                upWicks.Children.Add(wick);
                upBodies.Children.Add(body);
            }
            else
            {
                downWicks.Children.Add(wick);
                downBodies.Children.Add(body);
            }
        }

        // 3. Горизонтальная сетка и шкала цен
        var yGrid = new GeometryGroup();
        const int gridLines = 9;
        double step = range / gridLines;

        for (int i = 0; i <= gridLines; i++)
        {
            double price = minPrice + step * i;
            double y = Math.Round(GetY(price)) + 0.5;
            if (y < 0 || y > h) continue;

            yGrid.Children.Add(new LineGeometry(new Point(0, y), new Point(w, y)));

            var lbl = new TextBlock
            {
                Text = price.ToString(priceFmt),
                Foreground = TextBrush,
                FontSize = 11,
                FontFamily = MonoFont
            };
            Canvas.SetLeft(lbl, 8);
            Canvas.SetTop(lbl, y - 7);
            PriceAxisCanvas.Children.Add(lbl);
        }

        // 4. Отрисовка слоёв
        ChartCanvas.Children.Add(new Path { Stroke = GridBrush, StrokeThickness = 1, Data = xGrid });
        ChartCanvas.Children.Add(new Path { Stroke = GridBrush, StrokeThickness = 1, Data = yGrid });
        ChartCanvas.Children.Add(new Path
        {
            Stroke = DaySepBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            Data = daySeps
        });

        Brush upBrush = _isHeikinAshi ? HaUpBrush : StdUpBrush;
        Brush downBrush = _isHeikinAshi ? HaDownBrush : StdDownBrush;

        ChartCanvas.Children.Add(new Path { Stroke = upBrush, StrokeThickness = 1, Data = upWicks });
        ChartCanvas.Children.Add(new Path { Stroke = downBrush, StrokeThickness = 1, Data = downWicks });
        ChartCanvas.Children.Add(new Path { Fill = upBrush, Data = upBodies });
        ChartCanvas.Children.Add(new Path { Fill = downBrush, Data = downBodies });

        // 5. Пунктирная линия и плашка последней цены
        //double lastPrice = _candles[^1].Close;
        //double lastY = Math.Round(GetY(lastPrice)) + 0.5;

        // 5. Пунктирная линия, плашка последней цены и бирка тикера
        double lastPrice = _candles[^1].Close;
        double lastY = Math.Round(GetY(lastPrice)) + 0.5;
        DrawLastPriceIndicator(w, h, lastPrice, lastY, priceFmt);

        if (_toolMode == ChartToolMode.Crosshair && _isMouseOverChart)
            UpdateCrosshairVisuals();
        else
            UpdateBarInfo(force: true);

        // Рисует штрихпунктирную линию от текущей цены (лишнее ибо есть DrawLastPriceIndicator)
        //if (lastY >= 0 && lastY <= h)
        //{
        //    ChartCanvas.Children.Add(new Line
        //    {
        //        X1 = 0,
        //        X2 = w,
        //        Y1 = lastY,
        //        Y2 = lastY,
        //        Stroke = LastLineBrush,
        //        StrokeThickness = 1,
        //        StrokeDashArray = new DoubleCollection { 3, 3 }
        //    });

        //    var badge = new Border
        //    {
        //        Background = LastLineBrush,
        //        CornerRadius = new CornerRadius(2),
        //        Padding = new Thickness(5, 1, 5, 1),
        //        Child = new TextBlock
        //        {
        //            Text = lastPrice.ToString(priceFmt),
        //            Foreground = Brushes.Black,
        //            FontWeight = FontWeights.Bold,
        //            FontSize = 11,
        //            FontFamily = MonoFont
        //        }
        //    };
        //    Canvas.SetLeft(badge, 4);
        //    Canvas.SetTop(badge, lastY - 9);
        //    PriceAxisCanvas.Children.Add(badge);
        //}
    }

    // Косметическая добавка индикатора
    private void DrawLastPriceIndicator(double w, double h, double lastPrice, double lastY, string priceFmt)
    {
        if (lastY < 0 || lastY > h) return;

        // 1) Пунктирная линия текущей цены
        ChartCanvas.Children.Add(new Line
        {
            X1 = 0,
            X2 = w,
            Y1 = lastY,
            Y2 = lastY,
            Stroke = LastLineBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 }
        });

        // 2) Плашка цены на правой шкале (PriceAxisCanvas)
        var priceBadge = new Border
        {
            Background = LastLineBrush,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1, 5, 1),
            Child = new TextBlock
            {
                Text = lastPrice.ToString(priceFmt),
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                FontFamily = MonoFont
            }
        };
        Canvas.SetLeft(priceBadge, 4);
        Canvas.SetTop(priceBadge, lastY - 9);
        PriceAxisCanvas.Children.Add(priceBadge);

        // 3) Косметическая бирка тикера слева от плашки цены (на правом краю ChartCanvas)
        // Чтобы отключить или изменить вид — редактировать только этот блок
        if (ShowTickerBadge && !string.IsNullOrWhiteSpace(Ticker))
        {
            var tickerBadge = new Border
            {
                Background = TickerBadgeBrush,
                BorderBrush = LastLineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(5, 0, 5, 0),
                Child = new TextBlock
                {
                    Text = Ticker,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    FontFamily = MonoFont
                }
            };
            Canvas.SetRight(tickerBadge, 2); // Отступ от правой границы графика
            Canvas.SetTop(tickerBadge, lastY - 9);
            ChartCanvas.Children.Add(tickerBadge);
        }
    }

    // --- Навигация мышью (зум к курсору, панорамирование, масштаб цены) ---

    private void ChartCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_candles.Count == 0) return;

        double mouseX = e.GetPosition(ChartCanvas).X;
        double w = ChartCanvas.ActualWidth;
        double prevSlot = BaseCandleWidth * _zoomX;

        _zoomX = Math.Clamp(_zoomX * (e.Delta > 0 ? 1.12 : 0.89), 0.15, 25.0);
        double newSlot = BaseCandleWidth * _zoomX;

        double anchorIndex = (w - RightMarginPx + _panX - mouseX) / prevSlot;
        _panX = mouseX - (w - RightMarginPx) + anchorIndex * newSlot;
        DrawChart();
    }

    private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ResetView(); return; }

        StopInertia();
        _isPanning = true;
        _lastMousePos = e.GetPosition(ChartCanvas);

        double now = _inertiaSw.Elapsed.TotalSeconds;
        _lastDragTimeSec = now;
        _dragHistory.Clear();
        _dragHistory.Enqueue((now, _lastMousePos));

        ChartCanvas.CaptureMouse();
    }

    private void ChartCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasPanning = _isPanning;
        _isPanning = false;
        ChartCanvas.ReleaseMouseCapture();

        if (!wasPanning || !EnablePanInertia || _dragHistory.Count < 2)
            return;

        double now = _inertiaSw.Elapsed.TotalSeconds;
        if (now - _lastDragTimeSec > 0.06)
            return;

        var oldest = _dragHistory.Peek();
        double span = _lastDragTimeSec - oldest.Time;
        if (span < 0.008)
            return;

        _velX = (_lastMousePos.X - oldest.Pos.X) / span;
        _velY = _isAutoScale ? 0.0 : (_lastMousePos.Y - oldest.Pos.Y) / span;

        if (Math.Abs(_velX) > 100 || Math.Abs(_velY) > 100)
            StartInertia();
    }

    private void ChartCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Released)
        {
            _isPanning = false;
            ChartCanvas.ReleaseMouseCapture();
        }

        _isMouseOverChart = false;
        _hoveredCandleIndex = -1;
        HideCrosshair();
        UpdateBarInfo(force: true);
    }

    //private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    //{
    //    if (!_isPanning) return;
    //    Point pos = e.GetPosition(ChartCanvas);
    //    _panX += pos.X - _lastMousePos.X;
    //    _panY += pos.Y - _lastMousePos.Y;
    //    _lastMousePos = pos;
    //    DrawChart();
    //}
    private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point pos = e.GetPosition(ChartCanvas);
        _lastCrosshairPos = pos;
        _isMouseOverChart = true;

        if (_isPanning)
        {
            double now = _inertiaSw.Elapsed.TotalSeconds;
            _panX += pos.X - _lastMousePos.X;
            if (!_isAutoScale)
                _panY += pos.Y - _lastMousePos.Y;

            _lastDragTimeSec = now;
            _dragHistory.Enqueue((now, pos));
            while (_dragHistory.Count > 2 && now - _dragHistory.Peek().Time > 0.08)
                _dragHistory.Dequeue();

            _lastMousePos = pos;
            DrawChart();
            return;
        }

        // Движение без панорамирования: только сверхбыстрый оверлей, без DrawChart()
        if (_toolMode == ChartToolMode.Crosshair)
            UpdateCrosshairVisuals();
    }

    //private void PriceAxis_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    //{
    //    if (e.ClickCount == 2) { _scaleY = 1.0; _panY = 0.0; DrawChart(); return; }
    //    _isScalingPrice = true;
    //    _lastPriceMousePos = e.GetPosition(PriceAxisCanvas);
    //    PriceAxisCanvas.CaptureMouse();
    //}
    private void PriceAxis_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            SetAutoScale(true); // Двойной клик по шкале цен включает Авто Y
            return;
        }
        _isScalingPrice = true;
        _lastPriceMousePos = e.GetPosition(PriceAxisCanvas);
        PriceAxisCanvas.CaptureMouse();
    }

    private void PriceAxis_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isScalingPrice = false;
        PriceAxisCanvas.ReleaseMouseCapture();
    }

    private void PriceAxis_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Released)
        {
            _isScalingPrice = false;
            PriceAxisCanvas.ReleaseMouseCapture();
        }
    }

    //private void PriceAxis_MouseMove(object sender, MouseEventArgs e)
    //{
    //    if (!_isScalingPrice) return;
    //    Point pos = e.GetPosition(PriceAxisCanvas);
    //    double deltaY = pos.Y - _lastPriceMousePos.Y;
    //    if (deltaY == 0) return;

    //    double prevScale = _scaleY;
    //    _scaleY = Math.Clamp(_scaleY * (1.0 - deltaY * 0.01), 0.1, 20.0);

    //    double halfH = PriceAxisCanvas.ActualHeight / 2.0;
    //    double anchorY = pos.Y - halfH;
    //    _panY = anchorY - (anchorY - _panY) * (_scaleY / prevScale);

    //    _lastPriceMousePos = pos;
    //    DrawChart();
    //}
    private void PriceAxis_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isScalingPrice) return;
        Point pos = e.GetPosition(PriceAxisCanvas);
        double deltaY = pos.Y - _lastPriceMousePos.Y;
        if (deltaY == 0) return;

        // Потянули шкалу вручную — автоматически снимаем Авто Y
        if (_isAutoScale)
            SetAutoScale(false, redraw: false);

        double prevScale = _scaleY;
        _scaleY = Math.Clamp(_scaleY * (1.0 - deltaY * 0.01), 0.1, 20.0);

        double halfH = PriceAxisCanvas.ActualHeight / 2.0;
        double anchorY = pos.Y - halfH;
        _panY = anchorY - (anchorY - _panY) * (_scaleY / prevScale);

        _lastPriceMousePos = pos;
        DrawChart();
    }

    // --- Оверлей перекрестия и информации о баре (без перерисовки свечей) ---

    private void UpdateCrosshairVisuals()
    {
        if (_candles.Count == 0 || _toolMode != ChartToolMode.Crosshair)
        {
            HideCrosshair();
            return;
        }

        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int n = _candles.Count;
        double slotWidth = BaseCandleWidth * _zoomX;

        double fromRight = (w - RightMarginPx - (slotWidth / 2.0) + _panX - _lastCrosshairPos.X) / slotWidth;
        int idx = (n - 1) - (int)Math.Round(fromRight);
        bool validCandle = idx >= 0 && idx < n;
        _hoveredCandleIndex = validCandle ? idx : -1;

        double x = SnapCrosshairToCandle && validCandle
            ? w - RightMarginPx - ((n - 1 - idx) * slotWidth) - (slotWidth / 2.0) + _panX
            : _lastCrosshairPos.X;

        double y = Math.Clamp(_lastCrosshairPos.Y, 0, h);
        double drawX = Math.Round(x) + 0.5;
        double drawY = Math.Round(y) + 0.5;

        // Вертикальная линия
        if (drawX >= 0 && drawX <= w)
        {
            CrosshairV.X1 = drawX;
            CrosshairV.X2 = drawX;
            CrosshairV.Y1 = 0;
            CrosshairV.Y2 = h;
            CrosshairV.Visibility = Visibility.Visible;
        }
        else
        {
            CrosshairV.Visibility = Visibility.Collapsed;
        }

        // Горизонтальная линия
        CrosshairH.X1 = 0;
        CrosshairH.X2 = w;
        CrosshairH.Y1 = drawY;
        CrosshairH.Y2 = drawY;
        CrosshairH.Visibility = Visibility.Visible;

        // Плашка цены на правой оси
        double baseY = (y - _panY - h / 2.0) / _scaleY + h / 2.0;
        double price = _viewMinPrice + ((h - baseY) / h) * _viewRange;
        CrosshairPriceText.Text = price.ToString(_viewPriceFmt);
        Canvas.SetLeft(CrosshairPriceTag, 4);
        Canvas.SetTop(CrosshairPriceTag, Math.Clamp(drawY - 9, 0, Math.Max(0, h - 18)));
        CrosshairPriceTag.Visibility = Visibility.Visible;

        // Плашка времени на нижней оси
        if (validCandle && drawX >= 0 && drawX <= w)
        {
            var dt = _candles[idx].Begin;
            CrosshairDateText.Text = _isDaily ? dt.ToString("dd.MM.yy") : dt.ToString("dd.MM HH:mm");
            Canvas.SetLeft(CrosshairDateTag, Math.Clamp(drawX - 38, 2, Math.Max(2, w - 84)));
            Canvas.SetTop(CrosshairDateTag, 4);
            CrosshairDateTag.Visibility = Visibility.Visible;
        }
        else
        {
            CrosshairDateTag.Visibility = Visibility.Collapsed;
        }

        UpdateBarInfo(force: false);
    }

    private void HideCrosshair()
    {
        CrosshairV.Visibility = Visibility.Collapsed;
        CrosshairH.Visibility = Visibility.Collapsed;
        CrosshairPriceTag.Visibility = Visibility.Collapsed;
        CrosshairDateTag.Visibility = Visibility.Collapsed;
    }

    private void UpdateBarInfo(bool force)
    {
        if (!_isBarInfoEnabled || _candles.Count == 0)
        {
            BarInfoPanel.Visibility = Visibility.Collapsed;
            _lastInfoIndex = -2;
            _lastInfoCandle = null;
            return;
        }

        // В режиме перекрестия — наведённый бар; в режиме курсора (или вне графика) — последний реальный бар
        int targetIdx = (_toolMode == ChartToolMode.Crosshair && _isMouseOverChart && _hoveredCandleIndex >= 0)
            ? _hoveredCandleIndex
            : _candles.Count - 1;

        var c = _candles[targetIdx];
        if (!force && targetIdx == _lastInfoIndex && c == _lastInfoCandle)
            return;

        _lastInfoIndex = targetIdx;
        _lastInfoCandle = c;

        double pct = c.Open != 0 ? (c.Close - c.Open) / c.Open * 100.0 : 0.0;
        string sign = pct >= 0 ? "+" : "";
        string timeStr = _isDaily ? c.Begin.ToString("dd.MM.yyyy") : c.Begin.ToString("dd.MM HH:mm");

        BarInfoText.Text =
            $"{timeStr}   " +
            $"O:{c.Open.ToString(_viewPriceFmt)}  " +
            $"H:{c.High.ToString(_viewPriceFmt)}  " +
            $"L:{c.Low.ToString(_viewPriceFmt)}  " +
            $"C:{c.Close.ToString(_viewPriceFmt)} ({sign}{pct:F2}%)  " +
            $"V:{c.Volume:N0}";

        BarInfoPanel.Visibility = Visibility.Visible;
    }

    // --- Выдвижной бар слева ---

    private void ToolbarZone_MouseEnter(object sender, MouseEventArgs e)
    {
        _toolbarHideTimer.Stop();
        SetToolbarVisible(true);
    }

    private void ToolbarZone_MouseLeave(object sender, MouseEventArgs e)
    {
        _toolbarHideTimer.Stop();
        _toolbarHideTimer.Start();
    }

    private void SetToolbarVisible(bool visible)
    {
        if (_isToolbarVisible == visible) return;
        _isToolbarVisible = visible;

        ToolBar.IsHitTestVisible = visible;

        var dur = TimeSpan.FromMilliseconds(140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        //ToolbarTransform.BeginAnimation(
        //    TranslateTransform.XProperty,
        //    new DoubleAnimation(visible ? 0 : -46, dur) { EasingFunction = ease });

        if (ToolBar.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(visible ? 0 : -46, dur) { EasingFunction = ease });
        }

        ToolBar.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(visible ? 1.0 : 0.0, dur) { EasingFunction = ease });
    }

    private static DoubleCollection CreateFrozenDash(double dash, double gap)
    {
        var dc = new DoubleCollection { dash, gap };
        dc.Freeze();
        return dc;
    }

    // Методы для инерции пермещения графика
    private void StartInertia()
    {
        if (_isCoasting) return;
        _velX = Math.Clamp(_velX, -4500, 4500);
        _velY = Math.Clamp(_velY, -4500, 4500);
        _lastRenderTime = TimeSpan.Zero;
        _isCoasting = true;
        CompositionTarget.Rendering += OnInertiaFrame;
    }

    private void StopInertia()
    {
        if (!_isCoasting) return;
        _isCoasting = false;
        _velX = 0;
        _velY = 0;
        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering -= OnInertiaFrame;
    }

    private void OnInertiaFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;

        // Пропускаем дублирующие вызовы WPF внутри одного VSync-кадра
        if (args.RenderingTime == _lastRenderTime)
            return;

        double dt = _lastRenderTime == TimeSpan.Zero
            ? 1.0 / 60.0
            : (args.RenderingTime - _lastRenderTime).TotalSeconds;
        _lastRenderTime = args.RenderingTime;
        dt = Math.Clamp(dt, 0.001, 0.05);

        _panX += _velX * dt;
        if (!_isAutoScale)
            _panY += _velY * dt;

        double decay = Math.Exp(-InertiaFriction * dt);
        _velX *= decay;
        _velY *= decay;

        DrawChart();

        // Порог 25 px/s убирает медленное «пошаговое» дотягивание по 1 пикселю в конце
        if (Math.Abs(_velX) < 25 && Math.Abs(_velY) < 25)
            StopInertia();
    }

    private static Brush FreezeBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Brush FreezeBrush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
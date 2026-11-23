using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Terminallways.Core.Data;
using Terminallways.Core.Indicators;
using Terminallways.Core.Models;

namespace Terminallways.Desktop;

public partial class MainWindow : Window
{
    private readonly IMarketDataProvider _provider = new MoexIssProvider();
    private readonly LiveCandleFeed _feed;
    private readonly DispatcherTimer _timer;

    // Добавка для статуса рынка
    private static readonly Brush SessionActiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
    private static readonly Brush SessionPauseBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
    private static readonly Brush SessionClosedBrush = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)); // полупрозрачный серый под фон

    private string? _lastTradingStatus;
    private DateTime _lastStatusCheckUtc = DateTime.MinValue;

    //private int _selectedTfMinutes = 3;

    // Добавка для TimeframeSpec.cs
    private TimeframeSpec _currentTf = TimeframeSpec.Presets["3M"];
    private int _loadedMoexInterval = -1;
    

    private bool _useHeikinAshi = true;
    private bool _isReady;

    // Проверка, включен ли режим обновления (x сек обновления и тп)
    private bool IsAutoEnabled => ChkAuto.IsChecked == true;

    public MainWindow()
    {
        InitializeComponent();

        // Подписка на авто-отключение кнопки при ручном перетаскивании
        ChartControl.AutoScaleChanged += isAuto => BtnAutoScale.IsChecked = isAuto;

        _feed = new LiveCandleFeed(_provider);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshTailAsync();

        Loaded += async (_, _) =>
        {
            _isReady = true;
            await LoadBaseAsync();
        };
    }

    private string CurrentTicker => ((ComboBoxItem)TickerBox.SelectedItem).Content.ToString()!;

    private async Task LoadBaseAsync()
    {
        _timer.Stop();
        BtnReload.IsEnabled = false;
        StatusText.Text = $"Загружаю базу {CurrentTicker} ({_currentTf.Code}, {_currentTf.DaysBack} дн.)...";

        try
        {
            int count = await _feed.InitializeAsync(
                CurrentTicker,
                daysBack: _currentTf.DaysBack,
                interval: _currentTf.BaseMoexInterval);

            // Добавка Статус сессии
            await RefreshTradingStatusIfDueAsync(force: true);

            _loadedMoexInterval = _currentTf.BaseMoexInterval;
            ApplyViewToChart(resetView: true);
            UpdateStatus($"База загружена ({count} свечей, интервал MOEX={_currentTf.BaseMoexInterval})");

            if (ChkAuto.IsChecked == true && count > 0)
                _timer.Start();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            BtnReload.IsEnabled = true;
        }
    }

    private async Task RefreshTailAsync()
    {
        try
        {
            var res = await _feed.UpdateTailAsync(overlap: 2);
            if (res is null) return;

            if (res.AddedCount > 0 || res.UpdatedLast)
                ApplyViewToChart(resetView: false);

            // Добавка - Статус сессии рынка
            await RefreshTradingStatusIfDueAsync();

            UpdateStatus($"Хвост: {res.ElapsedMs} мс | пришло: {res.FetchedCount} | новых: {res.AddedCount}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка хвоста: {ex.Message}";
        }
    }

    private void ApplyViewToChart(bool resetView)
    {
        var baseCandles = _feed.Candles;
        if (baseCandles.Count == 0)
        {
            ChartControl.SetCandles(Array.Empty<Candle>(), _useHeikinAshi, resetView, _currentTf.IsDailyOrHigher);
            return;
        }

        var tfCandles = _currentTf.ResampleMinutes > 1
            ? TimeframeResampler.Resample(baseCandles, _currentTf.ResampleMinutes)
            : baseCandles;

        var displayCandles = _useHeikinAshi ? HeikinAshiConverter.Convert(tfCandles) : tfCandles;

        // Косметическая добавка перед ChartControl
        ChartControl.Ticker = CurrentTicker;

        ChartControl.SetCandles(displayCandles, _useHeikinAshi, resetView, _currentTf.IsDailyOrHigher);
    }

    private void UpdateStatus(string prefix)
    {
        // Добавка - Статус сессии рынка
        UpdateSessionIndicator();

        if (_feed.Candles.Count == 0)
        {
            StatusText.Text = prefix;
            return;
        }

        var last = _feed.Candles[^1];
        var lag = DateTime.Now - last.End;
        string lagText = lag < TimeSpan.Zero
            ? "00:00"
            : (lag.TotalHours >= 1 ? lag.ToString(@"hh\:mm\:ss") : lag.ToString(@"mm\:ss"));

        string mode = _useHeikinAshi ? "Heikin Ashi" : "Обычные";
        StatusText.Text =
            $"{CurrentTicker} | {_currentTf.Code} | {mode} | " +
            $"Последняя: {last.Close} ({last.End:dd.MM HH:mm:ss}, лаг {lagText}) | {prefix}";
    }

    private async void TickerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        await LoadBaseAsync();
    }

    private async void Tf_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb &&
            rb.Tag?.ToString() is string code &&
            TimeframeSpec.Presets.TryGetValue(code, out var newTf))
        {
            _currentTf = newTf;
            if (!_isReady) return;

            // Если базовый интервал MOEX тот же (например 3M -> 2M -> 15M), сеть вообще не трогаем
            if (_loadedMoexInterval == _currentTf.BaseMoexInterval && _feed.Candles.Count > 0)
            {
                ApplyViewToChart(resetView: true);
                UpdateStatus("Таймфрейм переключён локально");
            }
            else
            {
                await LoadBaseAsync();
            }
        }
    }

    private void ChartModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChartModeBox.SelectedItem is ComboBoxItem item)
        {
            _useHeikinAshi = item.Tag?.ToString() == "HA";
            if (_isReady)
            {
                ApplyViewToChart(resetView: false);
                UpdateStatus("Режим свечей переключён");
            }
        }
    }

    private async void BtnReload_Click(object sender, RoutedEventArgs e) => await LoadBaseAsync();
    private void BtnResetView_Click(object sender, RoutedEventArgs e) => ChartControl.ResetView();

    private async void ChkAuto_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;

        if (IsAutoEnabled)
        {
            if (_feed.Candles.Count > 0)
            {
                await RefreshTradingStatusIfDueAsync(force: true);
                UpdateSessionIndicator();

                // Повторная проверка на случай, если галочку успели снять за время запроса
                if (IsAutoEnabled)
                    _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
            UpdateSessionIndicator();
        }
    }

    // Косметическая добавка: Нужен чтобы изменение IsChecked их кода не вызывало закцикливание

    private void BtnAutoScale_Click(object sender, RoutedEventArgs e)
    {
        ChartControl.IsAutoScale = BtnAutoScale.IsChecked == true;
    }

    // Обновление статуса сессии рынка
    private async Task RefreshTradingStatusIfDueAsync(bool force = false)
    {
        if (!IsAutoEnabled)
            return;

        if (!force && (DateTime.UtcNow - _lastStatusCheckUtc).TotalSeconds < 60)
            return;

        try
        {
            _lastTradingStatus = await _provider.GetTradingStatusAsync(CurrentTicker);
            _lastStatusCheckUtc = DateTime.UtcNow;
        }
        catch
        {
            // Если запрос статуса временно не прошёл — индикатор сработает по лагу свечей
        }
    }

    private void UpdateSessionIndicator()
    {
        if (!IsAutoEnabled)
        {
            SessionIndicator.Fill = SessionClosedBrush;
            SessionHitBox.ToolTip = "Спящий режим (автообновление выключено)";
            return;
        }

        DateTime? lastEnd = _feed.Candles.Count > 0 ? _feed.Candles[^1].End : null;
        var info = MarketSessionEvaluator.Evaluate(_lastTradingStatus, lastEnd, _currentTf.IsDailyOrHigher);

        SessionIndicator.Fill = info.Status switch
        {
            MarketSessionStatus.Active => SessionActiveBrush,
            MarketSessionStatus.PauseOrAuction => SessionPauseBrush,
            _ => SessionClosedBrush
        };

        SessionHitBox.ToolTip = info.Tooltip;

        var desiredInterval = info.Status == MarketSessionStatus.Closed
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(3);

        if (_timer.Interval != desiredInterval)
            _timer.Interval = desiredInterval;
    }
}
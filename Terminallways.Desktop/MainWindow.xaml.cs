using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Terminallways.Core.Data;
using Terminallways.Core.Indicators;
using Terminallways.Core.Models;
using Terminallways.Desktop.Settings;
using System.Windows.Input;

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

    // Удалено после фикса 1ч, 1д таймфрейма. Переработан метод работы сессии.
    //private int _loadedMoexInterval = -1; // Вместо него ниже: 
    private readonly Dictionary<string, IReadOnlyList<Candle>> _historyCache = new();


    private bool _useHeikinAshi = true;
    private bool _isReady;

    // Проверка, включен ли режим обновления (x сек обновления и тп)
    private bool IsAutoEnabled => ChkAuto.IsChecked == true;

    // Добавка Burn
    private double _normalRefreshSeconds = 3.0;
    private double _burnRefreshSeconds = 1.0;
    private bool _isBurnMode;

    // Добавка настроек терминала
    private readonly AppSettings _settings;

    // Конструктор MainWindow()
    public MainWindow()
    {
        InitializeComponent();

        // Подписка StateChanged, для компенсации отсутпа в развёрнутом окне
        StateChanged += (_, _) => UpdateWindowChromeState();
        UpdateWindowChromeState();

        // Подписка на авто-отключение кнопки при ручном перетаскивании
        ChartControl.AutoScaleChanged += isAuto => BtnAutoScale.IsChecked = isAuto;

        // Добавка Burn
        ChartControl.BurnModeToggled += OnBurnModeToggled;
        UpdateAutoCheckBoxLabel();

        _feed = new LiveCandleFeed(_provider);

        // Время обновления графика (дефолт = TimeSpan.FromSeconds(1))
        // Дополнительно в UpdateSessionIndicator, также заменять данный параметр
        // Для использования миллисекунд: с FromSeconds на TimeSpan.FromMilliseconds = 500
        // 250 мс = 488 B/s
        // 3 с = 122 B/s

        //_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        // Добавка Burn
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_normalRefreshSeconds) };

        _timer.Tick += async (_, _) => await RefreshTailAsync();

        // Добавка настроек терминала
        _settings = AppSettings.Load();
        SettingsOverlay.BindSettings(_settings);
        SettingsOverlay.SettingsChanged += ApplySettings;

        ChartControl.SettingsRequested += () =>
            SettingsOverlay.Toggle(RootGrid.ActualWidth, RootGrid.ActualHeight);

        ApplySettings(_settings);

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
        _historyCache.Clear();

        string ticker = CurrentTicker;
        StatusText.Text = $"Загружаю живую 1m базу {ticker} (5 дней)...";

        try
        {
            // Живой поток ВСЕГДА на 1m — он питает все таймфреймы и индикатор сессии
            int count = await _feed.InitializeAsync(ticker, daysBack: 5, interval: 1);

            if (_currentTf.HistoryMoexInterval is not null)
            {
                StatusText.Text = $"Догружаю историю {ticker} ({_currentTf.Code}, {_currentTf.HistoryDaysBack} дн.)...";
                await EnsureHistoryLoadedAsync(ticker, _currentTf);
            }

            await RefreshTradingStatusIfDueAsync(force: true);
            ApplyViewToChart(resetView: true);
            UpdateStatus($"База 1m: {count} св.");

            if (IsAutoEnabled && count > 0)
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

    // Добавлен метод после фикса таймфреймов 1ч, 1д
    private async Task EnsureHistoryLoadedAsync(string ticker, TimeframeSpec tf)
    {
        if (tf.HistoryMoexInterval is not int histInterval)
            return;

        if (_historyCache.ContainsKey(tf.Code))
            return;

        DateTime from = DateTime.Today.AddDays(-tf.HistoryDaysBack);
        var raw = await _provider.GetCandlesAsync(ticker, from, null, histInterval);

        // Защита от гонки, если тикер переключили во время сетевого запроса
        if (ticker != CurrentTicker)
            return;

        IReadOnlyList<Candle> prepared = (!tf.IsDailyOrHigher && tf.ResampleMinutes > histInterval)
            ? TimeframeResampler.Resample(raw, tf.ResampleMinutes)
            : raw;

        _historyCache[tf.Code] = prepared;
    }

    private async Task RefreshTailAsync()
    {
        try
        {
            if (_feed.Ticker != CurrentTicker)
                return;

            var res = await _feed.UpdateTailAsync(overlap: 2);
            if (res is null) return;

            await RefreshTradingStatusIfDueAsync();

            if (res.AddedCount > 0 || res.UpdatedLast)
                ApplyViewToChart(resetView: false);

            UpdateStatus($"Хвост 1m: {res.ElapsedMs} мс | пришло: {res.FetchedCount} | новых: {res.AddedCount}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка хвоста: {ex.Message}";
        }
    }

    private void ApplyViewToChart(bool resetView)
    {
        var live1m = _feed.Candles;
        if (live1m.Count == 0)
        {
            ChartControl.Ticker = CurrentTicker;
            ChartControl.SetCandles(Array.Empty<Candle>(), _useHeikinAshi, resetView, _currentTf.IsDailyOrHigher);
            return;
        }

        // 1. Всегда собираем живой хвост выбранного ТФ из 1m-потока
        IReadOnlyList<Candle> liveTf = _currentTf.ResampleMinutes > 1
            ? TimeframeResampler.Resample(live1m, _currentTf.ResampleMinutes)
            : live1m;

        // 2. Для старших ТФ (1Ч, 1Д) склеиваем глубокую историю с живым хвостом
        IReadOnlyList<Candle> tfCandles = (_currentTf.HistoryMoexInterval is not null &&
                                           _historyCache.TryGetValue(_currentTf.Code, out var history))
            ? TimeframeResampler.MergeHistoryWithLive(history, liveTf)
            : liveTf;

        var displayCandles = _useHeikinAshi ? HeikinAshiConverter.Convert(tfCandles) : tfCandles;

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

            if (_currentTf.HistoryMoexInterval is not null && !_historyCache.ContainsKey(_currentTf.Code))
            {
                string ticker = CurrentTicker;
                StatusText.Text = $"Догружаю историю {ticker} ({_currentTf.Code}, {_currentTf.HistoryDaysBack} дн.)...";
                try
                {
                    await EnsureHistoryLoadedAsync(ticker, _currentTf);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Ошибка истории {_currentTf.Code}: {ex.Message}";
                    return;
                }

                // Если пока качали историю, пользователь успел переключиться — не перебиваем экран
                if (ticker != CurrentTicker || _currentTf.Code != code)
                    return;

                ApplyViewToChart(resetView: true);
                UpdateStatus($"История {_currentTf.Code} загружена и склеена с 1m");
            }
            else
            {
                ApplyViewToChart(resetView: true);
                UpdateStatus("Таймфрейм переключён локально");
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
        var info = MarketSessionEvaluator.Evaluate(_lastTradingStatus, lastEnd);

        SessionIndicator.Fill = info.Status switch
        {
            MarketSessionStatus.Active => SessionActiveBrush,
            MarketSessionStatus.PauseOrAuction => SessionPauseBrush,
            _ => SessionClosedBrush
        };

        SessionHitBox.ToolTip = info.Tooltip;

        //var desiredInterval = info.Status == MarketSessionStatus.Closed
        //    ? TimeSpan.FromSeconds(30)
        //    : TimeSpan.FromSeconds(3); // Здесь дополнительно редактируетса время автообновления хвоста графика

        //if (_timer.Interval != desiredInterval)
        //    _timer.Interval = desiredInterval;

        double activeSeconds = _isBurnMode ? _burnRefreshSeconds : _normalRefreshSeconds;
        var desiredInterval = info.Status == MarketSessionStatus.Closed
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(Math.Max(0.25, activeSeconds));

        if (_timer.Interval != desiredInterval)
            _timer.Interval = desiredInterval;
    }

    // Методы для Burn
    private void OnBurnModeToggled(bool enabled)
    {
        _isBurnMode = enabled;
        UpdateAutoCheckBoxLabel();
        UpdateSessionIndicator();
    }

    private void UpdateAutoCheckBoxLabel()
    {
        ChkAuto.Content = _isBurnMode
            ? $"Авто ({_burnRefreshSeconds:0.##} сек 🔥)"
            : $"Авто ({_normalRefreshSeconds:0.##} сек)";
    }

    // Методы после добавления окна настроек терминала
    private void ApplySettings(AppSettings s)
    {
        ChartControl.EnablePanInertia = s.EnablePanInertia;
        ChartControl.InertiaFriction = s.GetInertiaFriction();

        // Частицы снежинки
        ParticleOverlay.Intensity = s.ParticleIntensity;
        ParticleOverlay.IsEffectEnabled = s.EnableParticles;

        ParticleOverlay.Intensity = s.ParticleIntensity;
        ParticleOverlay.SpeedMultiplier = s.ParticleSpeedMultiplier;
        ParticleOverlay.IsEffectEnabled = s.EnableParticles;
        // -

        _normalRefreshSeconds = s.NormalRefreshSeconds;
        UpdateAutoCheckBoxLabel();

        if (_isReady)
            UpdateSessionIndicator();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsOverlay.IsOpen)
        {
            SettingsOverlay.Close();
            e.Handled = true;
        }
    }

    // Метод компенсации отступа окна Window Chrome
    private void UpdateWindowChromeState()
    {
        bool isMax = WindowState == WindowState.Maximized;
        ChromeRootGrid.Margin = isMax ? new Thickness(7) : new Thickness(0);

        // Частицы снежинки
        ParticleOverlay.SetPaused(WindowState == WindowState.Minimized);
        // -

        TitleBar.UpdateWindowState(WindowState);
    }
}
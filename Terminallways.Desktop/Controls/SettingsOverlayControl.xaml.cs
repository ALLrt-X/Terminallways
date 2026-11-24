using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Terminallways.Desktop.Settings;

namespace Terminallways.Desktop.Controls;

public partial class SettingsOverlayControl : UserControl
{
    private const double MinWindowWidth = 460;
    private const double MinWindowHeight = 300;

    public event Action<AppSettings>? SettingsChanged;

    public bool IsOpen => Visibility == Visibility.Visible;

    private AppSettings _settings = new();
    private bool _isBinding;
    private bool _hasInitialGeometry;

    private bool _isDragging;
    private Point _dragStartInHost;
    private double _dragOriginLeft;
    private double _dragOriginTop;

    private readonly DispatcherTimer _saveDebounceTimer;

    // Добавка чек
    private bool _isReady;

    //public SettingsOverlayControl()
    //{
    //    InitializeComponent();

    //    _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
    //    _saveDebounceTimer.Tick += (_, _) =>
    //    {
    //        _saveDebounceTimer.Stop();
    //        _settings.Save();
    //    };
    //}
    public SettingsOverlayControl()
    {
        _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveDebounceTimer.Tick += (_, _) =>
        {
            _saveDebounceTimer.Stop();
            _settings.Save();
        };

        InitializeComponent();
        _isReady = true;
    }

    public void BindSettings(AppSettings settings)
    {
        _settings = settings;
        SyncUiFromSettings();
    }

    public void Open(double hostWidth, double hostHeight)
    {
        SyncUiFromSettings();
        Visibility = Visibility.Visible;

        if (!_hasInitialGeometry && hostWidth > 0 && hostHeight > 0)
        {
            double w = Math.Max(MinWindowWidth, hostWidth * 0.75);
            double h = Math.Max(MinWindowHeight, hostHeight * 0.75);
            w = Math.Min(w, Math.Max(MinWindowWidth, hostWidth - 16));
            h = Math.Min(h, Math.Max(MinWindowHeight, hostHeight - 16));

            WindowFrame.Width = w;
            WindowFrame.Height = h;
            Canvas.SetLeft(WindowFrame, Math.Max(8, (hostWidth - w) / 2.0));
            Canvas.SetTop(WindowFrame, Math.Max(8, (hostHeight - h) / 2.0));
            _hasInitialGeometry = true;
        }
        else
        {
            ClampToHostBounds(hostWidth, hostHeight);
        }
    }

    public void Close()
    {
        if (_saveDebounceTimer.IsEnabled)
        {
            _saveDebounceTimer.Stop();
            _settings.Save();
        }

        Visibility = Visibility.Collapsed;
    }

    public void Toggle(double hostWidth, double hostHeight)
    {
        if (IsOpen) Close();
        else Open(hostWidth, hostHeight);
    }

    private void SyncUiFromSettings()
    {
        _isBinding = true;
        try
        {
            ChkInertia.IsChecked = _settings.EnablePanInertia;
            SliderInertia.Value = _settings.InertiaIntensity;
            SliderInertia.IsEnabled = _settings.EnablePanInertia;
            TxtInertiaValue.Text = $"{_settings.InertiaIntensity:0.0}";

            SliderRefresh.Value = _settings.NormalRefreshSeconds;
            TxtRefreshValue.Text = $"{_settings.NormalRefreshSeconds:0.#} сек";

            // Частицы снежинок
            ChkParticles.IsChecked = _settings.EnableParticles;
            SliderParticles.Value = _settings.ParticleIntensity;
            SliderParticles.IsEnabled = _settings.EnableParticles;
            TxtParticlesValue.Text = $"{_settings.ParticleIntensity:0.0}";

            SliderParticleSpeed.Value = _settings.ParticleSpeedMultiplier;
            SliderParticleSpeed.IsEnabled = _settings.EnableParticles;
            TxtParticleSpeedValue.Text = $"{_settings.ParticleSpeedMultiplier:0.0}×";
        }
        finally
        {
            _isBinding = false;
        }
    }

    private void PushUiToSettings()
    {
        //if (!IsInitialized || _isBinding) return;
        if (!_isReady || _isBinding) return;

        if (_isBinding) return;

        // Частицы снежинки
        _settings.EnableParticles = ChkParticles.IsChecked == true;
        _settings.ParticleIntensity = SliderParticles.Value;

        _settings.ParticleSpeedMultiplier = SliderParticleSpeed.Value;
        // --


        _settings.EnablePanInertia = ChkInertia.IsChecked == true;
        _settings.InertiaIntensity = SliderInertia.Value;
        _settings.NormalRefreshSeconds = SliderRefresh.Value;
        _settings.Normalize();

        // Частицы снежинки
        SliderParticles.IsEnabled = _settings.EnableParticles;
        TxtParticlesValue.Text = $"{_settings.ParticleIntensity:0.0}";

        SliderParticleSpeed.IsEnabled = _settings.EnableParticles;
        TxtParticleSpeedValue.Text = $"{_settings.ParticleSpeedMultiplier:0.0}×";
        // --

        SliderInertia.IsEnabled = _settings.EnablePanInertia;
        TxtInertiaValue.Text = $"{_settings.InertiaIntensity:0.0}";
        TxtRefreshValue.Text = $"{_settings.NormalRefreshSeconds:0.#} сек";

        SettingsChanged?.Invoke(_settings);

        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void SettingControl_Changed(object sender, RoutedEventArgs e) => PushUiToSettings();

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PushUiToSettings();

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        _settings.EnablePanInertia = defaults.EnablePanInertia;
        _settings.InertiaIntensity = defaults.InertiaIntensity;
        _settings.NormalRefreshSeconds = defaults.NormalRefreshSeconds;

        // Частицы снежинки
        _settings.EnableParticles = defaults.EnableParticles;
        _settings.ParticleIntensity = defaults.ParticleIntensity;

        _settings.ParticleSpeedMultiplier = defaults.ParticleSpeedMultiplier;

        SyncUiFromSettings();
        SettingsChanged?.Invoke(_settings);
        _settings.Save();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        //if (!IsLoaded) return;
        //if (!IsInitialized) return;
        if (!_isReady) return;

        bool showAll = TabAll.IsChecked == true;

        // Частицы снежинки
        SectionVisual.Visibility = (showAll || TabVisual.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        // -
        SectionChart.Visibility = (showAll || TabChart.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        SectionFeed.Visibility = (showAll || TabFeed.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Перетаскивание окна за шапку ---

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) is not null)
            return;

        _isDragging = true;
        _dragStartInHost = e.GetPosition(OverlayHost);
        _dragOriginLeft = Canvas.GetLeft(WindowFrame);
        _dragOriginTop = Canvas.GetTop(WindowFrame);
        if (double.IsNaN(_dragOriginLeft)) _dragOriginLeft = 0;
        if (double.IsNaN(_dragOriginTop)) _dragOriginTop = 0;

        HeaderBar.CaptureMouse();
    }

    private void HeaderBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        Point current = e.GetPosition(OverlayHost);
        double newLeft = _dragOriginLeft + (current.X - _dragStartInHost.X);
        double newTop = _dragOriginTop + (current.Y - _dragStartInHost.Y);

        double maxLeft = Math.Max(0, OverlayHost.ActualWidth - 120);
        double maxTop = Math.Max(0, OverlayHost.ActualHeight - 40);

        Canvas.SetLeft(WindowFrame, Math.Clamp(newLeft, -(WindowFrame.Width - 120), maxLeft));
        Canvas.SetTop(WindowFrame, Math.Clamp(newTop, 0, maxTop));
    }

    private void HeaderBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        HeaderBar.ReleaseMouseCapture();
    }

    // --- Изменение размера окна ---

    private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double left = double.IsNaN(Canvas.GetLeft(WindowFrame)) ? 0 : Canvas.GetLeft(WindowFrame);
        double maxW = Math.Max(MinWindowWidth, OverlayHost.ActualWidth - left - 6);
        WindowFrame.Width = Math.Clamp(WindowFrame.Width + e.HorizontalChange, MinWindowWidth, maxW);
    }

    private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double top = double.IsNaN(Canvas.GetTop(WindowFrame)) ? 0 : Canvas.GetTop(WindowFrame);
        double maxH = Math.Max(MinWindowHeight, OverlayHost.ActualHeight - top - 6);
        WindowFrame.Height = Math.Clamp(WindowFrame.Height + e.VerticalChange, MinWindowHeight, maxH);
    }

    private void ResizeCorner_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeRight_DragDelta(sender, e);
        ResizeBottom_DragDelta(sender, e);
    }

    private void OverlayHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_hasInitialGeometry && IsOpen)
            ClampToHostBounds(e.NewSize.Width, e.NewSize.Height);
    }

    private void ClampToHostBounds(double hostW, double hostH)
    {
        if (hostW <= 0 || hostH <= 0) return;

        if (WindowFrame.Width > hostW - 12)
            WindowFrame.Width = Math.Max(MinWindowWidth, hostW - 12);
        if (WindowFrame.Height > hostH - 12)
            WindowFrame.Height = Math.Max(MinWindowHeight, hostH - 12);

        double left = double.IsNaN(Canvas.GetLeft(WindowFrame)) ? 0 : Canvas.GetLeft(WindowFrame);
        double top = double.IsNaN(Canvas.GetTop(WindowFrame)) ? 0 : Canvas.GetTop(WindowFrame);

        Canvas.SetLeft(WindowFrame, Math.Clamp(left, 0, Math.Max(0, hostW - 120)));
        Canvas.SetTop(WindowFrame, Math.Clamp(top, 0, Math.Max(0, hostH - 40)));
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T found) return found;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
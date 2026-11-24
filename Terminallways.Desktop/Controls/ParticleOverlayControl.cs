using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Terminallways.Desktop.Controls;

public sealed class ParticleOverlayControl : FrameworkElement
{
    private struct Particle
    {
        public double X;
        public double Y;
        public double SpeedY;
        public double DriftAmp;
        public double DriftFreq;
        public double DriftPhase;
        public double Size;
        public double Angle;
        public double AngularSpeed;
        public double Age;
        public double MaxLife;
        public double FadeStartRatio; // с какой доли жизни начинает угасать альфа
        public double MaxOpacity;
        public int TextureIndex;
    }

    private static readonly Brush FallbackSnowBrush = CreateFallbackBrush();

    private readonly List<Particle> _particles = new(128);
    private readonly List<BitmapImage> _textures = new();
    private readonly Random _rng = new();

    private bool _isEffectEnabled;
    private bool _isPaused;
    private bool _isSubscribed;
    private double _intensity = 5.0; // 1..10
    private double _speedMultiplier = 1.0;

    public double SpeedMultiplier
    {
        get => _speedMultiplier;
        set => _speedMultiplier = Math.Clamp(value, 0.3, 2.5);
    }
    private double _spawnAccumulator;
    private TimeSpan _lastRenderTime = TimeSpan.Zero;

    public string TexturesRelativeFolder { get; set; } = Path.Combine("Resources", "Textures", "Particles");

    public bool IsEffectEnabled
    {
        get => _isEffectEnabled;
        set
        {
            if (_isEffectEnabled == value) return;
            _isEffectEnabled = value;
            UpdateLoopState();
        }
    }

    public double Intensity
    {
        get => _intensity;
        set => _intensity = Math.Clamp(value, 1.0, 10.0);
    }

    public ParticleOverlayControl()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);

        Loaded += (_, _) =>
        {
            ReloadTextures();
            UpdateLoopState();
        };
        Unloaded += (_, _) => StopLoop();
    }

    public void SetPaused(bool paused)
    {
        if (_isPaused == paused) return;
        _isPaused = paused;
        UpdateLoopState();
    }

    /// <summary>
    /// Загружает все .png файлы из папки частиц в память (без блокировки файлов на диске).
    /// </summary>
    public void ReloadTextures()
    {
        _textures.Clear();

        string folder = Path.Combine(AppContext.BaseDirectory, TexturesRelativeFolder);
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(file, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _textures.Add(bmp);
            }
            catch
            {
                // Пропускаем битые файлы, чтобы не ронять интерфейс
            }
        }
    }

    private void UpdateLoopState()
    {
        if (!_isEffectEnabled)
        {
            StopLoop();
            _particles.Clear();
            _spawnAccumulator = 0;
            Visibility = Visibility.Collapsed;
            InvalidateVisual();
            return;
        }

        Visibility = Visibility.Visible;

        if (_isPaused)
        {
            StopLoop();
            return;
        }

        StartLoop();
    }

    private void StartLoop()
    {
        if (_isSubscribed) return;
        _lastRenderTime = TimeSpan.Zero;
        _isSubscribed = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopLoop()
    {
        if (!_isSubscribed) return;
        _isSubscribed = false;
        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;
        if (args.RenderingTime == _lastRenderTime) return; // защита от двойного вызова WPF за 1 VSync

        double dt = _lastRenderTime == TimeSpan.Zero
            ? 1.0 / 60.0
            : (args.RenderingTime - _lastRenderTime).TotalSeconds;
        _lastRenderTime = args.RenderingTime;
        dt = Math.Clamp(dt, 0.001, 0.05);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        // 1. Спавн новых частиц пропорционально интенсивности и ширине окна
        double widthFactor = Math.Clamp(w / 1280.0, 0.5, 2.0);
        double spawnPerSec = (1.5 + _intensity * 2.2) * widthFactor;
        int maxParticles = (int)(25 + _intensity * 22 * widthFactor);

        _spawnAccumulator += spawnPerSec * dt;
        while (_spawnAccumulator >= 1.0)
        {
            _spawnAccumulator -= 1.0;
            if (_particles.Count < maxParticles)
                _particles.Add(CreateParticle(w));
        }

        // 2. Обновление физики
        double moveDt = dt * _speedMultiplier;
        double lifeDt = dt * Math.Clamp(_speedMultiplier, 0.45, 1.6);

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += lifeDt;

            if (p.Age >= p.MaxLife || p.Y - p.Size > h)
            {
                _particles.RemoveAt(i);
                continue;
            }

            p.Y += p.SpeedY * moveDt;
            p.X += Math.Sin(p.Age * p.DriftFreq + p.DriftPhase) * p.DriftAmp * moveDt;
            p.Angle += p.AngularSpeed * moveDt;

            _particles[i] = p;
        }

        InvalidateVisual();
    }

    private Particle CreateParticle(double hostWidth)
    {
        // Более крупные частицы летят чуть быстрее — лёгкий эффект глубины
        double sizeNorm = _rng.NextDouble();
        double size = 12.0 + sizeNorm * 22.0;                // 12..34 px
        double speedY = 26.0 + sizeNorm * 38.0 + _rng.NextDouble() * 18.0; // 26..82 px/s
        double maxLife = 4.5 + _rng.NextDouble() * 6.5;      // 4.5..11 сек (разная скорость угасания)
        double angSpeed = (_rng.NextDouble() * 2.0 - 1.0) * 75.0; // -75..+75 град/сек

        return new Particle
        {
            X = _rng.NextDouble() * hostWidth,
            Y = -size * 0.6,
            SpeedY = speedY,
            DriftAmp = 8.0 + _rng.NextDouble() * 22.0,
            DriftFreq = 0.8 + _rng.NextDouble() * 1.4,
            DriftPhase = _rng.NextDouble() * Math.PI * 2.0,
            Size = size,
            Angle = _rng.NextDouble() * 360.0,
            AngularSpeed = angSpeed,
            Age = 0.0,
            MaxLife = maxLife,
            FadeStartRatio = 0.45 + _rng.NextDouble() * 0.25, // начинает плавно исчезать на 45–70% жизни
            MaxOpacity = 0.35 + sizeNorm * 0.50,              // 0.35..0.85
            TextureIndex = _textures.Count > 0 ? _rng.Next(_textures.Count) : -1
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_isEffectEnabled || _particles.Count == 0)
            return;

        double h = ActualHeight;

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            double lifeRatio = p.Age / p.MaxLife;

            // Плавное появление в первые 0.35 сек
            double fadeIn = Math.Min(1.0, p.Age / 0.35);

            // Плавное затухание по времени жизни
            double fadeOut = lifeRatio > p.FadeStartRatio
                ? Math.Clamp(1.0 - (lifeRatio - p.FadeStartRatio) / (1.0 - p.FadeStartRatio), 0.0, 1.0)
                : 1.0;

            // Дополнительно мягко гасим у самой нижней границы окна
            if (h > 80 && p.Y > h - 80)
                fadeOut *= Math.Clamp((h - p.Y) / 80.0, 0.0, 1.0);

            double opacity = p.MaxOpacity * fadeIn * fadeOut;
            if (opacity <= 0.01) continue;

            dc.PushOpacity(opacity);

            if (p.TextureIndex >= 0 && p.TextureIndex < _textures.Count)
            {
                dc.PushTransform(new RotateTransform(p.Angle, p.X, p.Y));
                double half = p.Size * 0.5;
                dc.DrawImage(_textures[p.TextureIndex], new Rect(p.X - half, p.Y - half, p.Size, p.Size));
                dc.Pop();
            }
            else
            {
                // Фолбэк, пока в папке Particles нет PNG
                double r = p.Size * 0.22;
                dc.DrawEllipse(FallbackSnowBrush, null, new Point(p.X, p.Y), r, r);
            }

            dc.Pop();
        }
    }

    private static Brush CreateFallbackBrush()
    {
        var brush = new RadialGradientBrush(
            Color.FromArgb(230, 235, 245, 255),
            Color.FromArgb(0, 235, 245, 255));
        brush.Freeze();
        return brush;
    }
}
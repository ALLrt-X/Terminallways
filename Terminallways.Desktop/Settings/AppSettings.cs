using System.IO;
using System.Text.Json;

namespace Terminallways.Desktop.Settings;

public sealed class AppSettings
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    // Частицы (снежинки)
    public bool EnableParticles { get; set; } = true;
    public double ParticleIntensity { get; set; } = 2.5; // 1..10
    public double ParticleSpeedMultiplier { get; set; } = 0.75; // 0.3..2.5

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool EnablePanInertia { get; set; } = true;

    // Удобная шкала 1..10 (больше = мягче и длиннее накат; 6.0 ≈ прежние 8.5 трения)
    public double InertiaIntensity { get; set; } = 6.0;

    // Интервал обычного автообновления в секундах (Burn остаётся 1 сек)
    public double NormalRefreshSeconds { get; set; } = 3.0;

    public double GetInertiaFriction()
    {
        double clamped = Math.Clamp(InertiaIntensity, 1.0, 10.0);
        return Math.Clamp(15.0 - clamped * 1.1, 3.5, 14.5);
    }

    public void Normalize()
    {
        InertiaIntensity = Math.Round(Math.Clamp(InertiaIntensity, 1.0, 10.0), 1);
        NormalRefreshSeconds = Math.Round(Math.Clamp(NormalRefreshSeconds, 1.0, 30.0), 1);

        // Частицы снежинки
        ParticleIntensity = Math.Round(Math.Clamp(ParticleIntensity, 1.0, 10.0), 1);
        ParticleSpeedMultiplier = Math.Round(Math.Clamp(ParticleSpeedMultiplier, 0.3, 2.5), 1);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // При битом файле безопасно откатываемся на значения по умолчанию
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Normalize();
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Игнорируем временную блокировку файла
        }
    }
}
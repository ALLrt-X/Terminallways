using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Terminallways.Desktop.Controls;

public partial class ChartToolbarControl : UserControl
{
    public event Action<ChartToolMode>? ToolModeChanged;
    public event Action<bool>? BarInfoToggled;

    public ChartToolMode CurrentTool =>
        RbCrosshair.IsChecked == true ? ChartToolMode.Crosshair : ChartToolMode.Cursor;

    public bool IsBarInfoEnabled => BtnBarInfo.IsChecked == true;

    public ChartToolbarControl()
    {
        InitializeComponent();
        LoadCustomIcons();
    }

    private void LoadCustomIcons()
    {
        TryApplyPngIcon(IconCursorPng, IconCursorFallback, "tool_cursor.png");
        TryApplyPngIcon(IconCrosshairPng, IconCrosshairFallback, "tool_crosshair.png");
        TryApplyPngIcon(IconBarInfoPng, IconBarInfoFallback, "tool_barinfo.png");
    }

    private static void TryApplyPngIcon(Rectangle pngTarget, UIElement fallback, string fileName)
    {
        string path = System.IO.Path.Combine(
            AppContext.BaseDirectory, "Resources", "Textures", "Icons", fileName);

        if (!File.Exists(path))
            return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            // OpacityMask перекрашивает однотонный PNG (с прозрачным фоном) под цвет активной/неактивной кнопки.
            // Если PNG цветной и нужен как есть — замени OpacityMask на pngTarget.Fill = new ImageBrush(bmp) { Stretch = Stretch.Uniform };
            pngTarget.OpacityMask = new ImageBrush(bmp) { Stretch = Stretch.Uniform };
            pngTarget.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // При битом файле останется запасной вектор
        }
    }

    private void ToolRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ToolModeChanged?.Invoke(CurrentTool);
    }

    private void BtnBarInfo_Click(object sender, RoutedEventArgs e)
    {
        BarInfoToggled?.Invoke(IsBarInfoEnabled);
    }
}
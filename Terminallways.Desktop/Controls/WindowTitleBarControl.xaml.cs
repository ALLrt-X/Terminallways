using System.Windows;
using System.Windows.Controls;

namespace Terminallways.Desktop.Controls;

public partial class WindowTitleBarControl : UserControl
{
    public WindowTitleBarControl()
    {
        InitializeComponent();
    }

    public void UpdateWindowState(WindowState state)
    {
        bool isMax = state == WindowState.Maximized;
        IconMaximize.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        IconRestore.Visibility = isMax ? Visibility.Visible : Visibility.Collapsed;
        BtnMaximize.ToolTip = isMax ? "Восстановить" : "Развернуть";
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window w)
            w.WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window w)
        {
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }
}
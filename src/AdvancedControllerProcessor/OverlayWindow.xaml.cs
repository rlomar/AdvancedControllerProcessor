using System.Windows;
using System.Windows.Input;

namespace AdvancedControllerProcessor;

/// <summary>
/// In-app Rocket League-style overlay reference. Purely a demo HUD shown
/// inside the program — it is never injected over or into a game.
/// </summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
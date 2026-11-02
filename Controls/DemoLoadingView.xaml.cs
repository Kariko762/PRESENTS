using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FisPresentationPlatform.Controls;

public partial class DemoLoadingView : UserControl
{
    public DemoLoadingView() => InitializeComponent();
    public string Message { get => LoadingMessage.Text; set => LoadingMessage.Text = value; }

    private void LoadingVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Spinner?.RenderTransform is not RotateTransform rotation) return;
        rotation.BeginAnimation(RotateTransform.AngleProperty, IsVisible
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }
}
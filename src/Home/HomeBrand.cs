// Stackshot - The sidebar logo, alive while the window is on screen: every so often a glint crosses the tile, and once
// more when the pointer comes over it. In between only a timer waits, and nothing runs while the window is hidden or
// minimized. A contrast theme keeps it still.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Stackshot
{
    public partial class HomeWindow
    {
        const double BrandEveryS = 14, BrandFirstS = 2.5;
        DispatcherTimer brandTimer;
        Action brandPlay;

        FrameworkElement BrandMark()
        {
            Grid g = new Grid { Width = 30, Height = 30 };
            Image logo = new DecorImage { Width = 30, Height = 30, Source = Icon as ImageSource };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            g.Children.Add(logo);
            brandPlay = null;
            if (Ds.Brushes.High || logo.Source == null) return g;

            // A diagonal band of light, masked by the logo itself so it only lights the tile.
            TranslateTransform slide = new TranslateTransform(-1.3, 0);
            LinearGradientBrush shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), RelativeTransform = slide };
            shine.GradientStops.Add(new GradientStop(Ds.Argb(0, 255, 255, 255), 0.32));
            shine.GradientStops.Add(new GradientStop(Ds.Argb(0.6, 255, 255, 255), 0.5));
            shine.GradientStops.Add(new GradientStop(Ds.Argb(0, 255, 255, 255), 0.68));
            ImageBrush mask = new ImageBrush(logo.Source);
            mask.Freeze();
            g.Children.Add(new Rectangle { Fill = shine, OpacityMask = mask, IsHitTestVisible = false });

            brandPlay = delegate
            {
                DoubleAnimation sweep = new DoubleAnimation(-1.3, 1.3, TimeSpan.FromMilliseconds(780))
                {
                    BeginTime = TimeSpan.FromMilliseconds(90), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                slide.BeginAnimation(TranslateTransform.XProperty, sweep);
            };
            g.MouseEnter += delegate { if (brandPlay != null) brandPlay(); };
            return g;
        }

        // Runs only while the window is on screen; the first shot comes shortly after it shows.
        void BrandTimer(bool shown)
        {
            if (!shown) { if (brandTimer != null) brandTimer.Stop(); return; }
            if (brandTimer == null)
            {
                brandTimer = new DispatcherTimer(DispatcherPriority.Background);
                brandTimer.Tick += delegate
                {
                    brandTimer.Interval = TimeSpan.FromSeconds(BrandEveryS);
                    if (brandPlay != null && IsVisible && WindowState != WindowState.Minimized) brandPlay();
                };
            }
            if (brandTimer.IsEnabled) return;
            brandTimer.Interval = TimeSpan.FromSeconds(BrandFirstS);
            brandTimer.Start();
        }
    }
}

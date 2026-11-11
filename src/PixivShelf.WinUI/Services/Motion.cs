using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace PixivShelf.Services;

/// <summary>
/// Small, shared motion helpers. Everything here is a no-op when Windows
/// "animation effects" is turned off, and all durations are short enough to
/// stay out of the way of the interaction.
/// </summary>
internal static class Motion
{
    // Aligned with the WinUI defaults (83 / 167 / 250 ms).
    public const int Micro = 90;
    public const int Fast = 160;
    public const int Normal = 240;
    public const int Slow = 320;

    private static readonly UISettings UiSettings = new();
    private static readonly Dictionary<FrameworkElement, Storyboard> Running = new();
    private static readonly Dictionary<FrameworkElement, Storyboard> OpacityRuns = new();
    private static readonly HashSet<ButtonBase> PressButtons = new();

    public static bool Enabled
    {
        get
        {
            try { return UiSettings.AnimationsEnabled; }
            catch { return true; }
        }
    }

    // ------------------------------------------------------------------ //
    // composition helpers
    // ------------------------------------------------------------------ //
    private static CompositionEasingFunction EaseOut(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));

    private static CompositionEasingFunction EaseInOut(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f));

    private static Visual VisualOf(UIElement element) =>
        ElementCompositionPreview.GetElementVisual(element);

    public static void SetTranslation(FrameworkElement element, double x, double y)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        VisualOf(element).Properties.InsertVector3("Translation", new Vector3((float)x, (float)y, 0f));
    }

    public static void FadeTo(UIElement element, double to, int durationMs = Normal, int delayMs = 0, Action? completed = null)
    {
        if (!Enabled || element is FrameworkElement { IsLoaded: false })
        {
            element.Opacity = to;
            completed?.Invoke();
            return;
        }

        if (element is not FrameworkElement target)
        {
            element.Opacity = to;
            completed?.Invoke();
            return;
        }

        if (OpacityRuns.TryGetValue(target, out var previous))
        {
            previous.Stop();
            OpacityRuns.Remove(target);
        }

        var animation = new DoubleAnimation
        {
            From = element.Opacity,
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            OpacityRuns.Remove(target);
            completed?.Invoke();
        };
        OpacityRuns[target] = storyboard;
        storyboard.Begin();
    }

    public static void FadeIn(FrameworkElement element, int durationMs = Normal, int delayMs = 0, double fromOpacity = 0, double offsetY = 0, Action? completed = null)
    {
        element.Opacity = fromOpacity;
        if (offsetY != 0)
        {
            SetTranslation(element, 0, offsetY);
        }

        if (!Enabled || !element.IsLoaded)
        {
            element.Opacity = 1;
            if (offsetY != 0) SetTranslation(element, 0, 0);
            completed?.Invoke();
            return;
        }

        FadeTo(element, 1, durationMs, delayMs, completed);
        if (offsetY != 0)
        {
            SlideTo(element, 0, 0, durationMs, delayMs);
        }
    }

    public static void FadeOut(FrameworkElement element, int durationMs = Fast, int delayMs = 0, Action? completed = null)
    {
        if (!Enabled || !element.IsLoaded)
        {
            element.Opacity = 0;
            completed?.Invoke();
            return;
        }

        FadeTo(element, 0, durationMs, delayMs, completed);
    }

    public static void SlideTo(FrameworkElement element, double x, double y, int durationMs = Normal, int delayMs = 0)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = VisualOf(element);

        if (!Enabled || !element.IsLoaded)
        {
            visual.Properties.InsertVector3("Translation", new Vector3((float)x, (float)y, 0f));
            return;
        }

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, new Vector3((float)x, (float)y, 0f), EaseOut(visual.Compositor));
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        animation.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        visual.StartAnimation("Translation", animation);
    }

    public static void ScaleTo(FrameworkElement element, float scale, int durationMs = Fast, int delayMs = 0)
    {
        var visual = VisualOf(element);
        visual.CenterPoint = new Vector3((float)(element.ActualWidth / 2), (float)(element.ActualHeight / 2), 0f);

        if (!Enabled || !element.IsLoaded)
        {
            visual.Scale = new Vector3(scale, scale, 1f);
            return;
        }

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, new Vector3(scale, scale, 1f), EaseOut(visual.Compositor));
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        animation.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        visual.StartAnimation("Scale", animation);
    }

    public static void Pulse(FrameworkElement element, float peak = 1.06f, int durationMs = Fast)
    {
        var visual = VisualOf(element);
        visual.CenterPoint = new Vector3((float)(element.ActualWidth / 2), (float)(element.ActualHeight / 2), 0f);

        if (!Enabled || !element.IsLoaded)
        {
            visual.Scale = Vector3.One;
            return;
        }

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0.45f, new Vector3(peak, peak, 1f), EaseInOut(visual.Compositor));
        animation.InsertKeyFrame(1f, Vector3.One, EaseOut(visual.Compositor));
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation("Scale", animation);
    }

    // ------------------------------------------------------------------ //
    // height reveal (shell panels, source switch)
    // ------------------------------------------------------------------ //
    public static void Reveal(FrameworkElement element, bool show, int durationMs = Normal, double fixedHeight = double.NaN, double width = double.NaN, Action? completed = null)
    {
        if (Running.TryGetValue(element, out var previous))
        {
            previous.Stop();
            Running.Remove(element);
        }

        if (!Enabled || !element.IsLoaded)
        {
            element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            element.Height = double.NaN;
            element.Opacity = 1;
            completed?.Invoke();
            return;
        }

        if (show)
        {
            element.Visibility = Visibility.Visible;
            element.Height = double.NaN;
            element.Opacity = 0;

            var availableWidth = !double.IsNaN(width) && width > 0
                ? width
                : element.ActualWidth > 0
                    ? element.ActualWidth
                    : (element.Parent as FrameworkElement)?.ActualWidth ?? 0;

            element.Measure(new Size(availableWidth > 0 ? availableWidth : double.PositiveInfinity, double.PositiveInfinity));
            var target = !double.IsNaN(fixedHeight) ? fixedHeight : element.DesiredSize.Height;
            if (target < 0.5) target = 0.5;
            element.Height = 0;

            var storyboard = BuildReveal(element, target, durationMs, true, () =>
            {
                element.Height = double.IsNaN(fixedHeight) ? double.NaN : fixedHeight;
                element.Opacity = 1;
                Running.Remove(element);
                completed?.Invoke();
            });
            Running[element] = storyboard;
            storyboard.Begin();
            return;
        }

        if (element.Visibility != Visibility.Visible)
        {
            completed?.Invoke();
            return;
        }

        var current = element.ActualHeight;
        if (current <= 0.5)
        {
            element.Visibility = Visibility.Collapsed;
            element.Height = double.NaN;
            element.Opacity = 1;
            completed?.Invoke();
            return;
        }

        var hide = BuildReveal(element, 0, durationMs, false, () =>
        {
            element.Visibility = Visibility.Collapsed;
            element.Height = double.NaN;
            element.Opacity = 1;
            Running.Remove(element);
            completed?.Invoke();
        });
        Running[element] = hide;
        hide.Begin();
    }

    private static Storyboard BuildReveal(FrameworkElement element, double to, int durationMs, bool show, Action completed)
    {
        var storyboard = new Storyboard();

        var height = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(height, element);
        Storyboard.SetTargetProperty(height, "Height");
        storyboard.Children.Add(height);

        var opacity = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        storyboard.Children.Add(opacity);

        storyboard.Completed += (_, _) => completed();
        return storyboard;
    }

    // ------------------------------------------------------------------ //
    // progress / text
    // ------------------------------------------------------------------ //
    public static void SetProgress(ProgressBar bar, double value, int durationMs = 220)
    {
        if (!Enabled)
        {
            bar.Value = value;
            return;
        }

        if (Running.TryGetValue(bar, out var previous))
        {
            previous.Stop();
            Running.Remove(bar);
        }

        if (Math.Abs(bar.Value - value) < 0.0001)
        {
            return;
        }

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = bar.Value,
            To = value,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, bar);
        Storyboard.SetTargetProperty(animation, "Value");
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => Running.Remove(bar);
        Running[bar] = storyboard;
        storyboard.Begin();
    }

    public static void SwapText(TextBlock block, string text, int durationMs = 120)
    {
        if (block.Text == text) return;
        if (!Enabled || !block.IsLoaded)
        {
            block.Text = text;
            return;
        }

        FadeTo(block, 0.35, 60, 0, () =>
        {
            block.Text = text;
            FadeTo(block, 1, durationMs);
        });
    }

    public static void SwapContent(ContentControl control, object content, int durationMs = 120)
    {
        if (Equals(control.Content, content)) return;
        if (!Enabled || !control.IsLoaded)
        {
            control.Content = content;
            return;
        }

        FadeTo(control, 0.45, 60, 0, () =>
        {
            control.Content = content;
            FadeTo(control, 1, durationMs);
        });
    }

    // ------------------------------------------------------------------ //
    // button press feedback
    // ------------------------------------------------------------------ //
    public static void AttachPressScale(ButtonBase button)
    {
        if (!PressButtons.Add(button)) return;

        button.PointerPressed += (_, _) => ScaleTo(button, 0.985f, Micro);
        button.PointerReleased += (_, _) => ScaleTo(button, 1f, 140);
        button.PointerCanceled += (_, _) => ScaleTo(button, 1f, 140);
        button.PointerCaptureLost += (_, _) => ScaleTo(button, 1f, 140);
    }

}

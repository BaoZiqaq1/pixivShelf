using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PixivShelf.Services;

/// <summary>
/// Keeps a page's content at most <c>maxWidth</c> wide and centered when the
/// window is larger than that. Width is set explicitly so the content fills
/// the viewport at normal sizes and does not shrink to its desired size.
/// </summary>
internal static class PageSizing
{
    public static void Center(FrameworkElement content, FrameworkElement host, double maxWidth)
    {
        void Apply()
        {
            var width = host is ScrollViewer scroller && scroller.ViewportWidth > 0
                ? scroller.ViewportWidth
                : host.ActualWidth;

            if (width <= 0)
            {
                return;
            }

            content.Width = Math.Min(width, maxWidth);
        }

        host.SizeChanged += (_, _) => Apply();
        content.Loaded += (_, _) =>
        {
            Apply();
            content.DispatcherQueue.TryEnqueue(Apply);
        };

        Apply();
    }
}

// Stackshot - Grabacion page: "Probar microfono" on the microphone tile, the live level of the chosen microphone.
// MIT License - https://github.com/rubenitx/stackshot
using System.Windows;
using System.Windows.Controls;

namespace Stackshot
{
    public partial class HomeWindow
    {
        // Kept while the window lives: a rebuild of the page puts the same one back, so a test in progress goes on. It
        // ends by itself when the page changes or the window hides (MicCheck).
        MicCheck recMicTest;

        FrameworkElement RecMicTest()
        {
            if (indexing) return new Border();
            if (recMicTest == null) recMicTest = new MicCheck(delegate { return settings.MicDevice; }, true);
            Panel old = recMicTest.Parent as Panel;
            if (old != null) old.Children.Remove(recMicTest);
            recMicTest.Margin = new Thickness(0, 10, 0, 0);
            return recMicTest;
        }
    }
}

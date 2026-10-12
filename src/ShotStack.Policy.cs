// Stackshot - The stack following IT policies that change while it runs (see Policy).
// MIT License - https://github.com/rubenitx/stackshot
namespace Stackshot
{
    public partial class ShotStack
    {
        // A policy started or stopped forcing something (read again as a section of the main window was built): the
        // desktop mascot and the update checks follow at once; the window shows the rest as it builds the section.
        public void PolicyChanged()
        {
            HistoryIndex.Apply(settings);   // HistorySearch = 0 stops the reading and deletes the index
            MascotChanged();
            Updater.SettingsChanged();
        }
    }
}

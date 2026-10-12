// Stackshot - Pop-up menus for screen readers: each row is a menu item (checked or not) the reader can list and choose,
// and the keyboard's highlight is the focus.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Windows;
using System.Windows.Controls;

namespace Stackshot
{
    static class MacMenuAccess
    {
        // A menu row: what it runs, and its check mark. The highlight that follows the arrows marks the focus (no ring).
        internal static void Row(Border b, MenuEntry e, Action run)
        {
            ITap t = b as ITap;
            if (t == null) return;
            t.State.Do = run;
            t.State.ShowsFocus = true;
            if (e.Checked) t.State.On = delegate { return true; };
        }

        // A tile of the grid of commands: lit while the keyboard is on it, as while the mouse is.
        internal static void Highlight(Border b, Action<bool> paint)
        {
            ITap t = b as ITap;
            if (t != null) t.State.ShowsFocus = true;
            b.GotKeyboardFocus += delegate { paint(true); };
            b.LostKeyboardFocus += delegate { paint(b.IsMouseOver); };
        }
    }
}

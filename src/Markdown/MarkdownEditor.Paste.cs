// Stackshot - The editor's paste asks the window first: pictures and spreadsheet tables (Ctrl+V), plain text (Ctrl+Shift+V).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Windows.Input;

namespace Stackshot
{
    public sealed partial class MarkdownEditor
    {
        // Set by the window for Markdown documents: plain is Ctrl+Shift+V. True when it pasted (or found nothing to
        // paste); false lets the TextBox paste as always.
        internal Func<bool, bool> SmartPaste;

        bool PasteKey(KeyEventArgs e)
        {
            ModifierKeys m = e.KeyboardDevice.Modifiers;
            bool plain = e.Key == Key.V && m == (ModifierKeys.Control | ModifierKeys.Shift);
            if (!plain && !(e.Key == Key.V && m == ModifierKeys.Control) && !(e.Key == Key.Insert && m == ModifierKeys.Shift)) return false;
            return Pasted(plain);
        }

        bool Pasted(bool plain)
        {
            Func<bool, bool> f = SmartPaste;
            if (f == null) return false;
            try { return f(plain); }
            catch (Exception ex) { ShotStack.Log("Editor pegar: " + ex.Message); return false; }
        }
    }
}

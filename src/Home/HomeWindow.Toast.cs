// Stackshot - The main window's side of the notices: the button that leads to one of its sections.
// MIT License - https://github.com/rubenitx/stackshot
using System;

namespace Stackshot
{
    public partial class HomeWindow
    {
        // "Abrir Atajos"; Grabación is named in full so it doesn't read as opening a recording.
        internal static string NoticeButton(string page)
        {
            if (page == "record") return "Ajustes de grabaci\u00F3n";
            int i = Array.IndexOf(PageIds, page);
            return i < 0 ? "Abrir Stackshot" : "Abrir " + PageNames[i];
        }
    }
}

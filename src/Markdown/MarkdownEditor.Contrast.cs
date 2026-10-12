// Stackshot - The Markdown and XML editor's colours under a Windows contrast theme.
// MIT License - https://github.com/rubenitx/stackshot
using System.Windows.Media;

namespace Stackshot
{
    public sealed partial class MarkdownEditor
    {
        // Text in the text colour, headings, links, names and namespaces in HotTrack, marks and comments in GrayText;
        // search hits in translucent Highlight so the text drawn over them stays readable.
        void HighContrastTokens(Palette pal)
        {
            Brush text = Ds.Brush(pal.Label), link = Ds.Brush(pal.Link), faint = Ds.Brush(pal.Label3);
            for (int i = 0; i < brushes.Length; i++) brushes[i] = text;
            brushes[(int)Tok.Head] = brushes[(int)Tok.Link] = brushes[(int)Tok.Name] = brushes[(int)Tok.Ns] = link;
            brushes[(int)Tok.Mark] = brushes[(int)Tok.Faint] = brushes[(int)Tok.Comment] = faint;
            ink = text;
            hitBrush = Ds.Brush(Ds.WithAlpha(pal.Accent, 0.35));
            nowBrush = Ds.Brush(Ds.WithAlpha(pal.Accent, 0.55));
            Background = Ds.Brush(pal.Window);
        }
    }
}

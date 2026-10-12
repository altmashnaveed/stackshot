// Stackshot - The preview's document for printing: the light look whatever the theme, fonts a PDF can embed (the static
// Segoe UI and Consolas instead of the variable ones, which PDF readers would show at the wrong weight), pictures loaded at
// once and headings kept with the text that follows them.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Stackshot
{
    public sealed partial class MarkdownFlow
    {
        static readonly FontFamily PrintText = new FontFamily("Segoe UI"), PrintMono = new FontFamily("Consolas, Courier New");

        // No RichTextBox: only the blocks are made, on whatever thread prints.
        MarkdownFlow(Palette light, string folder, double tallestPicture)
        {
            pal = light;
            side = 0;
            printing = true;
            baseDir = folder;
            tallest = tallestPicture;
            ink = Ds.Brush(pal.Label);
            faint = Ds.Brush(pal.Label2);
            accent = Ds.Brush(pal.Accent);
            done = Ds.Brush(Ds.WithAlpha(pal.Label, 0.55));
            codeBack = Ds.Brush(Ds.Argb(0.06, 0, 0, 0));
            line = Ds.Brush(pal.Hairline);
        }

        // The whole document as one FlowDocument (the caller sets the page size and margins and paginates it); made gets
        // the element of each top-level block (null for none).
        internal static FlowDocument Printable(List<MdBlock> blocks, string folder, double tallestPicture, List<Block> made)
        {
            MarkdownFlow f = new MarkdownFlow(Palette.Legacy(false), folder, tallestPicture);   // prints keep their look
            FlowDocument d = new FlowDocument { FontFamily = PrintText, FontSize = 14.5, Foreground = f.ink, Background = Brushes.White, TextAlignment = TextAlignment.Left, ColumnWidth = 100000, ColumnGap = 0 };
            const double gap = 14;
            bool first = true;
            foreach (MdBlock b in blocks)
            {
                Block e = f.Make(b, 0);
                if (made != null) made.Add(e);
                if (e == null) continue;
                bool heading = b.Kind == MdKind.Heading;
                e.Margin = new Thickness(0, first ? 0 : heading ? gap + 8 : 0, 0, heading ? gap * 0.55 : gap);
                first = false;
                // A heading never ends a page alone; short code blocks are not split.
                if (heading && e is Paragraph) ((Paragraph)e).KeepWithNext = true;
                if (b.Kind == MdKind.Code && e is Paragraph && b.Text.Split('\n').Length <= 40) ((Paragraph)e).KeepTogether = true;
                Embeddable(e);
                d.Blocks.Add(e);
            }
            return d;
        }

        static void Embeddable(DependencyObject o)
        {
            TextElement t = o as TextElement;
            if (t != null)
            {
                FontFamily f = t.ReadLocalValue(TextElement.FontFamilyProperty) as FontFamily;
                if (f != null && (f == Ds.Text || f == Ds.Display)) t.FontFamily = PrintText;
                else if (f != null && f == Mono) t.FontFamily = PrintMono;
            }
            foreach (object c in LogicalTreeHelper.GetChildren(o))
            {
                DependencyObject d = c as DependencyObject;
                if (d != null && !(d is FrameworkElement)) Embeddable(d);
            }
        }
    }
}

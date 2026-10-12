// Stackshot - The player's chrome under a Windows contrast theme: the dark bar, cards and tips drawn over the video take
// the system colours (surfaces in Window, text and lines in WindowText, tracks in GrayText, the played part and the
// buttons that are on in Highlight). The video and everything exported from it never change.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using M = System.Windows.Media;

namespace Stackshot
{
    internal sealed partial class PlayerWindow
    {
        static bool HighChrome { get { return Ds.Brushes.High; } }

        static int Alpha(double al) { return Math.Max(0, Math.Min(255, (int)Math.Round(al))); }

        // A dark surface of the chrome at alpha al (0-255) times the bar's fade a.
        static Color ChromeBack(int al, int r, int g, int b, double a)
        {
            return HighChrome ? Color.FromArgb(Alpha(255 * a), Ds.Gdi(Ds.Brushes.Window)) : Color.FromArgb(Alpha(al * a), r, g, b);
        }

        // A white of the chrome at alpha al: text, icons and lines in the text colour, tracks (the faint ones) in GrayText.
        static Color ChromeWhite(int al, int v, double a)
        {
            if (!HighChrome) return Color.FromArgb(Alpha(al * a), v, v, v);
            M.Color c = al > 40 && al < 100 ? Ds.Brushes.Label3 : Ds.Brushes.Label;
            return Color.FromArgb(Alpha(255 * a), c.R, c.G, c.B);
        }

        // The marked piece (yellow): its band in the link colour and its ticks in the text colour.
        static Color ChromeMark(int al)
        {
            if (!HighChrome) return Color.FromArgb(al, 255, 214, 10);
            return Ds.Gdi(al < 255 ? Ds.Brushes.Link : Ds.Brushes.Label);
        }

        // A button that is on: a light blue readable on the dark bar, Highlight under a contrast theme.
        static M.Color ChromeOn(double a)
        {
            return Ds.WithAlpha(HighChrome ? Ds.Brushes.Accent : Palette.HudAccent, a);
        }

        // A button's hover or pill: a white wash, or an outline (Highlight for the pointer) under a contrast theme.
        void ChromeButton(Graphics g, Rectangle r, bool over, Func<int, int, Color> white)
        {
            if (HighChrome) EdLook.Hairline(g, r, P(8), Ds.Gdi(over ? Ds.Brushes.Accent : Ds.Brushes.Label));
            else EdLook.Fill(g, r, P(8), white(over ? 56 : 30, 255));
        }

        // A theme switched while the player is open repaints its chrome at once, even with the video paused.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Action restyle = delegate
            {
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)Invalidate); } catch (InvalidOperationException) { }
            };
            Ds.Changed += restyle;
            Disposed += delegate { Ds.Changed -= restyle; };
        }
    }
}

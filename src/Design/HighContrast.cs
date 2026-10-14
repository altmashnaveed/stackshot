// Stackshot - Windows contrast themes: while one is on, a third palette built from the system colours replaces light and
// dark, with nothing translucent and every role in a system colour. Captures and exported files never change.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Runtime.InteropServices;
using M = System.Windows.Media;

namespace Stackshot
{
    // The colours a contrast theme sets (Settings > Accessibility > Contrast themes).
    public sealed class HcColors
    {
        public M.Color Window, WindowText, ButtonFace, ButtonText, Highlight, HighlightText, HotTrack, GrayText;

        [DllImport("user32.dll")] static extern int GetSysColor(int index);

        static M.Color Sys(int index)
        {
            int c = GetSysColor(index);
            return M.Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
        }

        // Read straight from Windows each time, so a theme switched a moment ago is already there.
        public static HcColors FromSystem()
        {
            HcColors c = new HcColors();
            c.Window = Sys(5); c.WindowText = Sys(8); c.ButtonFace = Sys(15); c.ButtonText = Sys(18);
            c.Highlight = Sys(13); c.HighlightText = Sys(14); c.HotTrack = Sys(26); c.GrayText = Sys(17);
            return c;
        }

        public bool Same(HcColors o)
        {
            return o != null && Window == o.Window && WindowText == o.WindowText && ButtonFace == o.ButtonFace && ButtonText == o.ButtonText &&
                   Highlight == o.Highlight && HighlightText == o.HighlightText && HotTrack == o.HotTrack && GrayText == o.GrayText;
        }
    }

    public static class HighContrast
    {
        // For tests: 1 forces a contrast theme on, -1 off, 0 follows Windows; Fake stands in for the system colours.
        public static int Force;
        public static HcColors Fake;

        // Light or dark as the app would be without a contrast theme (exports keep following it).
        public static bool BaseDark = true;

        static bool micaKnown, osMica;

        // Changes with every new palette: caches of painted pixels keyed on it never outlive a theme (two contrast themes
        // can both be dark).
        public static int Generation;

        public static bool On { get { return Ds.Brushes.High; } }

        public static bool SystemOn
        {
            get
            {
                if (Force != 0) return Force > 0;
                try { return System.Windows.Forms.SystemInformation.HighContrast; }
                catch { return false; }
            }
        }

        // The colours for the palette, or null when no contrast theme is on.
        public static HcColors Current()
        {
            if (!SystemOn) return null;
            if (Fake != null) return Fake;
            try { return HcColors.FromSystem(); }
            catch (Exception ex) { ShotStack.Log("Contraste: " + ex.Message); return null; }
        }

        // Windows draws no Mica under a contrast theme: windows get a solid system background instead.
        internal static void Applied(bool high)
        {
            Generation++;
            if (!micaKnown) { osMica = Sheet.HasMica; micaKnown = true; }
            Sheet.HasMica = osMica && !high;
        }

        internal static bool OsMica
        {
            get { if (!micaKnown) { osMica = Sheet.HasMica; micaKnown = true; } return osMica; }
        }

        // Relative luminance (WCAG) of an opaque colour.
        public static double Luminance(M.Color c)
        {
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }

    public sealed partial class Palette
    {
        public readonly bool High;      // built from a contrast theme
        public readonly HcColors Sys;   // its system colours, or null
        readonly M.Color link, onAccent;
        public readonly bool Pictures;  // the look of exported pictures and prints (see Legacy())

        static readonly M.Color LightLink = Ds.Rgb(0, 90, 204), DarkLink = Ds.Rgb(76, 162, 255), HudLink = Ds.Rgb(110, 180, 255);

        // Accent used as text or as a line icon on the window's surfaces (links, actions): readable on all of them.
        public M.Color Link { get { return High ? link : Dark ? DarkLink : LightLink; } }

        // Text and icons on an Accent fill.
        public M.Color OnAccent { get { return High ? onAccent : M.Colors.White; } }

        // Text in the accent's hue on a light accent tint (a toggle that is on): 4.5:1 on the tint, hovered or not.
        public M.Color TintInk { get { return High ? onAccent : Dark ? DarkLink : Ds.Rgb(0, 84, 190); } }

        // The lower end of an accent gradient under white text: deeper, never lighter, than the accent (flat in a contrast
        // theme).
        public M.Color AccentDeep { get { return Deeper(Accent, 0.84); } }

        // The colour scaled towards black by k (hover and press of a fill under white text); flat in a contrast theme.
        public M.Color Deeper(M.Color c, double k)
        {
            return High ? c : M.Color.FromArgb(c.A, (byte)Math.Round(c.R * k), (byte)Math.Round(c.G * k), (byte)Math.Round(c.B * k));
        }

        // Error and warning text (Red and Orange are too light for small text on the light surfaces).
        public M.Color Error { get { return High ? Label : Dark ? Ds.Rgb(255, 138, 130) : Ds.Rgb(176, 26, 20); } }
        public M.Color Warn { get { return High ? Label : Dark ? Orange : Ds.Rgb(156, 68, 0); } }

        // Line icons beside menu text: a little softer than the text, or the text colour itself in a contrast theme.
        public M.Color MenuIcon { get { return High ? Label : Ds.WithAlpha(Label, Dark ? 0.72 : 0.68); } }

        // Contrast theme: text in WindowText, disabled in GrayText, buttons in ButtonFace, selections in Highlight with
        // HighlightText, links in HotTrack and every line in WindowText; nothing translucent, no shadows, no colour that
        // carries meaning on its own.
        public Palette(HcColors s)
        {
            High = true;
            Sys = s;
            Dark = HighContrast.Luminance(s.Window) < 0.4;
            Label = s.WindowText; Label2 = s.WindowText; Label3 = s.GrayText;
            Window = s.Window; Sidebar = s.Window; Group = s.Window;
            Control = s.ButtonFace; ControlHover = s.ButtonFace;
            Separator = s.WindowText; Hairline = s.WindowText;
            Accent = s.Highlight;
            Red = Green = Orange = Yellow = Purple = Pink = Teal = s.WindowText;
            Thumb = s.Window;
            Shadow = M.Colors.Transparent;
            link = s.HotTrack;
            onAccent = s.HighlightText;
        }

        // The look exported pictures and prints have always had (they are documents, not the app's chrome): the old
        // secondary greys and system blue, and never a contrast theme.
        public static Palette Legacy(bool dark) { return new Palette(dark, true); }

        public static Palette Export() { return Legacy(HighContrast.BaseDark); }

        Palette(bool dark, bool legacy) : this(dark)
        {
            if (!legacy) return;
            Pictures = true;
            if (dark) { Label2 = Ds.Argb(0.55, 255, 255, 255); Label3 = Ds.Argb(0.28, 255, 255, 255); Accent = Ds.Rgb(10, 132, 255); }
            else { Label2 = Ds.Argb(0.50, 0, 0, 0); Label3 = Ds.Argb(0.26, 0, 0, 0); Accent = Ds.Rgb(0, 122, 255); }
        }

        // Heads-up surfaces: always dark, or the window colours under a contrast theme.
        public static M.Color Hud { get { Palette p = Ds.Brushes; return p.High ? p.Window : hud; } }
        public static M.Color HudHover { get { Palette p = Ds.Brushes; return p.High ? Ds.WithAlpha(p.Label, 0.25) : hudHover; } }
        public static M.Color HudLabel { get { Palette p = Ds.Brushes; return p.High ? p.Label : hudLabel; } }
        public static M.Color HudLabel2 { get { Palette p = Ds.Brushes; return p.High ? p.Label : hudLabel2; } }
        public static M.Color HudLine { get { Palette p = Ds.Brushes; return p.High ? p.Hairline : hudLine; } }

        // The accent as text or an icon on a heads-up surface (always dark): a light blue that keeps 4.5:1 there.
        public static M.Color HudAccent { get { Palette p = Ds.Brushes; return p.High ? p.Link : HudLink; } }

        // A dark veil or badge drawn over a capture: the heads-up surface under a contrast theme, so HudLabel stays readable.
        public static M.Color HudBack(M.Color normal) { Palette p = Ds.Brushes; return p.High ? p.Window : normal; }

        // Coloured text or icons on a heads-up surface (warnings, muted sources): the text colour under a contrast theme.
        public static M.Color HudInk(M.Color normal) { Palette p = Ds.Brushes; return p.High ? p.Label : normal; }
        // Text and icons that warn on a HUD (muted source, little disk left, a capture that lost its place).
        public static M.Color HudDanger { get { return HudInk(Ds.Rgb(255, 105, 97)); } }

        // A coloured button on a heads-up surface (Detener, a confirmation): Highlight under a contrast theme, with
        // OnHudFill for what is drawn on it.
        public static M.Color HudFill(M.Color normal) { Palette p = Ds.Brushes; return p.High ? p.Accent : normal; }
        public static M.Color OnHudFill(M.Color normal) { Palette p = Ds.Brushes; return p.High ? p.OnAccent : normal; }

        // A white wash for the pointer over a heads-up button (it would not show on a light system window): HudHover
        // under a contrast theme.
        public static M.Color HudWash(M.Color normal) { return Ds.Brushes.High ? HudHover : normal; }

        // Tracks and other faint marks on a heads-up surface: GrayText under a contrast theme.
        public static M.Color HudFaint(M.Color normal) { Palette p = Ds.Brushes; return p.High ? p.Label3 : normal; }
    }
}

// Stackshot - More plates for the trailer: a synthetic invoice and the real editor toolbar and hint strip (drawn by their own
// controls, offscreen, at 2x) for the sensitive-data scene. Built by tools\make-film.ps1 together with Plates.cs.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using W = System.Windows;
using M = System.Windows.Media;

namespace Stackshot
{
    public static partial class Plates
    {
        static readonly M.FontFamily InvUi = new M.FontFamily("Segoe UI Variable Text, Segoe UI"), InvDisplay = new M.FontFamily("Segoe UI Variable Display, Segoe UI"),
            InvMono = new M.FontFamily("Cascadia Code, Consolas");

        const int InvW = 1280, InvH = 800;

        static M.Color Rgb(int v, double a = 1) { return M.Color.FromArgb((byte)Math.Round(255 * a), (byte)(v >> 16), (byte)(v >> 8), (byte)v); }

        static M.FormattedText InvText(string s, double size, int rgb, int weight, M.FontFamily f)
        {
            return new M.FormattedText(s, CultureInfo.GetCultureInfo("es-ES"), W.FlowDirection.LeftToRight,
                new M.Typeface(f ?? InvUi, W.FontStyles.Normal, W.FontWeight.FromOpenTypeWeight(weight), W.FontStretches.Normal), size, new M.SolidColorBrush(Rgb(rgb)), 1.0);
        }

        // Draws text with its top left at (x, y), or its top right when right is set; returns the box it took.
        static W.Rect InvDraw(M.DrawingContext dc, string s, double x, double y, double size, int rgb, int weight = 400, M.FontFamily f = null, bool right = false)
        {
            M.FormattedText ft = InvText(s, size, rgb, weight, f);
            double ox = right ? x - ft.Width : x;
            dc.DrawText(ft, new W.Point(ox, y));
            return new W.Rect(ox, y, ft.Width, ft.Height);
        }

        static void InvRound(M.DrawingContext dc, double x, double y, double w, double h, double r, int rgb)
        {
            dc.DrawRoundedRectangle(new M.SolidColorBrush(Rgb(rgb)), null, new W.Rect(x, y, w, h), r, r);
        }

        // A synthetic invoice with the usual documentation examples of personal data (DNI, e-mail, phone, IBAN); returns the
        // boxes of those four, in picture pixels.
        static List<W.Rect> Invoice(M.DrawingContext dc, bool es)
        {
            List<W.Rect> found = new List<W.Rect>();
            double x = 60, y = 40, pw = InvW - 120, ph = InvH - 80;
            dc.DrawRectangle(new M.SolidColorBrush(Rgb(0xEEF0F4)), null, new W.Rect(0, 0, InvW, InvH));
            InvRound(dc, x, y, pw, ph, 8, 0xFFFFFF);
            InvDraw(dc, es ? "FACTURA" : "INVOICE", x + 40, y + 36, 28, 0x111827, 700, InvDisplay);
            InvDraw(dc, (es ? "N\u00BA 2026-0412" : "No. 2026-0412") + " \u00B7 12/10/2026", x + 40, y + 76, 14, 0x6B7280);
            InvRound(dc, x + pw - 200, y + 36, 160, 44, 8, 0xEEF2FF);
            M.FormattedText badge = InvText("Estudio Aurora", 15, 0x4338CA, 700, null);
            dc.DrawText(badge, new W.Point(x + pw - 120 - badge.Width / 2, y + 58 - badge.Height / 2));
            InvDraw(dc, es ? "Datos del cliente" : "Customer details", x + 40, y + 124, 15, 0x111827, 600);
            string[,] f = es
                ? new string[,] { { "Nombre", "Luc\u00EDa Mart\u00EDn Serrano" }, { "DNI", "12345678Z" }, { "Correo", "lucia.martin@example.com" }, { "Tel\u00E9fono", "+34 600 123 456" }, { "IBAN", "ES91 2100 0418 4502 0005 1332" } }
                : new string[,] { { "Name", "Luc\u00EDa Mart\u00EDn Serrano" }, { "DNI", "12345678Z" }, { "E-mail", "lucia.martin@example.com" }, { "Phone", "+34 600 123 456" }, { "IBAN", "ES91 2100 0418 4502 0005 1332" } };
            for (int i = 0; i < 5; i++)
            {
                double yy = y + 156 + i * 32;
                InvDraw(dc, f[i, 0], x + 40, yy, 14, 0x6B7280);
                W.Rect r = InvDraw(dc, f[i, 1], x + 160, yy, 15, 0x111827, 400, i >= 1 ? InvMono : null);
                if (i >= 1) found.Add(new W.Rect(r.X - 4, r.Y + 1, r.Width + 8, r.Height - 2));
            }
            double ty = y + 340;
            dc.DrawRectangle(new M.SolidColorBrush(Rgb(0xF3F4F6)), null, new W.Rect(x + 40, ty, pw - 80, 34));
            InvDraw(dc, es ? "Concepto" : "Item", x + 56, ty + 8, 13.5, 0x374151, 600);
            InvDraw(dc, es ? "Importe" : "Amount", x + pw - 56, ty + 8, 13.5, 0x374151, 600, null, true);
            string[,] l = es
                ? new string[,] { { "Dise\u00F1o de la pantalla de inicio", "1.200,00 \u20AC" }, { "Iconos y mascota", "450,00 \u20AC" }, { "Revisi\u00F3n de accesibilidad", "300,00 \u20AC" } }
                : new string[,] { { "Home screen design", "1,200.00 \u20AC" }, { "Icons and mascot", "450.00 \u20AC" }, { "Accessibility review", "300.00 \u20AC" } };
            for (int i = 0; i < 3; i++)
            {
                double yy = ty + 46 + i * 34;
                InvDraw(dc, l[i, 0], x + 56, yy, 14, 0x111827);
                InvDraw(dc, l[i, 1], x + pw - 56, yy, 14, 0x111827, 400, null, true);
                dc.DrawLine(new M.Pen(new M.SolidColorBrush(Rgb(0xF1F5F9)), 1), new W.Point(x + 40, yy + 26), new W.Point(x + pw - 40, yy + 26));
            }
            InvDraw(dc, "Total", x + pw - 220, ty + 160, 16, 0x111827, 700);
            InvDraw(dc, es ? "2.359,50 \u20AC" : "2,359.50 \u20AC", x + pw - 56, ty + 160, 16, 0x111827, 700, null, true);
            InvDraw(dc, es ? "IVA (21 %) incluido" : "VAT (21 %) included", x + pw - 56, ty + 184, 12, 0x6B7280, 400, null, true);
            return found;
        }

        // invoice-<lang>.png at 2x, and where the personal data sits (2x pixels) in invoice-boxes.txt.
        static void InvoicePlates(string dir)
        {
            List<W.Rect> boxes = null;
            foreach (bool es in new bool[] { true, false })
            {
                M.DrawingVisual dv = new M.DrawingVisual();
                using (M.DrawingContext dc = dv.RenderOpen()) boxes = Invoice(dc, es);
                M.Imaging.RenderTargetBitmap rtb = new M.Imaging.RenderTargetBitmap(InvW * 2, InvH * 2, 96, 96, M.PixelFormats.Pbgra32);
                M.DrawingVisual scaled = new M.DrawingVisual();
                using (M.DrawingContext dc = scaled.RenderOpen())
                {
                    dc.PushTransform(new M.ScaleTransform(2, 2));
                    dc.DrawDrawing(dv.Drawing);
                    dc.Pop();
                }
                rtb.Render(scaled);
                Save(rtb, Path.Combine(dir, es ? "invoice-es.png" : "invoice-en.png"));
            }
            using (StreamWriter sw = new StreamWriter(Path.Combine(dir, "invoice-boxes.txt")))
                foreach (W.Rect r in boxes)
                    sw.WriteLine(((int)Math.Round(r.X * 2)) + " " + ((int)Math.Round(r.Y * 2)) + " " + ((int)Math.Round(r.Width * 2)) + " " + ((int)Math.Round(r.Height * 2)));
        }

        // The editor's toolbar and hint strip as its own controls draw them (at 2x, with the "Datos sensibles" button idle,
        // pressed and busy), and the button's box in sensitive-bar.txt.
        static void EditorPlates(string dir, string temp)
        {
            string png = Path.Combine(temp, "invoice.png");
            using (Bitmap big = new Bitmap(Path.Combine(dir, "invoice-es.png")))
            using (Bitmap small = new Bitmap(big, InvW, InvH))
                small.Save(png, ImageFormat.Png);
            Editor ed = new Editor(null, png, ShotStack.LoadFull(png));
            try
            {
                Bar bar = (Bar)typeof(Editor).GetField("bar", NP).GetValue(ed);
                Hint hint = (Hint)typeof(Editor).GetField("hint", NP).GetValue(ed);
                Bar.Item item = (Bar.Item)typeof(Editor).GetField("sensitiveItem", NP).GetValue(ed);
                bar.S = 2f;
                bar.Height = 104;
                hint.S = 2f;
                hint.Height = 56;
                ed.Size = new Size(1900, 900);
                IntPtr handle = ed.Handle;
                ed.PerformLayout();
                bar.LayoutItems();
                int w = bar.FullWidth;
                // Windows keeps a window within the screen, so the controls are widened on their own.
                bar.Dock = DockStyle.None;
                hint.Dock = DockStyle.None;
                bar.Size = new Size(w, 104);
                hint.Size = new Size(w, 56);
                bar.LayoutItems();
                Pump(300);
                if (bar.Level != 0) throw new InvalidOperationException("The editor bar is not at its full width (level " + bar.Level + ", bar " + bar.Width + ", needs " + w + ", form " + ed.Size + ", client " + ed.ClientSize + ").");
                SaveControl(bar, Path.Combine(dir, "sensitive-bar-0.png"));
                item.HotT = 1;
                item.DownT = 1;
                SaveControl(bar, Path.Combine(dir, "sensitive-bar-1.png"));
                item.HotT = 0;
                item.DownT = 0;
                item.Enabled = false;
                item.ShowAlt = true;
                SaveControl(bar, Path.Combine(dir, "sensitive-bar-2.png"));
                using (StreamWriter sw = new StreamWriter(Path.Combine(dir, "sensitive-bar.txt")))
                    sw.WriteLine(bar.Width + " " + bar.Height + " " + item.R.X + " " + item.R.Y + " " + item.R.Width + " " + item.R.Height);
                List<SensitiveFinding> found = new List<SensitiveFinding>();
                foreach (SensitiveKind k in new SensitiveKind[] { SensitiveKind.Dni, SensitiveKind.Email, SensitiveKind.Phone, SensitiveKind.Iban })
                {
                    SensitiveFinding f = new SensitiveFinding();
                    f.Kind = k;
                    found.Add(f);
                }
                hint.LeftText = Editor.SensitiveResult(found, found.Count);
                SaveControl(hint, Path.Combine(dir, "sensitive-hint-1.png"));
                hint.LeftText = "Buscando datos sensibles\u2026";
                SaveControl(hint, Path.Combine(dir, "sensitive-hint-0.png"));
                hint.LeftText = "";
                SaveControl(hint, Path.Combine(dir, "sensitive-hint-2.png"));
            }
            finally
            {
                typeof(Editor).GetField("closeWithoutAsking", NP).SetValue(ed, true);
                ed.Close();
            }
        }

        static void SaveControl(Control c, string path)
        {
            IntPtr h = c.Handle;
            using (Bitmap b = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppArgb))
            {
                c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                b.Save(path, ImageFormat.Png);
            }
        }
    }
}

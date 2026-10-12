// Stackshot - The editor's backdrop strip for screen readers: backdrops and proportions to choose, the margin and corner
// sliders (a reader can type a new value) and the shadow switch.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Stackshot
{
    public partial class BgPanel
    {
        protected override AccessibleObject CreateAccessibilityInstance() { return new PanelAccess(this); }

        // A choice made from a reader, as a click on it makes it.
        void AccessPick(Item it)
        {
            if (!Enabled || Bg == null) return;
            switch (it.Kind)
            {
                case Kind.Swatch:
                    if (it.Index < 0) On = false;
                    else { On = true; Bg.BgPreset = it.Index; }
                    break;
                case Kind.Shadow:
                    Bg.BgShadow = !Bg.BgShadow;
                    On = true;
                    break;
                case Kind.Ratio:
                    Bg.BgRatio = Ratios[it.Index];
                    On = true;
                    break;
                default:
                    return;
            }
            Invalidate();
            Fire(Changed);
            Fire(Committed);
        }

        // A slider set from a reader (0 to 100).
        void AccessSlide(Item it, string typed)
        {
            int v;
            if (!Enabled || Bg == null || it.Kind != Kind.Slider || typed == null) return;
            if (!int.TryParse(typed.Replace("%", "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return;
            v = Math.Max(0, Math.Min(100, v));
            On = true;
            if (it.Index == 0) Bg.BgPadding = v; else Bg.BgRadius = v;
            Invalidate();
            Fire(Changed);
            Fire(Committed);
        }

        sealed class PanelAccess : PartsAccessible
        {
            readonly BgPanel p;

            public PanelAccess(BgPanel p) : base(p) { this.p = p; }

            public override AccessibleRole Role { get { return AccessibleRole.Grouping; } }
            public override string Name { get { return "Fondo"; } }

            protected override string Signature()
            {
                StringBuilder b = new StringBuilder();
                b.Append(p.Width).Append('|').Append(p.On).Append('|').Append(p.Enabled).Append('|').Append(p.items.Count);
                if (p.Bg != null) b.Append('|').Append(p.Bg.BgPreset).Append('|').Append(p.Bg.BgPadding).Append('|').Append(p.Bg.BgRadius).Append('|').Append(p.Bg.BgShadow).Append('|').Append(p.Bg.BgRatio);
                return b.ToString();
            }

            protected override void Fill(List<PartAccessible> into)
            {
                Settings bg = p.Bg;
                if (bg == null) return;
                foreach (Item it in p.items)
                {
                    Item item = it;
                    PartAccessible a;
                    switch (it.Kind)
                    {
                        case Kind.Swatch:
                            a = NewPart(into, it.Index < 0 ? "Sin fondo" : Backdrop.Name(it.Index), AccessibleRole.RadioButton, it.R);
                            a.PartState = StateOf(p.Enabled, it.Index < 0 ? !p.On : p.On && bg.BgPreset == it.Index, false);
                            break;
                        case Kind.Slider:
                            a = NewPart(into, it.Label, AccessibleRole.Slider, it.R);
                            a.PartState = StateOf(p.Enabled, false, false);
                            a.PartValue = (it.Index == 0 ? bg.BgPadding : bg.BgRadius).ToString(CultureInfo.CurrentCulture);
                            a.PartHelp = "De 0 a 100";
                            a.SetValue = delegate(string v) { p.AccessSlide(item, v); };
                            continue;
                        case Kind.Shadow:
                            a = NewPart(into, it.Label, AccessibleRole.CheckButton, it.R);
                            a.PartState = StateOf(p.Enabled, bg.BgShadow, false);
                            break;
                        default:
                            a = NewPart(into, "Proporci\u00F3n " + it.Label, AccessibleRole.RadioButton, it.R);
                            a.PartState = StateOf(p.Enabled, bg.BgRatio == Ratios[it.Index], false);
                            break;
                    }
                    a.PartAction = it.Kind == Kind.Shadow ? "Cambiar" : "Elegir";
                    if (p.Enabled) a.Do = delegate { p.AccessPick(item); };
                }
            }
        }
    }
}

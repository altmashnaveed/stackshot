// Stackshot - The main window under IT policies: the settings they force are shown but can't be changed (see Policy).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Stackshot
{
    public partial class HomeWindow
    {
        // Read again whenever a section is built, so what IT changes applies without restarting Stackshot.
        void ReadPolicy()
        {
            try { if (Policy.Apply(settings)) owner.PolicyChanged(); }
            catch (Exception ex) { ShotStack.Log("Directivas: " + ex.Message); }
        }

        // The rows of the settings a policy forces: dimmed and disabled, saying who decides. Without policies nothing
        // here runs.
        void PolicyRows()
        {
            if (body == null || !Policy.Any) return;
            List<Row> rows = new List<Row>();
            FindRows(body, rows);
            foreach (Row r in rows)
            {
                if (!ForcedByPolicy(r)) continue;
                r.When = delegate { return false; };
                r.WhenOff = Policy.Managed;
                r.Build();
            }
        }

        // The rows (in General and in Ajustes de la mascota) of each setting a policy can force.
        static bool ForcedByPolicy(Row r)
        {
            if (r is FolderRow) return Policy.FolderForced;
            switch (r.Title)
            {
                case "Buscar actualizaciones": return Policy.ChecksForced;
                case "Instalar actualizaciones autom\u00E1ticamente": return Policy.AutoForced;
                case "Mostrar la mascota":
                case "Saludos y consejos":
                case "Mascota en el escritorio": return Policy.MascotForced;
                case "Tapar los datos sensibles al capturar": return Policy.SensitiveForced;
                case "Buscar por el texto de las capturas": return Policy.SearchForced;
            }
            return false;
        }

        // A control outside those rows (Acerca de, the mascot's stage) whose setting a policy forces.
        static void LockedByPolicy(FrameworkElement e, bool forced)
        {
            if (!forced) return;
            e.IsEnabled = false;
            e.Opacity = 0.42;
            if (e.Cursor == System.Windows.Input.Cursors.Hand) e.Cursor = System.Windows.Input.Cursors.Arrow;   // nothing to press
            e.ToolTip = Policy.Managed;
            ToolTipService.SetShowOnDisabled(e, true);
        }

        // The line under the update card in Acerca de while any policy applies.
        FrameworkElement PolicyNote()
        {
            Palette pal = Ds.Brushes;
            StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
            GlyphView icon = new GlyphView("info", 13, pal.Label2, 1.5);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 6, 0);
            line.Children.Add(icon);
            line.Children.Add(Label(Policy.ManagedNote, Ds.Regular, 12.5, pal.Label2));
            return line;
        }
    }
}

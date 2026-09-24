namespace Darkmount.App;

/// <summary>Small helpers so every settings page has the same dark, aligned look.</summary>
public static class Ui
{
    public static readonly Color Back = Color.FromArgb(18, 20, 24);
    public static readonly Color Panel = Color.FromArgb(28, 31, 37);
    public static readonly Color PanelHover = Color.FromArgb(38, 42, 50);
    public static readonly Color Text = Color.FromArgb(232, 234, 238);
    public static readonly Color Dim = Color.FromArgb(150, 156, 168);
    public static readonly Color Accent = Color.FromArgb(255, 138, 31);

    public static readonly Font Body = new("Segoe UI", 10f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font Section = new("Segoe UI Semibold", 11f);

    /// <summary>A page: title, optional description, then aligned label/control rows.</summary>
    public class Page : TableLayoutPanel
    {
        public Page(string title, string? description = null)
        {
            // Size to content (no stretched rows); the host panel scrolls.
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnCount = 2;
            Padding = new Padding(24, 18, 24, 18);
            BackColor = Back;
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddFull(new Label { Text = title, Font = Title, ForeColor = Ui.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
            if (description is not null) AddFull(Note(description, 620));
            AddFull(new Label { Height = 8, AutoSize = false });
        }

        public void Row(string label, Control control, string? hint = null)
        {
            Controls.Add(new Label
            {
                Text = label, AutoSize = true, ForeColor = Ui.Text, Font = Body, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 9, 12, 9),
            });
            control.Anchor = AnchorStyles.Left;
            control.Margin = new Padding(0, 6, 0, 6);
            if (hint is null)
            {
                Controls.Add(control);
                return;
            }
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Left };
            flow.Controls.Add(control);
            flow.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Dim, Font = Body, Margin = new Padding(8, 9, 0, 0) });
            Controls.Add(flow);
        }

        public void Row(Control left, Control right)
        {
            left.Anchor = AnchorStyles.Left;
            left.Margin = new Padding(0, 8, 12, 8);
            right.Anchor = AnchorStyles.Left;
            right.Margin = new Padding(0, 6, 0, 6);
            Controls.Add(left);
            Controls.Add(right);
        }

        public void Heading(string text) =>
            AddFull(new Label { Text = text, Font = Section, ForeColor = Accent, AutoSize = true, Margin = new Padding(0, 14, 0, 4) });

        public void AddFull(Control c)
        {
            Controls.Add(c);
            SetColumnSpan(c, 2);
        }
    }

    public static Label Note(string text, int width = 520) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = Dim, Font = Body, Margin = new Padding(0, 2, 0, 6) };

    public static ComboBox Combo<T>(int width = 220) where T : struct, Enum
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Font = Body, FlatStyle = FlatStyle.Flat };
        foreach (var v in Enum.GetValues<T>()) c.Items.Add(v);
        return c;
    }

    public static NumericUpDown Number(decimal min, decimal max, decimal step = 1, int decimals = 0) =>
        new() { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, Width = 90, Font = Body };

    public static CheckBox Check(string text) => new() { Text = text, AutoSize = true, ForeColor = Ui.Text, Font = Body };

    public static Button Button(string text, EventHandler? onClick = null, bool primary = false)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(100, 34), Font = Body, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Panel, ForeColor = primary ? Color.Black : Text, Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : PanelHover;
        if (onClick is not null) b.Click += onClick;
        return b;
    }

    public static decimal Clamp(NumericUpDown n, double v) => Math.Clamp((decimal)v, n.Minimum, n.Maximum);

    /// <summary>Asks Windows 10/11 for a dark title bar.</summary>
    public static void UseDarkTitleBar(Form form)
    {
        int on = 1;
        _ = DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

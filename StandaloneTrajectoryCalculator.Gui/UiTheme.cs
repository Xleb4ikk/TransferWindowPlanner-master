using System.Drawing.Drawing2D;

namespace StandaloneTrajectoryCalculator.Gui;

internal static class UiTheme
{
    public static readonly Color WindowBackground = Color.FromArgb(244, 247, 250);
    public static readonly Color ToolbarBackground = Color.FromArgb(231, 237, 243);
    public static readonly Color PanelBackground = Color.FromArgb(240, 244, 248);
    public static readonly Color CardBackground = Color.FromArgb(255, 255, 255);
    public static readonly Color CardAltBackground = Color.FromArgb(247, 250, 252);
    public static readonly Color InputBackground = Color.FromArgb(252, 253, 255);
    public static readonly Color InfoCardBackground = Color.FromArgb(236, 243, 249);
    public static readonly Color ActionSurface = Color.FromArgb(249, 251, 253);
    public static readonly Color Border = Color.FromArgb(211, 220, 228);
    public static readonly Color BorderStrong = Color.FromArgb(160, 176, 190);
    public static readonly Color TextPrimary = Color.FromArgb(34, 47, 62);
    public static readonly Color TextSecondary = Color.FromArgb(82, 96, 112);
    public static readonly Color TextMuted = Color.FromArgb(119, 132, 146);
    public static readonly Color AccentBlue = Color.FromArgb(76, 117, 175);
    public static readonly Color AccentBlueMuted = Color.FromArgb(206, 222, 238);
    public static readonly Color AccentGreen = Color.FromArgb(92, 146, 114);
    public static readonly Color AccentAmber = Color.FromArgb(184, 132, 74);
    public static readonly Color Selection = Color.FromArgb(220, 232, 246);

    public static void Apply(Control root)
    {
        root.SuspendLayout();
        try
        {
            ApplyRecursive(root);
        }
        finally
        {
            root.ResumeLayout(true);
        }
    }

    public static void StyleActionButton(Button button, Color? fill = null)
    {
        var background = fill ?? ActionSurface;
        var useLightText = UseLightForeground(background);

        button.BackColor = background;
        button.ForeColor = useLightText ? Color.White : TextPrimary;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = useLightText ? Shift(background, -0.16) : Border;
        button.FlatAppearance.MouseOverBackColor = useLightText ? Shift(background, 0.04) : Shift(background, -0.03);
        button.FlatAppearance.MouseDownBackColor = useLightText ? Shift(background, -0.04) : Shift(background, -0.06);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
    }

    public static void StyleDataGridView(DataGridView grid)
    {
        grid.BackgroundColor = InputBackground;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = CardAltBackground,
            ForeColor = TextPrimary,
            SelectionBackColor = CardAltBackground,
            SelectionForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Padding = new Padding(6, 4, 6, 4)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = InputBackground,
            ForeColor = TextPrimary,
            SelectionBackColor = Selection,
            SelectionForeColor = TextPrimary,
            Padding = new Padding(6, 4, 6, 4)
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = CardAltBackground,
            ForeColor = TextPrimary,
            SelectionBackColor = Selection,
            SelectionForeColor = TextPrimary,
            Padding = new Padding(6, 4, 6, 4)
        };
    }

    internal static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ApplyRecursive(Control control)
    {
        ApplySingle(control);
        foreach (Control child in control.Controls)
        {
            ApplyRecursive(child);
        }
    }

    private static void ApplySingle(Control control)
    {
        switch (control)
        {
            case Form form:
                form.BackColor = WindowBackground;
                form.ForeColor = TextPrimary;
                break;
            case FlowLayoutPanel flow:
                flow.BackColor = ToolbarBackground;
                flow.ForeColor = TextSecondary;
                break;
            case GroupBox group:
                group.BackColor = CardBackground;
                group.ForeColor = TextPrimary;
                break;
            case TableLayoutPanel table:
                table.BackColor = table.Parent?.BackColor ?? PanelBackground;
                table.ForeColor = TextPrimary;
                break;
            case TabPage page:
                page.BackColor = PanelBackground;
                page.ForeColor = TextPrimary;
                break;
            case Panel panel when Equals(panel.Tag, "info-card"):
                panel.BackColor = InfoCardBackground;
                panel.ForeColor = TextPrimary;
                break;
            case Panel panel when panel.BorderStyle == BorderStyle.FixedSingle:
                panel.BackColor = CardBackground;
                panel.ForeColor = TextPrimary;
                break;
            case Panel panel:
                panel.BackColor = PanelBackground;
                panel.ForeColor = TextPrimary;
                break;
            case SplitContainer split:
                split.BackColor = Border;
                split.ForeColor = TextPrimary;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = label.Font.Bold ? TextPrimary : TextSecondary;
                break;
            case TextBox textBox:
                StyleTextBox(textBox);
                break;
            case RichTextBox richTextBox:
                richTextBox.BackColor = InputBackground;
                richTextBox.ForeColor = TextPrimary;
                richTextBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.BackColor = InputBackground;
                combo.ForeColor = TextPrimary;
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = InputBackground;
                numeric.ForeColor = TextPrimary;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                break;
            case DateTimePicker picker:
                picker.CalendarMonthBackground = InputBackground;
                picker.CalendarForeColor = TextPrimary;
                picker.CalendarTitleBackColor = CardAltBackground;
                picker.CalendarTitleForeColor = TextPrimary;
                picker.CalendarTrailingForeColor = TextMuted;
                picker.BackColor = InputBackground;
                picker.ForeColor = TextPrimary;
                break;
            case Button button:
                StyleActionButton(button, button.BackColor);
                break;
            case CheckBox checkBox:
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = TextSecondary;
                checkBox.FlatStyle = FlatStyle.Flat;
                checkBox.FlatAppearance.BorderColor = BorderStrong;
                checkBox.FlatAppearance.CheckedBackColor = Selection;
                break;
            case DataGridView grid:
                StyleDataGridView(grid);
                break;
        }
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.ForeColor = TextPrimary;
        textBox.BackColor = textBox.ReadOnly ? CardAltBackground : InputBackground;
    }

    private static bool UseLightForeground(Color background)
    {
        var luminance = (background.R * 299 + background.G * 587 + background.B * 114) / 1000.0;
        return luminance < 156.0;
    }

    private static Color Shift(Color baseColor, double amount)
    {
        return Color.FromArgb(
            baseColor.A,
            ClampChannel(baseColor.R + 255.0 * amount),
            ClampChannel(baseColor.G + 255.0 * amount),
            ClampChannel(baseColor.B + 255.0 * amount));
    }

    private static int ClampChannel(double value)
    {
        return (int)Math.Round(Math.Clamp(value, 0.0, 255.0));
    }
}

internal sealed class ThemedGroupBox : GroupBox
{
    public ThemedGroupBox()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = UiTheme.CardBackground;
        ForeColor = UiTheme.TextPrimary;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent?.BackColor ?? UiTheme.PanelBackground);

        var caption = Text ?? string.Empty;
        using var titleFont = new Font(Font.FontFamily, Font.Size, FontStyle.Bold);
        var titleSize = TextRenderer.MeasureText(graphics, caption, titleFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var titleRect = new Rectangle(20, 0, Math.Max(0, Math.Min(Width - 40, titleSize.Width + 8)), titleSize.Height);
        var cardRect = new Rectangle(0, Math.Max(8, titleRect.Height / 2), Math.Max(1, Width - 1), Math.Max(1, Height - Math.Max(8, titleRect.Height / 2) - 1));

        using var backgroundBrush = new SolidBrush(BackColor);
        using var titleBackBrush = new SolidBrush(Parent?.BackColor ?? UiTheme.PanelBackground);
        using var borderPen = new Pen(UiTheme.Border);
        using var accentBrush = new SolidBrush(UiTheme.AccentBlueMuted);
        using var path = UiTheme.CreateRoundedRect(cardRect, 14);

        graphics.FillPath(backgroundBrush, path);
        graphics.DrawPath(borderPen, path);

        if (!string.IsNullOrWhiteSpace(caption))
        {
            graphics.FillRectangle(titleBackBrush, titleRect.X - 10, titleRect.Y, titleRect.Width + 18, titleRect.Height);
            graphics.FillRectangle(accentBrush, titleRect.X, titleRect.Bottom + 1, Math.Min(54, titleRect.Width), 3);
            TextRenderer.DrawText(
                graphics,
                caption,
                titleFont,
                titleRect,
                UiTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}

internal sealed class ThemedTabControl : TabControl
{
    public ThemedTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        ItemSize = new Size(152, 38);
        SizeMode = TabSizeMode.Fixed;
        Padding = new Point(18, 8);
        Multiline = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count)
        {
            return;
        }

        var graphics = e.Graphics;
        var bounds = Rectangle.Inflate(e.Bounds, -4, -2);
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using var background = new SolidBrush(selected ? UiTheme.CardBackground : UiTheme.CardAltBackground);
        using var border = new Pen(selected ? UiTheme.BorderStrong : UiTheme.Border);
        using var accent = new SolidBrush(UiTheme.AccentBlue);
        using var font = new Font(Font.FontFamily, Font.Size, selected ? FontStyle.Bold : FontStyle.Regular);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiTheme.CreateRoundedRect(bounds, 12);
        graphics.FillPath(background, path);
        graphics.DrawPath(border, path);

        if (selected)
        {
            var accentRect = new Rectangle(bounds.X + 18, bounds.Bottom - 5, Math.Max(18, bounds.Width - 36), 3);
            graphics.FillRectangle(accent, accentRect);
        }

        TextRenderer.DrawText(
            graphics,
            TabPages[e.Index].Text,
            font,
            bounds,
            selected ? UiTheme.TextPrimary : UiTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

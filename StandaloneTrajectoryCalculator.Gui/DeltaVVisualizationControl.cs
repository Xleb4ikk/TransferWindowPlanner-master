using System.Drawing.Drawing2D;
using Core = StandaloneTrajectoryCalculator;

namespace StandaloneTrajectoryCalculator.Gui;

internal sealed class DeltaVVisualizationControl : Control
{
    private Core.TransferDetails? _transfer;
    private Core.PorkchopResult? _porkchop;
    private Core.MissionCalendar? _calendar;

    public DeltaVVisualizationControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = UiTheme.PanelBackground;
    }

    public string EmptyText = "Run a calculation to see the minimum delta-v.";
    public string BestCaptionText = "Best transfer from porkchop scan";
    public string SingleCaptionText = "Current transfer";
    public string TotalDvText = "Total dV";
    public string EjectionDvText = "Ejection dV";
    public string InsertionDvText = "Insertion dV";
    public string DepartureText = "Departure";
    public string TravelText = "Travel";
    public string PhaseAngleText = "Phase angle";
    public string GridText = "Grid";
    public string DepartureAxisText = "Departure date";
    public string TravelAxisText = "Travel days";
    public string LegendLowText = "Lower dV";
    public string LegendHighText = "Higher dV";
    public string BestPointText = "Best point";
    public string HeatmapTitleText = "Delta-v map";
    public string BreakdownTitleText = "Transfer delta-v breakdown";

    public void SetResult(Core.TransferDetails? transfer, Core.PorkchopResult? porkchop, Core.MissionCalendar? calendar)
    {
        _transfer = transfer;
        _porkchop = porkchop;
        _calendar = calendar;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        var client = Rectangle.Inflate(ClientRectangle, -16, -16);
        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        var transfer = _porkchop?.BestTransfer ?? _transfer;
        if (transfer is null)
        {
            DrawCenteredMessage(e.Graphics, client, EmptyText);
            return;
        }

        var headerHeight = Math.Min(172, Math.Max(126, client.Height / 3));
        var headerRect = new Rectangle(client.X, client.Y, client.Width, headerHeight);
        var bodyRect = new Rectangle(client.X, headerRect.Bottom + 12, client.Width, client.Bottom - headerRect.Bottom - 12);

        DrawHeader(e.Graphics, headerRect, transfer);

        if (_porkchop is not null)
        {
            DrawHeatmap(e.Graphics, bodyRect, _porkchop);
        }
        else
        {
            DrawBreakdown(e.Graphics, bodyRect, transfer);
        }
    }

    private void DrawHeader(Graphics graphics, Rectangle rect, Core.TransferDetails transfer)
    {
        var captionHeight = 24;
        var cardGap = 10;
        var cardsHeight = 84;
        var infoGap = 10;
        var cardsTop = rect.Y + captionHeight + 8;
        var infoTop = cardsTop + cardsHeight + infoGap;

        using var captionFont = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        using var cardLabelFont = new Font(Font.FontFamily, 9.0f, FontStyle.Regular);
        using var cardValueFont = new Font(Font.FontFamily, 19.0f, FontStyle.Bold);
        using var infoFont = new Font(Font.FontFamily, 9.0f, FontStyle.Regular);
        using var cardBack = new SolidBrush(UiTheme.CardBackground);
        using var captionBrush = new SolidBrush(UiTheme.TextPrimary);
        using var borderPen = new Pen(UiTheme.Border);

        TextRenderer.DrawText(
            graphics,
            _porkchop is null ? SingleCaptionText : BestCaptionText,
            captionFont,
            new Rectangle(rect.X, rect.Y, rect.Width, captionHeight),
            UiTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var cardWidth = (rect.Width - 2 * cardGap) / 3;
        var cards = new[]
        {
            new Rectangle(rect.X, cardsTop, cardWidth, cardsHeight),
            new Rectangle(rect.X + cardWidth + cardGap, cardsTop, cardWidth, cardsHeight),
            new Rectangle(rect.X + 2 * (cardWidth + cardGap), cardsTop, rect.Width - 2 * (cardWidth + cardGap), cardsHeight)
        };

        DrawCard(graphics, cards[0], TotalDvText, $"{transfer.DVTotal:0.0} m/s", cardBack, borderPen, cardLabelFont, cardValueFont);
        DrawCard(graphics, cards[1], EjectionDvText, $"{transfer.DVEjection:0.0} m/s", cardBack, borderPen, cardLabelFont, cardValueFont);
        DrawCard(graphics, cards[2], InsertionDvText, $"{transfer.DVInjection:0.0} m/s", cardBack, borderPen, cardLabelFont, cardValueFont);

        var infoWidth = (rect.Width - 2 * cardGap) / 3;
        var departureRect = new Rectangle(rect.X, infoTop, infoWidth, Math.Max(36, rect.Bottom - infoTop));
        var travelRect = new Rectangle(departureRect.Right + cardGap, infoTop, infoWidth, departureRect.Height);
        var phaseRect = new Rectangle(travelRect.Right + cardGap, infoTop, rect.Right - (travelRect.Right + cardGap), departureRect.Height);

        DrawInfoBlock(graphics, departureRect, DepartureText, FormatDate(transfer.DepartureTime), infoFont);
        DrawInfoBlock(graphics, travelRect, TravelText, FormatDuration(transfer.TravelTime), infoFont);
        DrawInfoBlock(graphics, phaseRect, PhaseAngleText, FormatPhaseAngle(transfer), infoFont);
    }

    private void DrawHeatmap(Graphics graphics, Rectangle rect, Core.PorkchopResult porkchop)
    {
        if (rect.Width <= 120 || rect.Height <= 120)
        {
            DrawCenteredMessage(graphics, rect, HeatmapTitleText);
            return;
        }

        using var titleFont = new Font(Font.FontFamily, 10.0f, FontStyle.Bold);
        using var smallFont = new Font(Font.FontFamily, 8.75f, FontStyle.Regular);
        using var borderPen = new Pen(UiTheme.Border);
        using var invalidBrush = new SolidBrush(UiTheme.CardAltBackground);
        using var bestOutlinePen = new Pen(UiTheme.TextPrimary, 2f);
        using var bestInnerPen = new Pen(UiTheme.BorderStrong, 1f);

        TextRenderer.DrawText(
            graphics,
            HeatmapTitleText,
            titleFont,
            new Rectangle(rect.X, rect.Y, rect.Width, 20),
            UiTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var plotRect = new Rectangle(rect.X + 60, rect.Y + 32, Math.Max(80, rect.Width - 148), Math.Max(80, rect.Height - 84));
        var legendRect = new Rectangle(plotRect.Right + 18, plotRect.Y, 18, plotRect.Height);

        var values = porkchop.Points.Where(point => point.TotalDeltaV.HasValue).Select(point => point.TotalDeltaV!.Value).ToArray();
        var min = values.Length == 0 ? 0.0 : values.Min();
        var max = values.Length == 0 ? 1.0 : values.Max();
        var range = Math.Max(1e-6, max - min);

        var departureSteps = porkchop.Window.DepartureSteps;
        var travelSteps = porkchop.Window.TravelTimeSteps;
        var cellWidth = plotRect.Width / (float)Math.Max(1, departureSteps);
        var cellHeight = plotRect.Height / (float)Math.Max(1, travelSteps);

        using var plotBack = new SolidBrush(UiTheme.InputBackground);
        graphics.FillRectangle(plotBack, plotRect);
        graphics.DrawRectangle(borderPen, plotRect);

        Core.PorkchopPoint? bestPoint = null;
        foreach (var point in porkchop.Points)
        {
            if (!point.TotalDeltaV.HasValue)
            {
                continue;
            }

            if (bestPoint is null || point.TotalDeltaV.Value < bestPoint.TotalDeltaV!.Value)
            {
                bestPoint = point;
            }
        }

        for (var x = 0; x < departureSteps; x++)
        {
            for (var y = 0; y < travelSteps; y++)
            {
                var point = porkchop.Points[x * travelSteps + y];
                var drawY = travelSteps - 1 - y;
                var cellRect = new RectangleF(
                    plotRect.X + x * cellWidth,
                    plotRect.Y + drawY * cellHeight,
                    cellWidth + 1,
                    cellHeight + 1);

                if (!point.TotalDeltaV.HasValue)
                {
                    graphics.FillRectangle(invalidBrush, cellRect);
                    continue;
                }

                using var cellBrush = new SolidBrush(InterpolateHeatColor((point.TotalDeltaV.Value - min) / range));
                graphics.FillRectangle(cellBrush, cellRect);
            }
        }

        if (bestPoint is not null)
        {
            var bestIndex = porkchop.Points.IndexOf(bestPoint);
            var bestX = bestIndex / travelSteps;
            var bestY = bestIndex % travelSteps;
            var drawY = travelSteps - 1 - bestY;
            var bestRect = new RectangleF(
                plotRect.X + bestX * cellWidth,
                plotRect.Y + drawY * cellHeight,
                Math.Max(2, cellWidth),
                Math.Max(2, cellHeight));
            graphics.DrawRectangle(bestOutlinePen, Rectangle.Round(bestRect));
            graphics.DrawRectangle(bestInnerPen, Rectangle.Round(RectangleF.Inflate(bestRect, -1, -1)));
        }

        DrawLegend(graphics, legendRect, min, max, smallFont);

        TextRenderer.DrawText(
            graphics,
            $"{BestPointText}: {porkchop.BestTransfer.DVTotal:0.0} m/s",
            smallFont,
            new Rectangle(plotRect.X, rect.Bottom - 26, plotRect.Width, 18),
            UiTheme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            $"{GridText}: {departureSteps} x {travelSteps}",
            smallFont,
            new Rectangle(plotRect.X, rect.Y + 18, plotRect.Width, 14),
            UiTheme.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.EndEllipsis);

        DrawAxisLabel(graphics, new Rectangle(plotRect.X, plotRect.Bottom + 6, plotRect.Width, 18), DepartureAxisText, smallFont, ContentAlignment.MiddleCenter);
        DrawAxisLabel(graphics, new Rectangle(rect.X, plotRect.Y, 48, plotRect.Height), TravelAxisText, smallFont, ContentAlignment.MiddleCenter, rotate: true);

        DrawEdgeLabel(graphics, new Point(plotRect.Left, plotRect.Bottom + 22), ShortDate(porkchop.Window.DepartureStart), smallFont, ContentAlignment.TopLeft);
        DrawEdgeLabel(graphics, new Point(plotRect.Right, plotRect.Bottom + 22), ShortDate(porkchop.Window.DepartureEnd), smallFont, ContentAlignment.TopRight);
        DrawEdgeLabel(graphics, new Point(plotRect.Left - 8, plotRect.Bottom), $"{porkchop.Window.TravelTimeMin / Core.SolarSystemCatalog.SecondsPerDay:0} d", smallFont, ContentAlignment.BottomRight);
        DrawEdgeLabel(graphics, new Point(plotRect.Left - 8, plotRect.Top), $"{porkchop.Window.TravelTimeMax / Core.SolarSystemCatalog.SecondsPerDay:0} d", smallFont, ContentAlignment.TopRight);
    }

    private void DrawBreakdown(Graphics graphics, Rectangle rect, Core.TransferDetails transfer)
    {
        using var titleFont = new Font(Font.FontFamily, 10.0f, FontStyle.Bold);
        using var labelFont = new Font(Font.FontFamily, 9.0f, FontStyle.Regular);
        using var valueFont = new Font(Font.FontFamily, 9.0f, FontStyle.Bold);
        using var baselinePen = new Pen(UiTheme.Border);
        using var ejectionBrush = new SolidBrush(UiTheme.AccentBlue);
        using var insertionBrush = new SolidBrush(UiTheme.AccentAmber);
        using var totalBrush = new SolidBrush(UiTheme.AccentGreen);

        TextRenderer.DrawText(
            graphics,
            BreakdownTitleText,
            titleFont,
            new Rectangle(rect.X, rect.Y, rect.Width, 20),
            UiTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var plotRect = new Rectangle(rect.X + 28, rect.Y + 34, rect.Width - 56, rect.Height - 56);
        if (plotRect.Width <= 0 || plotRect.Height <= 0)
        {
            return;
        }

        var maxValue = Math.Max(1.0, transfer.DVTotal);
        var bars = new[]
        {
            (Label: EjectionDvText, Value: transfer.DVEjection, Brush: ejectionBrush),
            (Label: InsertionDvText, Value: transfer.DVInjection, Brush: insertionBrush),
            (Label: TotalDvText, Value: transfer.DVTotal, Brush: totalBrush)
        };

        var barGap = 18;
        var barWidth = (plotRect.Width - barGap * (bars.Length - 1)) / bars.Length;
        var baselineY = plotRect.Bottom - 28;

        graphics.DrawLine(baselinePen, plotRect.Left, baselineY, plotRect.Right, baselineY);

        for (var i = 0; i < bars.Length; i++)
        {
            var barHeight = (int)Math.Round((baselineY - plotRect.Top - 18) * (bars[i].Value / maxValue));
            var barRect = new Rectangle(plotRect.X + i * (barWidth + barGap), baselineY - barHeight, barWidth, barHeight);
            graphics.FillRectangle(bars[i].Brush, barRect);

            TextRenderer.DrawText(
                graphics,
                $"{bars[i].Value:0.0} m/s",
                valueFont,
                new Rectangle(barRect.X - 12, Math.Max(plotRect.Top, barRect.Y - 22), barRect.Width + 24, 18),
                UiTheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(
                graphics,
                bars[i].Label,
                labelFont,
                new Rectangle(barRect.X - 12, baselineY + 6, barRect.Width + 24, 34),
                UiTheme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }
    }

    private void DrawLegend(Graphics graphics, Rectangle rect, double min, double max, Font font)
    {
        for (var i = 0; i < rect.Height; i++)
        {
            var t = 1.0 - (double)i / Math.Max(1, rect.Height - 1);
            using var pen = new Pen(InterpolateHeatColor(t));
            graphics.DrawLine(pen, rect.Left, rect.Top + i, rect.Right, rect.Top + i);
        }

        using var borderPen = new Pen(UiTheme.BorderStrong);
        graphics.DrawRectangle(borderPen, rect);

        TextRenderer.DrawText(
            graphics,
            $"{LegendHighText}: {max:0.0}",
            font,
            new Rectangle(rect.X - 56, rect.Y - 2, 132, 16),
            UiTheme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            $"{LegendLowText}: {min:0.0}",
            font,
            new Rectangle(rect.X - 56, rect.Bottom - 14, 132, 16),
            UiTheme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void DrawCard(Graphics graphics, Rectangle rect, string label, string value, Brush background, Pen border, Font labelFont, Font valueFont)
    {
        graphics.FillRectangle(background, rect);
        graphics.DrawRectangle(border, rect);

        TextRenderer.DrawText(
            graphics,
            label,
            labelFont,
            new Rectangle(rect.X + 12, rect.Y + 10, rect.Width - 24, 18),
            UiTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            value,
            valueFont,
            new Rectangle(rect.X + 12, rect.Y + 30, rect.Width - 24, rect.Height - 36),
            UiTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawInfoBlock(Graphics graphics, Rectangle rect, string label, string value, Font font)
    {
        var blockRect = Rectangle.Inflate(rect, 0, -2);
        using var labelBrush = new SolidBrush(UiTheme.TextMuted);
        using var valueBrush = new SolidBrush(UiTheme.TextSecondary);

        graphics.DrawString(label, font, labelBrush, new RectangleF(blockRect.X, blockRect.Y, blockRect.Width, 16));
        graphics.DrawString(value, font, valueBrush, new RectangleF(blockRect.X, blockRect.Y + 16, blockRect.Width, blockRect.Height - 16));
    }

    private void DrawCenteredMessage(Graphics graphics, Rectangle rect, string message)
    {
        using var font = new Font(Font.FontFamily, 10.0f, FontStyle.Regular);
        TextRenderer.DrawText(
            graphics,
            message,
            font,
            rect,
            UiTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private void DrawAxisLabel(Graphics graphics, Rectangle rect, string text, Font font, ContentAlignment alignment, bool rotate = false)
    {
        if (!rotate)
        {
            TextRenderer.DrawText(graphics, text, font, rect, UiTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        graphics.TranslateTransform(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
        graphics.RotateTransform(-90);
        var rotatedRect = new Rectangle(-rect.Height / 2, -rect.Width / 2, rect.Height, rect.Width);
        TextRenderer.DrawText(graphics, text, font, rotatedRect, UiTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        graphics.ResetTransform();
    }

    private static void DrawEdgeLabel(Graphics graphics, Point point, string text, Font font, ContentAlignment alignment)
    {
        var size = TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var origin = alignment switch
        {
            ContentAlignment.TopRight => new Point(point.X - size.Width, point.Y),
            ContentAlignment.BottomRight => new Point(point.X - size.Width, point.Y - size.Height),
            ContentAlignment.TopLeft => point,
            _ => new Point(point.X, point.Y - size.Height)
        };

        TextRenderer.DrawText(graphics, text, font, new Rectangle(origin, size), UiTheme.TextMuted, TextFormatFlags.NoPadding);
    }

    private string FormatDate(double seconds)
    {
        return _calendar?.FormatDate(seconds) ?? $"{seconds:0.###} s";
    }

    private string FormatDuration(double seconds)
    {
        return _calendar?.FormatDuration(seconds) ?? $"{seconds:0.###} s";
    }

    private string ShortDate(double seconds)
    {
        var text = FormatDate(seconds);
        return text.Length >= 10 && text[4] == '-' && text[7] == '-' ? text[..10] : text;
    }

    private static string FormatPhaseAngle(Core.TransferDetails transfer)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{transfer.PhaseAngle * Core.LambertSolver.Rad2Deg:0.00} deg ({transfer.DepartureSeparation * Core.LambertSolver.Rad2Deg:0.00} sep)");
    }

    private static Color InterpolateHeatColor(double value)
    {
        value = Math.Clamp(value, 0.0, 1.0);
        if (value < 0.5)
        {
            return Lerp(Color.FromArgb(72, 163, 116), Color.FromArgb(243, 190, 76), value * 2.0);
        }

        return Lerp(Color.FromArgb(243, 190, 76), Color.FromArgb(210, 92, 78), (value - 0.5) * 2.0);
    }

    private static Color Lerp(Color from, Color to, double amount)
    {
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }
}

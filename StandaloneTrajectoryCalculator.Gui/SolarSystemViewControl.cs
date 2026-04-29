using System.Drawing.Drawing2D;
using Core = StandaloneTrajectoryCalculator;

namespace StandaloneTrajectoryCalculator.Gui;

internal sealed class SolarSystemViewControl : Control
{
    private static readonly IReadOnlyList<Core.OrbitalBody> Bodies = Core.SolarSystemCatalog.CreateOrbitalBodies();

    private Core.TransferDetails? _transfer;
    private Core.MissionCalendar? _calendar;

    public SolarSystemViewControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = UiTheme.PanelBackground;
    }

    public string EmptyText = "Run a calculation to see the departure geometry.";
    public string PhaseAngleText = "Phase angle";
    public string DepartureText = "Departure";
    public string TravelText = "Travel";
    public string LongWayText = "Long-way";
    public string YesText = "yes";
    public string NoText = "no";
    public string OriginDepartureText = "Origin at departure";
    public string DestinationDepartureText = "Destination at departure";
    public string DestinationArrivalText = "Destination at arrival";
    public string TransferPathText = "Transfer path";
    public string ScaleNoteText = "Orbit sizes are visually compressed so inner and outer planets fit in one view.";

    public void SetResult(Core.TransferDetails? transfer, Core.MissionCalendar? calendar)
    {
        _transfer = transfer;
        _calendar = calendar;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var client = Rectangle.Inflate(ClientRectangle, -16, -16);
        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        if (_transfer is null)
        {
            DrawCenteredMessage(e.Graphics, client, EmptyText);
            return;
        }

        Rectangle mapRect;
        Rectangle infoRect;
        if (client.Width >= 860)
        {
            var infoWidth = 250;
            mapRect = new Rectangle(client.X, client.Y, client.Width - infoWidth - 16, client.Height);
            infoRect = new Rectangle(mapRect.Right + 16, client.Y, infoWidth, client.Height);
        }
        else
        {
            var infoHeight = 146;
            mapRect = new Rectangle(client.X, client.Y, client.Width, client.Height - infoHeight - 16);
            infoRect = new Rectangle(client.X, mapRect.Bottom + 16, client.Width, infoHeight);
        }

        DrawMap(e.Graphics, mapRect, _transfer);
        DrawInfoPanel(e.Graphics, infoRect, _transfer);
    }

    private void DrawMap(Graphics graphics, Rectangle rect, Core.TransferDetails transfer)
    {
        var size = Math.Min(rect.Width, rect.Height);
        var square = new Rectangle(rect.X + (rect.Width - size) / 2, rect.Y + (rect.Height - size) / 2, size, size);
        var center = new PointF(square.Left + square.Width / 2f, square.Top + square.Height / 2f);
        var maxOrbitRadius = square.Width / 2f - 32f;
        var maxSemiMajorAxis = Bodies.Max(body => body.Orbit.SemiMajorAxis);

        using var orbitPen = new Pen(UiTheme.Border, 1f);
        using var phasePen = new Pen(UiTheme.AccentBlue, 2.2f);
        using var transferPen = new Pen(UiTheme.AccentGreen, 2.0f) { DashStyle = DashStyle.Dash };
        using var titleFont = new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
        using var sunBrush = new SolidBrush(Color.FromArgb(248, 191, 68));

        var positionsAtDeparture = Bodies.ToDictionary(
            body => body.Name,
            body => body.Orbit.PositionAtTime(transfer.DepartureTime),
            StringComparer.OrdinalIgnoreCase);

        foreach (var body in Bodies)
        {
            var orbitRadius = ScaleDistance(body.Orbit.SemiMajorAxis, maxSemiMajorAxis, maxOrbitRadius);
            using var pen = new Pen(BodyColor(body.Name) == Color.Empty ? orbitPen.Color : Color.FromArgb(72, BodyColor(body.Name)), body.Name.Equals(transfer.OriginName, StringComparison.OrdinalIgnoreCase) || body.Name.Equals(transfer.DestinationName, StringComparison.OrdinalIgnoreCase) ? 1.8f : 1f);
            graphics.DrawEllipse(pen, center.X - orbitRadius, center.Y - orbitRadius, orbitRadius * 2f, orbitRadius * 2f);
        }

        var originDeparture = ProjectPoint(center, maxSemiMajorAxis, maxOrbitRadius, transfer.OriginPositionAtDeparture);
        var destinationDeparture = positionsAtDeparture.TryGetValue(transfer.DestinationName, out var destinationDepartureVector)
            ? ProjectPoint(center, maxSemiMajorAxis, maxOrbitRadius, destinationDepartureVector)
            : ProjectPoint(center, maxSemiMajorAxis, maxOrbitRadius, transfer.DestinationPositionAtArrival);
        var destinationArrival = ProjectPoint(center, maxSemiMajorAxis, maxOrbitRadius, transfer.DestinationPositionAtArrival);

        graphics.DrawLine(transferPen, originDeparture, destinationArrival);
        DrawPhaseArc(graphics, center, originDeparture, transfer.PhaseAngle, Math.Max(30f, Math.Min(Distance(center, originDeparture), Distance(center, destinationDeparture)) * 0.62f), phasePen);

        graphics.FillEllipse(sunBrush, center.X - 9f, center.Y - 9f, 18f, 18f);
        graphics.DrawEllipse(Pens.Goldenrod, center.X - 9f, center.Y - 9f, 18f, 18f);

        DrawMarker(graphics, originDeparture, BodyColor(transfer.OriginName), transfer.OriginName, titleFont);
        DrawMarker(graphics, destinationDeparture, BodyColor(transfer.DestinationName), transfer.DestinationName, titleFont);
        DrawMarker(graphics, destinationArrival, Color.White, string.Empty, titleFont, outlineColor: BodyColor(transfer.DestinationName), diameter: 13f);
    }

    private void DrawInfoPanel(Graphics graphics, Rectangle rect, Core.TransferDetails transfer)
    {
        using var panelBrush = new SolidBrush(UiTheme.CardBackground);
        using var borderPen = new Pen(UiTheme.Border);
        using var titleFont = new Font(Font.FontFamily, 11.0f, FontStyle.Bold);
        using var labelFont = new Font(Font.FontFamily, 9.0f, FontStyle.Regular);
        using var valueFont = new Font(Font.FontFamily, 9.2f, FontStyle.Bold);

        graphics.FillRectangle(panelBrush, rect);
        graphics.DrawRectangle(borderPen, rect);

        var textRect = Rectangle.Inflate(rect, -14, -14);
        TextRenderer.DrawText(
            graphics,
            $"{transfer.OriginName} -> {transfer.DestinationName}",
            titleFont,
            new Rectangle(textRect.X, textRect.Y, textRect.Width, 24),
            UiTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        var rowY = textRect.Y + 32;
        rowY = DrawInfoRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), PhaseAngleText, FormatPhaseAngle(transfer), labelFont, valueFont);
        rowY = DrawInfoRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), DepartureText, FormatDate(transfer.DepartureTime), labelFont, valueFont);
        rowY = DrawInfoRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), TravelText, FormatDuration(transfer.TravelTime), labelFont, valueFont);
        rowY = DrawInfoRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), LongWayText, transfer.LongWay ? YesText : NoText, labelFont, valueFont);

        rowY += 8;
        rowY = DrawLegendRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), BodyColor(transfer.OriginName), OriginDepartureText, labelFont);
        rowY = DrawLegendRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), BodyColor(transfer.DestinationName), DestinationDepartureText, labelFont);
        rowY = DrawLegendRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), Color.White, DestinationArrivalText, labelFont, outlineColor: BodyColor(transfer.DestinationName));
        rowY = DrawLineLegendRow(graphics, new Rectangle(textRect.X, rowY, textRect.Width, 18), TransferPathText, labelFont);

        TextRenderer.DrawText(
            graphics,
            ScaleNoteText,
            labelFont,
            new Rectangle(textRect.X, Math.Min(textRect.Bottom - 38, rowY + 10), textRect.Width, 38),
            UiTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);
    }

    private static int DrawInfoRow(Graphics graphics, Rectangle rect, string label, string value, Font labelFont, Font valueFont)
    {
        var labelRect = new Rectangle(rect.X, rect.Y, Math.Min(116, rect.Width / 2), rect.Height);
        var valueRect = new Rectangle(labelRect.Right + 6, rect.Y, rect.Right - labelRect.Right - 6, rect.Height);

        TextRenderer.DrawText(graphics, label, labelFont, labelRect, UiTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(graphics, value, valueFont, valueRect, UiTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        return rect.Bottom + 4;
    }

    private static int DrawLegendRow(Graphics graphics, Rectangle rect, Color fillColor, string text, Font font, Color? outlineColor = null)
    {
        var markerRect = new Rectangle(rect.X, rect.Y + 3, 12, 12);
        using var fillBrush = new SolidBrush(fillColor);
        var borderColor = outlineColor ?? (fillColor == Color.White ? Color.FromArgb(86, 98, 109) : fillColor);
        using var outlinePen = new Pen(borderColor);

        graphics.FillEllipse(fillBrush, markerRect);
        graphics.DrawEllipse(outlinePen, markerRect);
        TextRenderer.DrawText(graphics, text, font, new Rectangle(markerRect.Right + 8, rect.Y, rect.Width - markerRect.Width - 8, rect.Height), UiTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        return rect.Bottom + 4;
    }

    private static int DrawLineLegendRow(Graphics graphics, Rectangle rect, string text, Font font)
    {
        using var pen = new Pen(UiTheme.AccentGreen, 2f) { DashStyle = DashStyle.Dash };
        graphics.DrawLine(pen, rect.X, rect.Y + rect.Height / 2, rect.X + 18, rect.Y + rect.Height / 2);
        TextRenderer.DrawText(graphics, text, font, new Rectangle(rect.X + 26, rect.Y, rect.Width - 26, rect.Height), UiTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        return rect.Bottom + 4;
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

    private static void DrawMarker(Graphics graphics, PointF point, Color fillColor, string label, Font font, Color? outlineColor = null, float diameter = 12f)
    {
        using var fillBrush = new SolidBrush(fillColor);
        using var outlinePen = new Pen(outlineColor ?? fillColor, fillColor == Color.White ? 1.8f : 1.0f);
        graphics.FillEllipse(fillBrush, point.X - diameter / 2f, point.Y - diameter / 2f, diameter, diameter);
        graphics.DrawEllipse(outlinePen, point.X - diameter / 2f, point.Y - diameter / 2f, diameter, diameter);

        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var textSize = TextRenderer.MeasureText(graphics, label, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var labelPoint = new Point((int)Math.Round(point.X + 8f), (int)Math.Round(point.Y - textSize.Height - 4f));
        TextRenderer.DrawText(graphics, label, font, new Rectangle(labelPoint, textSize), UiTheme.TextPrimary, TextFormatFlags.NoPadding);
    }

    private static void DrawPhaseArc(Graphics graphics, PointF center, PointF startPoint, double sweepRadians, float radius, Pen pen)
    {
        var startAngle = Math.Atan2(center.Y - startPoint.Y, startPoint.X - center.X);
        var segments = Math.Max(18, (int)(Math.Abs(sweepRadians) * 24));
        var points = new PointF[segments + 1];

        for (var i = 0; i <= segments; i++)
        {
            var angle = startAngle + sweepRadians * i / segments;
            points[i] = new PointF(
                center.X + radius * (float)Math.Cos(angle),
                center.Y - radius * (float)Math.Sin(angle));
        }

        graphics.DrawLines(pen, points);
    }

    private string FormatDate(double seconds)
    {
        return _calendar?.FormatDate(seconds) ?? $"{seconds:0.###} s";
    }

    private string FormatDuration(double seconds)
    {
        return _calendar?.FormatDuration(seconds) ?? $"{seconds:0.###} s";
    }

    private static string FormatPhaseAngle(Core.TransferDetails transfer)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{transfer.PhaseAngle * Core.LambertSolver.Rad2Deg:0.00} deg ({transfer.DepartureSeparation * Core.LambertSolver.Rad2Deg:0.00} sep)");
    }

    private static PointF ProjectPoint(PointF center, double maxSemiMajorAxis, float maxDisplayRadius, Core.Vector3D position)
    {
        var angle = Math.Atan2(position.Y, position.X);
        var distance = position.Magnitude;
        var scaledDistance = ScaleDistance(distance, maxSemiMajorAxis, maxDisplayRadius);
        return new PointF(
            center.X + scaledDistance * (float)Math.Cos(angle),
            center.Y - scaledDistance * (float)Math.Sin(angle));
    }

    private static float ScaleDistance(double actualDistance, double maxSemiMajorAxis, float maxDisplayRadius)
    {
        if (actualDistance <= 0 || maxSemiMajorAxis <= 0 || maxDisplayRadius <= 0)
        {
            return 0f;
        }

        var normalized = Math.Sqrt(Math.Clamp(actualDistance / maxSemiMajorAxis, 0.0, 1.0));
        return (float)(normalized * maxDisplayRadius);
    }

    private static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static Color BodyColor(string bodyName)
    {
        return bodyName.ToLowerInvariant() switch
        {
            "mercury" => Color.FromArgb(142, 131, 120),
            "venus" => Color.FromArgb(201, 155, 86),
            "earth" => Color.FromArgb(74, 136, 220),
            "mars" => Color.FromArgb(211, 109, 72),
            "jupiter" => Color.FromArgb(192, 149, 104),
            "saturn" => Color.FromArgb(203, 186, 125),
            "uranus" => Color.FromArgb(116, 198, 212),
            "neptune" => Color.FromArgb(82, 116, 214),
            _ => Color.FromArgb(120, 120, 120)
        };
    }
}

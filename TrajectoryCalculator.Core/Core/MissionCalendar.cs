using System.Globalization;

namespace TrajectoryCalculator;

/// <summary>
/// Calendar utility for mission date/time arithmetic and formatting.
/// </summary>
public sealed class MissionCalendar
{
    private readonly DateTimeOffset? _epochUtc;
    private readonly bool _useUtcFormatting;

    public MissionCalendar(
        int epochYear,
        int epochDayOfYear,
        int daysPerYear,
        int hoursPerDay,
        int minutesPerHour,
        int secondsPerMinute,
        bool useUtcFormatting = false,
        DateTimeOffset? epochUtc = null)
    {
        if (daysPerYear <= 0 || hoursPerDay <= 0 || minutesPerHour <= 0 || secondsPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(daysPerYear), "Calendar units must be positive.");
        }

        EpochYear = epochYear;
        EpochDayOfYear = epochDayOfYear;
        DaysPerYear = daysPerYear;
        HoursPerDay = hoursPerDay;
        MinutesPerHour = minutesPerHour;
        SecondsPerMinute = secondsPerMinute;
        _useUtcFormatting = useUtcFormatting;
        _epochUtc = epochUtc?.ToUniversalTime();
    }

    public int EpochYear { get; }
    public int EpochDayOfYear { get; }
    public int DaysPerYear { get; }
    public int HoursPerDay { get; }
    public int MinutesPerHour { get; }
    public int SecondsPerMinute { get; }
    public bool UsesUtcFormatting => _useUtcFormatting;
    public DateTimeOffset? EpochUtc => _epochUtc;
    public long SecondsPerHour => (long)MinutesPerHour * SecondsPerMinute;
    public long SecondsPerDay => SecondsPerHour * HoursPerDay;
    public long SecondsPerYear => SecondsPerDay * DaysPerYear;

    public static MissionCalendar FromInput(CalendarInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var kind = input.Kind?.Trim().ToLowerInvariant() ?? "custom";
        if (kind == "utc")
        {
            var epochUtc = ParseEpochUtc(input.EpochUtc);
            return new MissionCalendar(
                epochUtc.Year,
                epochUtc.DayOfYear,
                365,
                24,
                60,
                60,
                useUtcFormatting: true,
                epochUtc: epochUtc);
        }

        return new MissionCalendar(
            input.EpochYear,
            input.EpochDayOfYear,
            input.DaysPerYear,
            input.HoursPerDay,
            input.MinutesPerHour,
            input.SecondsPerMinute);
    }

    public string FormatDate(double utSeconds)
    {
        if (_useUtcFormatting && _epochUtc.HasValue)
        {
            return _epochUtc.Value.AddSeconds(utSeconds)
                .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }

        var sign = utSeconds < 0 ? "- " : string.Empty;
        var totalSeconds = (long)Math.Round(Math.Abs(utSeconds), MidpointRounding.AwayFromZero);

        var year = EpochYear + (int)(totalSeconds / SecondsPerYear);
        totalSeconds %= SecondsPerYear;

        var day = EpochDayOfYear + (int)(totalSeconds / SecondsPerDay);
        totalSeconds %= SecondsPerDay;

        if (day > DaysPerYear)
        {
            year += (day - 1) / DaysPerYear;
            day = ((day - 1) % DaysPerYear) + 1;
        }

        var hour = (int)(totalSeconds / SecondsPerHour);
        totalSeconds %= SecondsPerHour;
        var minute = (int)(totalSeconds / SecondsPerMinute);
        var second = (int)(totalSeconds % SecondsPerMinute);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}Year {year}, Day {day}, {hour:D2}:{minute:D2}:{second:D2}");
    }

    public string FormatDuration(double durationSeconds)
    {
        if (_useUtcFormatting)
        {
            return FormatUtcDuration(durationSeconds);
        }

        var sign = durationSeconds < 0 ? "- " : string.Empty;
        var totalSeconds = (long)Math.Round(Math.Abs(durationSeconds), MidpointRounding.AwayFromZero);

        var years = totalSeconds / SecondsPerYear;
        totalSeconds %= SecondsPerYear;
        var days = totalSeconds / SecondsPerDay;
        totalSeconds %= SecondsPerDay;
        var hours = totalSeconds / SecondsPerHour;
        totalSeconds %= SecondsPerHour;
        var minutes = totalSeconds / SecondsPerMinute;
        var seconds = totalSeconds % SecondsPerMinute;

        if (years > 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sign}{years}y, {days}d, {hours:D2}:{minutes:D2}:{seconds:D2}");
        }

        if (days > 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sign}{days}d, {hours:D2}:{minutes:D2}:{seconds:D2}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{hours:D2}:{minutes:D2}:{seconds:D2}");
    }

    private static DateTimeOffset ParseEpochUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SolarSystemCatalog.J2000Utc;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        throw new InvalidOperationException($"'{value}' is not a valid UTC epoch.");
    }

    private static string FormatUtcDuration(double durationSeconds)
    {
        var sign = durationSeconds < 0 ? "- " : string.Empty;
        var duration = TimeSpan.FromSeconds(Math.Abs(durationSeconds));
        var wholeDays = (long)Math.Floor(duration.TotalDays);

        if (wholeDays > 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sign}{wholeDays}d, {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
    }
}

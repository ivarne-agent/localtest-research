using System.Text;

namespace Altinn.Register.TestUtils.Tracing;

internal static class TimeSpanExtensions
{
    private static readonly TimeSpan _day = TimeSpan.FromDays(1);
    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan _month = TimeSpan.FromDays(30);
    private static readonly TimeSpan _year = TimeSpan.FromDays(365);

    public static void ToFriendlyString(this TimeSpan ts, StringBuilder sb)
    {
        if (ts.Equals(_month))
        {
            sb.Append("1M");
        }

        if (ts.Equals(_year))
        {
            sb.Append("1y");
        }

        if (ts.Equals(_day))
        {
            sb.Append("1d");
        }

        if (ts.Equals(_hour))
        {
            sb.Append("1h");
        }

        var years = ts.Days / 365;
        var months = (ts.Days % 365) / 30;
        var weeks = ((ts.Days % 365) % 30) / 7;
        var days = ((ts.Days % 365) % 30) % 7;

        if (years > 0)
        {
            sb.Append(years).Append("y");
        }

        if (months > 0)
        {
            sb.Append(months).Append("M");
        }

        if (weeks > 0)
        {
            sb.Append(weeks).Append("w");
        }

        if (days > 0)
        {
            sb.Append(days).Append("d");
        }

        if (ts.Hours > 0)
        {
            sb.Append(ts.Hours).Append("h");
        }

        if (ts.Minutes > 0)
        {
            sb.Append(ts.Minutes).Append("m");
        }

        if (ts.Seconds > 0)
        {
            sb.Append(ts.Seconds).Append("s");
        }

        if (ts.Milliseconds > 0)
        {
            sb.Append(ts.Milliseconds).Append("ms");
        }

        if (ts.Ticks == 0)
        {
            sb.Append("-0-");
        }
        else if (sb.Length == 0)
        {
            var nanos = ts.Ticks * 100;
            if (nanos > 1000)
            {
                sb.Append((nanos + 500) / 1000).Append("\x00B5s");
            }
            else
            {
                sb.Append(nanos).Append("ns");
            }
        }
    }
}

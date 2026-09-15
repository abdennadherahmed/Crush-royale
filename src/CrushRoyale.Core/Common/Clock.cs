using System;

namespace CrushRoyale.Core.Common
{
    /// <summary>Wall-clock abstraction (lives recharge, daily resets, seasons). Never used inside a match simulation.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>Controllable clock for tests, tools and deterministic server jobs.</summary>
    public sealed class ManualClock : IClock
    {
        private DateTime _now;

        public ManualClock(DateTime utcNow)
        {
            _now = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }

        public DateTime UtcNow => _now;

        public void Set(DateTime utcNow) => _now = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    /// <summary>UTC calendar helpers shared by client and server so both agree on "today" and "this week".</summary>
    public static class TimeUtil
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Monday 1970-01-05 00:00 UTC: first week boundary after the Unix epoch.</summary>
        private static readonly DateTime FirstMonday = new DateTime(1970, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Number of whole UTC days since the Unix epoch.</summary>
        public static int DayIndex(DateTime utc) => (int)Math.Floor((ToUtc(utc) - Epoch).TotalDays);

        /// <summary>
        /// Week index. Weeks start Monday 00:00 UTC, i.e. right after "Sunday midnight UTC" as in the GDD.
        /// </summary>
        public static int WeekIndex(DateTime utc) => (int)Math.Floor((ToUtc(utc) - FirstMonday).TotalDays / 7.0);

        public static DateTime StartOfDay(DateTime utc) => ToUtc(utc).Date;

        public static DateTime NextDailyReset(DateTime utc) => StartOfDay(utc).AddDays(1);

        public static DateTime WeekStart(int weekIndex) => FirstMonday.AddDays(weekIndex * 7.0);

        /// <summary>The next weekly reset (the upcoming Monday 00:00 UTC = Sunday midnight).</summary>
        public static DateTime NextWeeklyReset(DateTime utc) => WeekStart(WeekIndex(utc) + 1);

        public static long ToUnixMs(DateTime utc) => (long)(ToUtc(utc) - Epoch).TotalMilliseconds;

        public static DateTime FromUnixMs(long ms) => Epoch.AddMilliseconds(ms);

        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}

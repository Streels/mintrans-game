using Mintrans.Simulation.Systems;
using Mintrans.Simulation.Time;

namespace Mintrans.Simulation.Tests;

/// <summary>Календарь кампании (docs/14): с 1 января 1950 года, григорианские месяцы и кварталы.</summary>
public class CalendarTests
{
    private static readonly GameCalendar Calendar = GameCalendar.Default;

    [Fact]
    public void Campaign_starts_on_first_of_january_1950()
    {
        Assert.Equal(new DateTime(1950, 1, 1), Calendar.DateAt(GameTime.Start));
        Assert.Equal("1 января 1950, 00:00", Calendar.Format(GameTime.Start));
        Assert.Equal("14 марта 1950, 08:00", Calendar.Format(new GameTime((31 + 28 + 13) * 24 + 8)));
    }

    [Fact]
    public void Months_quarters_and_leap_years_follow_real_calendar()
    {
        Assert.True(Calendar.IsStartOfMonth(GameTime.FromDays(31)));          // 1 февраля 1950
        Assert.False(Calendar.IsStartOfQuarter(GameTime.FromDays(31)));
        Assert.True(Calendar.IsStartOfQuarter(GameTime.FromDays(31 + 28 + 31))); // 1 апреля 1950
        Assert.Equal(2, Calendar.Quarter(GameTime.FromDays(31 + 28 + 31)));
        Assert.Equal(new DateTime(1952, 2, 29), Calendar.DateAt(Calendar.StartOfYear(1952).AddHours(24 * 59)).Date);
        Assert.True(Calendar.IsStartOfYear(Calendar.StartOfYear(1956)));
        Assert.Equal(1955, Calendar.Year(Calendar.StartOfYear(1956).AddHours(-1)));
    }

    [Fact]
    public void Calendar_schedules_fire_at_month_quarter_and_year_starts()
    {
        var sim = TestSimulation.Create();
        var log = new List<string>();
        var monthly = new RecordingSystem("month", Schedule.Monthly, Phases.Statistics, log);
        var quarterly = new RecordingSystem("quarter", Schedule.Quarterly, Phases.Growth, log);
        var yearly = new RecordingSystem("year", Schedule.Yearly, Phases.Statistics, log);
        sim.Register(monthly);
        sim.Register(quarterly);
        sim.Register(yearly);

        // 1950 и 1951 годы целиком: 365 + 365 суток.
        sim.Run(24L * 730);

        Assert.Equal(24, monthly.Calls);
        Assert.Equal(8, quarterly.Calls);
        Assert.Equal(2, yearly.Calls);
        Assert.Equal(1952, sim.State.Year);
    }

    [Fact]
    public void Custom_start_date_is_respected()
    {
        var calendar = new GameCalendar(new DateTime(1960, 7, 1));
        Assert.Equal(1960, calendar.Year(GameTime.Start));
        Assert.Equal(3, calendar.Quarter(GameTime.Start));
    }
}

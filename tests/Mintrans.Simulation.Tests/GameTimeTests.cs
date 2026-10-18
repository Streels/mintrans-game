using Mintrans.Simulation.Time;

namespace Mintrans.Simulation.Tests;

public class GameTimeTests
{
    [Fact]
    public void Day_and_week_are_derived_from_hours()
    {
        var time = new GameTime(24 * 9 + 5);
        Assert.Equal(9, time.Day);
        Assert.Equal(5, time.HourOfDay);
        Assert.Equal(1, time.Week);
        Assert.Equal(168, GameTime.HoursPerWeek);
    }

    [Fact]
    public void Text_counts_days_from_one()
    {
        Assert.Equal("День 1, 00:00", GameTime.Start.ToString());
        Assert.Equal("День 3, 14:00", new GameTime(2 * 24 + 14).ToString());
    }

    [Fact]
    public void Start_of_day_is_detected()
    {
        Assert.True(GameTime.FromDays(3).IsStartOfDay);
        Assert.False(GameTime.FromDays(3).AddHours(1).IsStartOfDay);
    }
}

using System.Globalization;

namespace Mintrans.Simulation.Time;

/// <summary>
/// Календарь кампании (docs/14): счётчик часов <see cref="GameTime"/> переводится в обычные даты
/// григорианского календаря, начиная с даты старта (по умолчанию 1 января 1950 года, 00:00).
/// Месяцы и кварталы — календарные, с високосными годами.
/// </summary>
public sealed class GameCalendar(DateTime start)
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static GameCalendar Default { get; } = new(new DateTime(1950, 1, 1));

    public DateTime Start { get; } = start.Date;

    public DateTime DateAt(GameTime time) => Start.AddHours(time.Hours);

    public int Year(GameTime time) => DateAt(time).Year;

    /// <summary>Номер квартала года: 1…4.</summary>
    public int Quarter(GameTime time) => (DateAt(time).Month - 1) / 3 + 1;

    public bool IsStartOfMonth(GameTime time)
    {
        var date = DateAt(time);
        return date.Day == 1 && date.Hour == 0;
    }

    public bool IsStartOfQuarter(GameTime time) => IsStartOfMonth(time) && (DateAt(time).Month - 1) % 3 == 0;

    public bool IsStartOfYear(GameTime time) => IsStartOfMonth(time) && DateAt(time).Month == 1;

    /// <summary>Игровое время, когда наступит начало указанного года.</summary>
    public GameTime StartOfYear(int year) => new((long)(new DateTime(year, 1, 1) - Start).TotalHours);

    /// <summary>«14 марта 1950, 08:00».</summary>
    public string Format(GameTime time) => DateAt(time).ToString("d MMMM yyyy, HH:mm", Russian);

    /// <summary>«14 марта 1950».</summary>
    public string FormatDate(GameTime time) => DateAt(time).ToString("d MMMM yyyy", Russian);
}

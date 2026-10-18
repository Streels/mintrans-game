namespace Mintrans.Simulation.Time;

/// <summary>
/// Игровое время — целое число прошедших игровых часов (docs/03_simulation_time.md).
/// Сутки и недели вычисляются из него. Календарь с годами появится вместе с темой 14 «Эпоха».
/// </summary>
public readonly record struct GameTime(long Hours) : IComparable<GameTime>
{
    public const int HoursPerDay = 24;
    public const int DaysPerWeek = 7;
    public const int HoursPerWeek = HoursPerDay * DaysPerWeek;

    public static readonly GameTime Start = new(0);

    /// <summary>Номер суток с нуля.</summary>
    public long Day => Hours / HoursPerDay;

    public int HourOfDay => (int)(Hours % HoursPerDay);

    /// <summary>Номер недели с нуля.</summary>
    public long Week => Hours / HoursPerWeek;

    /// <summary>Ровно начало суток (граница между сутками).</summary>
    public bool IsStartOfDay => HourOfDay == 0;

    public GameTime AddHours(long hours) => new(Hours + hours);

    public static GameTime FromDays(long days) => new(days * HoursPerDay);

    public int CompareTo(GameTime other) => Hours.CompareTo(other.Hours);

    public static bool operator <(GameTime a, GameTime b) => a.Hours < b.Hours;
    public static bool operator >(GameTime a, GameTime b) => a.Hours > b.Hours;
    public static bool operator <=(GameTime a, GameTime b) => a.Hours <= b.Hours;
    public static bool operator >=(GameTime a, GameTime b) => a.Hours >= b.Hours;

    /// <summary>«День 3, 14:00» — дни для игрока считаются с единицы.</summary>
    public override string ToString() => $"День {Day + 1}, {HourOfDay:00}:00";
}

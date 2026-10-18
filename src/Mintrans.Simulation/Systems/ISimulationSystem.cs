using Mintrans.Simulation.Time;

namespace Mintrans.Simulation.Systems;

/// <summary>
/// Часть игровой логики, которую симуляция вызывает по расписанию: движение транспорта,
/// производство, потребление города, статистика. Каждый будущий модуль (экономика, транспорт)
/// добавляет свои системы, не меняя ядро симуляции.
/// </summary>
public interface ISimulationSystem
{
    /// <summary>Имя для журнала и отладки.</summary>
    string Name { get; }

    /// <summary>Когда вызывать.</summary>
    Schedule Schedule { get; }

    /// <summary>Место в порядке расчёта внутри одного шага; см. <see cref="Phases"/>.</summary>
    int Phase { get; }

    void Run(SimulationState state);
}

/// <summary>Период вызова системы в игровых часах. Вызов — в конце каждого полного периода.</summary>
public readonly record struct Schedule(int PeriodHours, CalendarPeriod Calendar = CalendarPeriod.None)
{
    /// <summary>Каждый игровой час.</summary>
    public static readonly Schedule Hourly = new(1);

    /// <summary>Раз в игровые сутки, на их границе — после часовых расчётов последнего часа.</summary>
    public static readonly Schedule Daily = new(GameTime.HoursPerDay);

    /// <summary>Раз в игровую неделю.</summary>
    public static readonly Schedule Weekly = new(GameTime.HoursPerWeek);

    /// <summary>В начале каждого календарного месяца.</summary>
    public static readonly Schedule Monthly = new(0, CalendarPeriod.Month);

    /// <summary>В начале каждого календарного квартала (рост предприятий и городов, docs/05).</summary>
    public static readonly Schedule Quarterly = new(0, CalendarPeriod.Quarter);

    /// <summary>В начале каждого года (открытие новой техники, docs/14).</summary>
    public static readonly Schedule Yearly = new(0, CalendarPeriod.Year);

    public bool IsValid => Calendar != CalendarPeriod.None || PeriodHours > 0;

    /// <summary>Нужно ли вызвать систему, когда только что завершился час и время стало <paramref name="now"/>.</summary>
    public bool IsDue(GameTime now, GameCalendar calendar) => Calendar switch
    {
        CalendarPeriod.Month => calendar.IsStartOfMonth(now),
        CalendarPeriod.Quarter => calendar.IsStartOfQuarter(now),
        CalendarPeriod.Year => calendar.IsStartOfYear(now),
        _ => PeriodHours > 0 && now.Hours % PeriodHours == 0,
    };
}

/// <summary>Календарный период расписания: месяцы и кварталы разной длины, поэтому считаются по датам.</summary>
public enum CalendarPeriod
{
    None,
    Month,
    Quarter,
    Year,
}

/// <summary>
/// Порядок расчётов внутри шага (docs/03): сначала прибытия и перевозки данного часа,
/// затем производство, потребление и, последней, статистика завершившегося периода.
/// Значения с промежутками, чтобы новые этапы можно было вставить между существующими.
/// </summary>
public static class Phases
{
    public const int Commands = 0;
    public const int Transport = 100;
    public const int Production = 200;
    public const int Consumption = 300;
    public const int Growth = 400;
    public const int Statistics = 900;
}

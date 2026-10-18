namespace Mintrans.Simulation.Time;

/// <summary>Настройки темпа из content/simulation.json.</summary>
/// <param name="SecondsPerHourAt1x">Сколько реальных секунд длится игровой час на скорости 1×.</param>
/// <param name="Speeds">Доступные множители скорости, например 1, 2, 4, 8.</param>
/// <param name="MaxStepsPerFrame">Предел шагов за один кадр: если компьютер не успевает, игра замедляется, а не зависает.</param>
public sealed record SimulationSettings(double SecondsPerHourAt1x, IReadOnlyList<int> Speeds, int MaxStepsPerFrame)
{
    public static SimulationSettings Default { get; } = new(2.0, [1, 2, 4, 8], 32);

    public void Validate()
    {
        if (SecondsPerHourAt1x <= 0)
            throw new ArgumentException("Длительность игрового часа должна быть положительной.");
        if (Speeds.Count == 0 || Speeds.Any(s => s <= 0))
            throw new ArgumentException("Нужна хотя бы одна положительная скорость.");
        if (MaxStepsPerFrame <= 0)
            throw new ArgumentException("Предел шагов за кадр должен быть положительным.");
    }
}

/// <summary>
/// Переводит реальное время в игровые шаги: пауза, выбор скорости, ручной шаг.
/// Скорость меняет только частоту шагов; сами шаги одинаковы при любой скорости (docs/03).
/// Не зависит от Godot — отображение лишь передаёт сюда время кадра.
/// </summary>
public sealed class SimulationRunner
{
    private readonly GameSimulation _simulation;
    private double _pendingHours;
    private int _speedIndex;

    public SimulationRunner(GameSimulation simulation, SimulationSettings settings)
    {
        settings.Validate();
        _simulation = simulation;
        Settings = settings;
    }

    public SimulationSettings Settings { get; }
    public bool Paused { get; set; }

    public int SpeedIndex
    {
        get => _speedIndex;
        set => _speedIndex = Math.Clamp(value, 0, Settings.Speeds.Count - 1);
    }

    public int Speed => Settings.Speeds[_speedIndex];

    /// <summary>
    /// Доля текущего часа, прошедшая в реальном времени (0…1). Нужна только отображению,
    /// чтобы плавно двигать технику между шагами; на расчёты не влияет.
    /// </summary>
    public double HourProgress => Math.Clamp(_pendingHours, 0, 1);

    /// <summary>Учесть прошедшие реальные секунды и выполнить созревшие шаги. Возвращает число шагов.</summary>
    public int Advance(double realSeconds)
    {
        if (Paused || realSeconds <= 0)
            return 0;

        _pendingHours += realSeconds * Speed / Settings.SecondsPerHourAt1x;
        var steps = (int)Math.Min(Math.Floor(_pendingHours), Settings.MaxStepsPerFrame);
        for (var i = 0; i < steps; i++)
            _simulation.Step();
        _pendingHours -= steps;

        // Не успели — не копим долг, иначе после подвисания игра «догоняла» бы рывком.
        if (_pendingHours >= 1)
            _pendingHours = 0.999;
        return steps;
    }

    /// <summary>Ручной шаг на один час — для отладки, работает и на паузе.</summary>
    public void StepOnce()
    {
        _simulation.Step();
        _pendingHours = 0;
    }
}

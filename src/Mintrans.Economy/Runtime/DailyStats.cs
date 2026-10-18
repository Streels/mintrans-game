using Mintrans.Core.Ids;

namespace Mintrans.Economy.Runtime;

/// <summary>Что ограничило процесс в этот день.</summary>
public enum ProcessLimit
{
    /// <summary>Работал в полную силу.</summary>
    None,

    /// <summary>Не хватило входного ресурса.</summary>
    MissingInput,

    /// <summary>Некуда девать продукцию — склад выхода полон.</summary>
    OutputFull,
}

/// <summary>Итог процесса за сутки: сколько циклов из возможных и что помешало.</summary>
public sealed record ProcessResult(string ProcessId, long Cycles, long MaxCycles, ProcessLimit Limit, TypeId? Resource)
{
    /// <summary>Доля выполнения от 0 до 1 — для спроса это процент удовлетворения.</summary>
    public double Share => MaxCycles == 0 ? 1 : (double)Cycles / MaxCycles;
}

/// <summary>Учёт объекта за одни игровые сутки (docs/03: выпуск, доставка, потребление, дефицит, простой).</summary>
public sealed class DailyStats
{
    private readonly Dictionary<TypeId, Mass> _produced = new();
    private readonly Dictionary<TypeId, Mass> _consumed = new();
    private readonly Dictionary<TypeId, Mass> _received = new();
    private readonly Dictionary<TypeId, Mass> _shipped = new();
    private readonly List<ProcessResult> _processes = new();

    public DailyStats(long day) => Day = day;

    /// <summary>Номер суток с нуля.</summary>
    public long Day { get; }

    public IReadOnlyDictionary<TypeId, Mass> Produced => _produced;
    public IReadOnlyDictionary<TypeId, Mass> Consumed => _consumed;
    public IReadOnlyDictionary<TypeId, Mass> Received => _received;
    public IReadOnlyDictionary<TypeId, Mass> Shipped => _shipped;
    public IReadOnlyList<ProcessResult> Processes => _processes;

    internal void AddProduced(TypeId resource, Mass mass) => Add(_produced, resource, mass);
    internal void AddConsumed(TypeId resource, Mass mass) => Add(_consumed, resource, mass);
    internal void AddReceived(TypeId resource, Mass mass) => Add(_received, resource, mass);
    internal void AddShipped(TypeId resource, Mass mass) => Add(_shipped, resource, mass);
    internal void AddProcess(ProcessResult result) => _processes.Add(result);

    private static void Add(Dictionary<TypeId, Mass> totals, TypeId resource, Mass mass)
    {
        if (mass.IsZero)
            return;
        totals[resource] = totals.GetValueOrDefault(resource) + mass;
    }
}

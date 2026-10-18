using Mintrans.Core.Ids;
using Mintrans.Economy.Definitions;

namespace Mintrans.Economy.Runtime;

/// <summary>
/// Экономика одного объекта карты: склад, процессы и учёт по суткам.
/// Запас меняется только четырьмя операциями (docs/04): процесс производит и потребляет,
/// транспорт загружает (<see cref="Ship"/>) и выгружает (<see cref="Receive"/>).
/// </summary>
public sealed class EconomicSite
{
    private const int HistoryDays = 30;
    private readonly LinkedList<DailyStats> _history = new();

    internal EconomicSite(SiteId site, EconomicSiteDefinition definition, EconomyTotals totals, long day)
    {
        Site = site;
        Definition = definition;
        Totals = totals;
        Stock = new Stockpile(definition.Storage);
        Today = new DailyStats(day);
    }

    public SiteId Site { get; }
    public EconomicSiteDefinition Definition { get; }
    public Stockpile Stock { get; }
    private EconomyTotals Totals { get; }

    /// <summary>Учёт текущих (ещё не завершённых) суток.</summary>
    public DailyStats Today { get; private set; }

    /// <summary>Последние завершённые сутки или null, если их ещё не было.</summary>
    public DailyStats? Yesterday => _history.First?.Value;

    /// <summary>Завершённые сутки, от последних к ранним (не больше 30).</summary>
    public IEnumerable<DailyStats> History => _history;

    /// <summary>Нужен ли объекту этот ресурс: он входит в какой-то его процесс (сырьё завода, спрос города).</summary>
    public bool Needs(TypeId resource) =>
        Definition.Processes.Any(p => p.Inputs.Any(i => i.Resource == resource));

    /// <summary>Принять груз с транспорта. Возвращает, сколько поместилось на склад.</summary>
    public Mass Receive(TypeId resource, Mass mass)
    {
        var accepted = Stock.Add(resource, mass);
        Today.AddReceived(resource, accepted);
        return accepted;
    }

    /// <summary>Отдать груз транспорту. Возвращает, сколько было на складе.</summary>
    public Mass Ship(TypeId resource, Mass mass)
    {
        var taken = Stock.Remove(resource, mass);
        Today.AddShipped(resource, taken);
        return taken;
    }

    /// <summary>Начальный запас из сценария. Учитывается как поступление «извне», чтобы баланс сходился.</summary>
    internal void Seed(TypeId resource, Mass mass)
    {
        if (!Stock.Stores(resource))
            throw new ArgumentException($"У объекта нет склада для «{resource}».");
        if (mass > Stock.Free(resource))
            throw new ArgumentException($"Начальный запас «{resource}» {mass} больше склада {Stock.Capacity(resource)}.");
        Totals.AddSeeded(resource, Stock.Add(resource, mass));
    }

    /// <summary>Выполнить за сутки все процессы данной фазы, в порядке из данных.</summary>
    internal void RunProcesses(ProcessPhase phase)
    {
        foreach (var process in Definition.Processes.Where(p => p.Phase == phase))
            Today.AddProcess(Run(process));
    }

    private ProcessResult Run(ProcessDefinition process)
    {
        long cycles = process.CyclesPerDay;
        var limit = ProcessLimit.None;
        TypeId? limiting = null;

        foreach (var input in process.Inputs)
        {
            var possible = Stock.Amount(input.Resource).Portions(input.Amount);
            if (possible < cycles)
                (cycles, limit, limiting) = (possible, ProcessLimit.MissingInput, input.Resource);
        }
        foreach (var output in process.Outputs)
        {
            var possible = Stock.Free(output.Resource).Portions(output.Amount);
            if (possible < cycles)
                (cycles, limit, limiting) = (possible, ProcessLimit.OutputFull, output.Resource);
        }

        foreach (var input in process.Inputs)
        {
            var used = Stock.Remove(input.Resource, input.Amount * cycles);
            Today.AddConsumed(input.Resource, used);
            Totals.AddConsumed(input.Resource, used);
        }
        foreach (var output in process.Outputs)
        {
            var made = Stock.Add(output.Resource, output.Amount * cycles);
            Today.AddProduced(output.Resource, made);
            Totals.AddProduced(output.Resource, made);
        }
        return new ProcessResult(process.Id, cycles, process.CyclesPerDay, limit, limiting);
    }

    /// <summary>Закрыть сутки: текущий учёт уходит в историю, начинается новый.</summary>
    internal void CloseDay(long nextDay)
    {
        _history.AddFirst(Today);
        while (_history.Count > HistoryDays)
            _history.RemoveLast();
        Today = new DailyStats(nextDay);
    }
}

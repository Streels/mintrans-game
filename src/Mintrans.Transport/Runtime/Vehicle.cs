using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Stops;
using Mintrans.World.Facilities;

namespace Mintrans.Transport.Runtime;

public readonly record struct VehicleId(int Value)
{
    public override string ToString() => $"vehicle#{Value}";
}

/// <summary>Что техника делает сейчас.</summary>
public enum VehicleState
{
    /// <summary>Стоит в депо или гараже без задания.</summary>
    InDepot,

    /// <summary>Едет к цели текущего этапа.</summary>
    Travelling,

    /// <summary>Грузится в пункте А.</summary>
    Loading,

    /// <summary>Разгружается в пункте Б.</summary>
    Unloading,

    /// <summary>На обслуживании в депо или гараже — груз не возит.</summary>
    Servicing,

    /// <summary>Не может ехать: нет пути, пункт снесён и т. п. Каждый час пробует снова.</summary>
    Stuck,

    /// <summary>Разбит в крушении (docs/10а): обломки занимают путь, затем техника исчезает.</summary>
    Crashed,

    /// <summary>Остановлен игроком там, где был: стоит на месте, пока его не возобновят. Поезд продолжает занимать путь.</summary>
    Stopped,
}

/// <summary>Что делает поезд у красного сигнала (docs/10а, docs/12).</summary>
public enum RedSignalBehavior
{
    /// <summary>Ждать, пока сигнал откроется.</summary>
    Wait,

    /// <summary>Подождав, развернуться и искать другой путь.</summary>
    Reverse,
}

/// <summary>Куда техника направляется в цикле А–Б–А (docs/12).</summary>
public enum Leg
{
    ToA,
    ToB,
    ToDepot,
}

/// <summary>Одна единица в составе: локомотив, вагон или грузовик, с её грузом.</summary>
public sealed class VehicleUnit(VehicleModel model, TruckBody? body = null)
{
    public VehicleModel Model { get; } = model;

    /// <summary>Кузов грузовика или null.</summary>
    public TruckBody? Body { get; } = body;

    /// <summary>Какие грузы можно везти: по кузову грузовика или по модели вагона.</summary>
    public IReadOnlyList<TypeId> Carries => Body?.Cargo ?? Model.Cargo;

    public TypeId? CargoType { get; internal set; }
    public Mass Cargo { get; internal set; }
    public Mass Free => Model.Capacity - Cargo;
    public bool CanCarry(TypeId resource) => Carries.Contains(resource) && (CargoType is null || CargoType == resource);
}

/// <summary>
/// Поезд или грузовик. Положение — точка на сети (клетка и направление движения); поезд дополнительно занимает
/// «хвост» из клеток, по которым только что проехал, — по длине состава (docs/02, docs/10а: поезд не точка).
/// </summary>
public sealed class Vehicle
{
    private readonly List<VehicleUnit> _units = new();
    private readonly List<RoutePoint> _trail = new();

    internal Vehicle(VehicleId id, string name, NetworkTransport network, Facility depot)
    {
        Id = id;
        Name = name;
        Network = network;
        Depot = depot.Id;
        Head = new RoutePoint(depot.Area.Origin, null, null);
    }

    public VehicleId Id { get; }
    public string Name { get; }
    public NetworkTransport Network { get; }

    /// <summary>Депо или гараж приписки: здесь куплена техника и здесь её обслуживают.</summary>
    public FacilityId Depot { get; }

    public IReadOnlyList<VehicleUnit> Units => _units;
    public StopRef? A { get; internal set; }
    public StopRef? B { get; internal set; }
    public VehicleState State { get; internal set; } = VehicleState.InDepot;
    public Leg Leg { get; internal set; } = Leg.ToA;

    /// <summary>Голова состава: клетка, откуда въехала и куда собирается выехать.</summary>
    public RoutePoint Head { get; private set; }

    /// <summary>Клетка головы состава.</summary>
    public GridPos Position => Head.Cell;

    /// <summary>Точки, которые занимает состав за головой, от ближней к дальней.</summary>
    public IReadOnlyList<RoutePoint> TrailPoints => _trail;

    /// <summary>Клетки, которые занимает состав за головой, от ближней к дальней.</summary>
    public IEnumerable<GridPos> Trail => _trail.Select(p => p.Cell);

    /// <summary>Маршрут до цели этапа; голова стоит в точке <see cref="PathIndex"/>.</summary>
    public IReadOnlyList<RoutePoint> Path { get; private set; } = [];
    internal int PathIndex { get; private set; }
    internal int PathRevision { get; set; } = -1;
    /// <summary>Накопленная доля следующего шага (0…1) — для плавного показа движения.</summary>
    public double MoveBudget { get; internal set; }

    /// <summary>Следующая точка маршрута или null — для плавного показа между часами.</summary>
    public RoutePoint? NextPoint => PathIndex + 1 < Path.Count ? Path[PathIndex + 1] : null;

    /// <summary>Следующая клетка пути или null.</summary>
    public GridPos? NextCell => NextPoint?.Cell;

    /// <summary>До <paramref name="count"/> следующих точек маршрута после головы — для плавного показа движения между часами.</summary>
    public IReadOnlyList<RoutePoint> PointsAhead(int count) =>
        Path.Skip(PathIndex + 1).Take(Math.Max(0, count)).ToList();

    /// <summary>Поведение у красного сигнала (настройка поезда, docs/10а).</summary>
    public RedSignalBehavior RedSignal { get; set; } = RedSignalBehavior.Wait;

    /// <summary>Сколько часов подряд стоит у красного сигнала или занятого переезда.</summary>
    public int BlockedHours { get; internal set; }

    /// <summary>Поезд только что встал (депо, станция) — может уехать в любую сторону, развернув состав.</summary>
    internal bool CanTurn { get; set; } = true;

    /// <summary>
    /// Куда ехать после обслуживания в депо: продолжить маршрут к А или к Б, или остаться в депо (null) —
    /// по команде игрока «в депо».
    /// </summary>
    public Leg? AfterDepot { get; internal set; } = Leg.ToA;

    /// <summary>Что техника делала, когда игрок её остановил, — к этому она вернётся по команде «возобновить».</summary>
    internal VehicleState StoppedFrom { get; set; }

    /// <summary>
    /// Сколько часов осталось до цели текущего этапа при нынешней скорости, без учёта ожидания у сигналов и переездов
    /// (docs/12). Null, если техника не в пути.
    /// </summary>
    public double? HoursToArrival =>
        State == VehicleState.Travelling && Path.Count > 0 && Speed > 0
            ? Math.Max(0, Path.Count - 1 - PathIndex - MoveBudget) / Speed
            : null;

    /// <summary>Износ от 0 до 1 и выше (docs/11).</summary>
    public double Wear { get; internal set; }

    /// <summary>Сколько часов техника находится в текущем состоянии.</summary>
    public int HoursInState { get; internal set; }

    /// <summary>Номер в порядке прибытия к пункту: кто раньше приехал, того раньше обслуживают.</summary>
    public long QueueTicket { get; internal set; }

    /// <summary>В этот час ждала своей очереди: пункт уже обработал столько, сколько успевает.</summary>
    public bool Queued { get; internal set; }

    /// <summary>Единицы, которые везут груз: вагоны поезда или сам грузовик.</summary>
    public IReadOnlyList<VehicleUnit> CargoUnits => _units.Where(u => u.Model.Capacity > Mass.Zero).ToList();

    /// <summary>
    /// Какая доля состава помещается у платформы длиной <paramref name="stopLength"/> клеток (docs/10, docs/12):
    /// состав длиннее платформы обслуживается медленнее во столько же раз. У грузовика — всегда 1.
    /// </summary>
    public double PlatformFit(int stopLength)
    {
        var wagons = CargoUnits.Count;
        if (!Network.Trains || wagons == 0)
            return 1;
        return Math.Min(1.0, stopLength * Network.WagonsPerStopCell / (double)wagons);
    }

    /// <summary>Пояснение для игрока: почему стоит, чего ждёт.</summary>
    public string Reason { get; internal set; } = "";

    /// <summary>Скорость, клеток в час: у поезда — по локомотиву, у грузовика — своя.</summary>
    public double Speed => _units.Where(u => u.Model.Role != UnitRole.Wagon).Select(u => u.Model.Speed).DefaultIfEmpty(0).Min();

    /// <summary>Длина в клетках.</summary>
    public int LengthCells => Math.Max(1, (int)Math.Ceiling(_units.Sum(u => u.Model.Length) / (double)Network.UnitsPerCell));

    public Mass Cargo => _units.Aggregate(Mass.Zero, (sum, u) => sum + u.Cargo);
    public Mass Capacity => _units.Aggregate(Mass.Zero, (sum, u) => sum + u.Model.Capacity);

    public Mass CargoOf(TypeId resource) =>
        _units.Where(u => u.CargoType == resource).Aggregate(Mass.Zero, (sum, u) => sum + u.Cargo);

    internal void AddUnit(VehicleModel model, TruckBody? body = null) => _units.Add(new VehicleUnit(model, body));

    /// <summary>Сколько вагонов в составе.</summary>
    public int WagonCount => _units.Count(u => u.Model.Role == UnitRole.Wagon);

    internal void RemoveLastUnit() => _units.RemoveAt(_units.Count - 1);

    /// <summary>Задать маршрут, начинающийся в точке головы.</summary>
    internal void SetPath(IReadOnlyList<RoutePoint> path, int revision)
    {
        Path = path;
        PathIndex = 0;
        PathRevision = revision;
        if (path.Count > 0)
            Head = path[0];
    }

    internal void ClearPath()
    {
        Path = [];
        PathIndex = 0;
    }

    /// <summary>Сдвинуть голову на следующую точку маршрута; хвост тянется следом.</summary>
    internal void StepForward()
    {
        _trail.Insert(0, Path[PathIndex]);
        while (_trail.Count > LengthCells - 1)
            _trail.RemoveAt(_trail.Count - 1);
        PathIndex++;
        Head = Path[PathIndex];
        CanTurn = false;
    }

    /// <summary>
    /// Развернуть состав (docs/10а): головой становится последний вагон, все точки — в обратную сторону.
    /// Маршрут сбрасывается: его нужно искать заново от новой головы.
    /// </summary>
    internal void Flip()
    {
        var all = new List<RoutePoint>(_trail.Count + 1) { Head };
        all.AddRange(_trail);
        all.Reverse();
        Head = all[0].Reversed();
        _trail.Clear();
        _trail.AddRange(all.Skip(1).Select(p => p.Reversed()));
        ClearPath();
    }

    /// <summary>Поставить технику в клетку без хвоста (в депо или гараж).</summary>
    internal void PlaceAt(GridPos cell)
    {
        _trail.Clear();
        Head = new RoutePoint(cell, null, null);
        ClearPath();
        CanTurn = true;
    }

    internal void SetState(VehicleState state, string reason = "")
    {
        Queued = false;
        if (state != State)
            HoursInState = 0;
        State = state;
        Reason = reason;
    }
}

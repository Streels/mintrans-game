using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Economy;
using Mintrans.Transport.Definitions;
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
/// Поезд или грузовик. Положение — клетка на сети; поезд дополнительно занимает «хвост» из клеток,
/// по которым только что проехал, — по длине состава (docs/02: поезд не точка).
/// </summary>
public sealed class Vehicle
{
    private readonly List<VehicleUnit> _units = new();
    private readonly LinkedList<GridPos> _trail = new();

    internal Vehicle(VehicleId id, string name, NetworkTransport network, Facility depot)
    {
        Id = id;
        Name = name;
        Network = network;
        Depot = depot.Id;
        Position = depot.Area.Origin;
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

    /// <summary>Клетка головы состава.</summary>
    public GridPos Position { get; internal set; }

    /// <summary>Клетки, которые занимает состав за головой, от ближней к дальней.</summary>
    public IEnumerable<GridPos> Trail => _trail;

    /// <summary>Оставшийся путь до цели этапа, начиная с текущей клетки.</summary>
    public IReadOnlyList<GridPos> Path { get; internal set; } = [];
    internal int PathIndex { get; set; }
    internal int PathRevision { get; set; } = -1;
    /// <summary>Накопленная доля следующего шага (0…1) — для плавного показа движения.</summary>
    public double MoveBudget { get; internal set; }

    /// <summary>Следующая клетка пути или null — для плавного показа между часами.</summary>
    public GridPos? NextCell => PathIndex + 1 < Path.Count ? Path[PathIndex + 1] : null;

    /// <summary>Износ от 0 до 1 и выше (docs/11).</summary>
    public double Wear { get; internal set; }

    /// <summary>Сколько часов техника находится в текущем состоянии.</summary>
    public int HoursInState { get; internal set; }

    /// <summary>Номер в порядке прибытия к пункту: кто раньше приехал, того раньше обслуживают.</summary>
    public long QueueTicket { get; internal set; }

    /// <summary>В этот час ждала своей очереди: пункт уже обработал столько, сколько успевает.</summary>
    public bool Queued { get; internal set; }

    /// <summary>Какая часть состава сейчас стоит у платформы (с нуля), если состав длиннее платформы.</summary>
    public int PartIndex { get; internal set; }

    /// <summary>Сколько часов ещё длится перестановка к следующей части.</summary>
    public int ShuntHoursLeft { get; internal set; }

    /// <summary>Единицы, которые везут груз: вагоны поезда или сам грузовик.</summary>
    public IReadOnlyList<VehicleUnit> CargoUnits => _units.Where(u => u.Model.Capacity > Mass.Zero).ToList();

    /// <summary>На сколько частей делится состав у платформы длиной <paramref name="stopLength"/> клеток.</summary>
    public int Parts(int stopLength) => Network.Trains
        ? Math.Max(1, (int)Math.Ceiling(CargoUnits.Count / (double)Math.Max(1, stopLength * Network.WagonsPerStopCell)))
        : 1;

    /// <summary>Вагоны текущей части у платформы.</summary>
    public IReadOnlyList<VehicleUnit> PartUnits(int stopLength)
    {
        if (!Network.Trains)
            return CargoUnits;
        var perPart = Math.Max(1, stopLength * Network.WagonsPerStopCell);
        return CargoUnits.Skip(PartIndex * perPart).Take(perPart).ToList();
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

    /// <summary>Сдвинуть голову на следующую клетку пути; хвост тянется следом.</summary>
    internal void StepForward()
    {
        _trail.AddFirst(Position);
        while (_trail.Count > LengthCells - 1)
            _trail.RemoveLast();
        PathIndex++;
        Position = Path[PathIndex];
    }

    internal void ClearTrail() => _trail.Clear();

    internal void SetState(VehicleState state, string reason = "")
    {
        Queued = false;
        if (state != State)
            HoursInState = 0;
        State = state;
        Reason = reason;
    }
}

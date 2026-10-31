using Mintrans.Core.Ids;
using Mintrans.World.Facilities;
using Mintrans.World.Sites;

namespace Mintrans.World.Rail;

/// <summary>Как сигнал решает, пустить ли поезд (docs/10а, модель OpenTTD).</summary>
public enum SignalLogic
{
    /// <summary>Блочный: зелёный, если следующий участок до сигналов пуст.</summary>
    Block,

    /// <summary>Маршрутный: зелёный, если удалось закрепить путь поезда до следующего безопасного места.</summary>
    Path,
}

/// <summary>В какую сторону движения по пути сигнал регулирует поезда.</summary>
public enum SignalFacing
{
    /// <summary>Двусторонний: в обе стороны.</summary>
    Both,

    /// <summary>Односторонний по направлению пути — от его начала к концу.</summary>
    Forward,

    /// <summary>Односторонний против направления пути.</summary>
    Backward,
}

public readonly record struct RailSignalId(int Value) : IComparable<RailSignalId>
{
    public int CompareTo(RailSignalId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"сигнал {Value}";
}

/// <summary>
/// Сигнал игрока (docs/10в, раздел 5): точка на пути в метрах от его начала и сторона, в которую он регулирует движение.
/// Клетка нужна только для правила «не больше одного сигнала на клетку» и для выделения.
/// Сигнал не меняет геометрию и не соединяет пути.
/// </summary>
/// <param name="Kind">Вид сигнала из правил строительства.</param>
public sealed record RailSignal(RailSignalId Id, TrackEdgeId Edge, double DistanceM, TypeId Kind, SignalLogic Logic, SignalFacing Facing)
{
    public bool TwoWay => Facing == SignalFacing.Both;

    /// <summary>Регулирует ли сигнал поезд, идущий по пути в направлении <paramref name="travel"/>.</summary>
    public bool Controls(bool forward) => Facing switch
    {
        SignalFacing.Both => true,
        SignalFacing.Forward => forward,
        _ => !forward,
    };
}

/// <summary>Чем служит участок пути: платформа станции предприятия или стоянка депо.</summary>
public enum BerthKind
{
    Platform,
    Depot,
}

public readonly record struct RailBerthId(int Value) : IComparable<RailBerthId>
{
    public int CompareTo(RailBerthId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"стоянка {Value}";
}

/// <summary>
/// Участок прямого пути с особой ролью (docs/10в, раздел 4): платформа станции (по составу на путь) или стоянка депо.
/// Длина платформы — целое число клеток по 5 м, выбранных игроком. Концы стоянки — встроенные сигналы:
/// поезд въезжает и выезжает, только закрепив путь (docs/10а).
/// </summary>
/// <param name="Site">Предприятие, чьей станцией служит платформа; у депо — <see cref="SiteId.None"/>.</param>
/// <param name="Facility">Депо, которому принадлежит стоянка; у платформы — null.</param>
public sealed record RailBerth(RailBerthId Id, BerthKind Kind, TrackEdgeId Edge, double FromM, double ToM, SiteId Site, FacilityId? Facility)
{
    public double LengthM => ToM - FromM;

    public bool Covers(double distanceM, double epsilonM = 0) => distanceM >= FromM - epsilonM && distanceM <= ToM + epsilonM;
}

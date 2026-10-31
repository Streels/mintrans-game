using Mintrans.Core.Spatial;

namespace Mintrans.Core.Geometry;

/// <summary>Отрезок ломаной, приближающей путь: концы и их расстояния вдоль исходной геометрии.</summary>
public readonly record struct FlatSegment(WorldPoint A, WorldPoint B, double FromS, double ToS)
{
    public WorldBounds Bounds => WorldBounds.Of(A, B);
}

/// <summary>Участок пути по расстоянию от начала, м: [From, To].</summary>
public readonly record struct PathInterval(double From, double To)
{
    public bool Covers(double from, double to, double epsilonM) => from >= From - epsilonM && to <= To + epsilonM;
}

/// <summary>
/// Линейная геометрия из последовательно стыкованных примитивов (docs/10в): ось рельсового пути, дороги.
/// Стыки непрерывны по положению и направлению (G1): мгновенного излома нет, смена кривизны на стыке допустима.
/// </summary>
public sealed class PathGeometry
{
    /// <summary>Допуск совпадения направлений на стыке, рад.</summary>
    public const double HeadingEpsilonRad = 1e-6;

    /// <summary>Допуск ломаной, приближающей дуги при проверке клеток и коллизий, м.</summary>
    public const double FlattenToleranceM = 0.001;

    /// <summary>Наибольшая длина отрезка такой ломаной, м: от неё зависит шаг конфликтных зон.</summary>
    public const double FlattenSegmentM = 1.0;

    private readonly double[] _starts;
    private IReadOnlyList<FlatSegment>? _flat;

    public PathGeometry(IReadOnlyList<PathPrimitive> primitives)
    {
        if (primitives.Count == 0)
            throw new ArgumentException("Путь должен состоять хотя бы из одного примитива.", nameof(primitives));
        for (var i = 1; i < primitives.Count; i++)
        {
            var (prev, next) = (primitives[i - 1], primitives[i]);
            if (!prev.End.ApproxEquals(next.Start, SpatialScale.EpsilonM))
                throw new ArgumentException($"Разрыв пути между примитивами {i - 1} и {i}: {prev.End} и {next.Start}.");
            if (Angles.Difference(prev.EndHeading, next.StartHeading) > HeadingEpsilonRad)
                throw new ArgumentException($"Излом пути между примитивами {i - 1} и {i}: направления не совпадают.");
        }
        Primitives = primitives;
        _starts = new double[primitives.Count];
        var length = 0.0;
        for (var i = 0; i < primitives.Count; i++)
        {
            _starts[i] = length;
            length += primitives[i].Length;
        }
        Length = length;
    }

    public PathGeometry(params PathPrimitive[] primitives) : this((IReadOnlyList<PathPrimitive>)primitives) { }

    public IReadOnlyList<PathPrimitive> Primitives { get; }
    public double Length { get; }

    public WorldPoint Start => Primitives[0].Start;
    public WorldPoint End => Primitives[^1].End;
    public double StartHeading => Primitives[0].StartHeading;
    public double EndHeading => Primitives[^1].EndHeading;

    public WorldBounds Bounds => Primitives.Skip(1).Aggregate(Primitives[0].Bounds, (b, p) => b.Union(p.Bounds));

    /// <summary>Весь путь — один прямой отрезок.</summary>
    public bool IsStraight => Primitives.All(p => p is StraightPrimitive);

    /// <summary>Примитив, на который приходится расстояние <paramref name="s"/>, и расстояние внутри него.</summary>
    public (int Index, double Local) Locate(double s)
    {
        s = Math.Clamp(s, 0, Length);
        var index = Array.BinarySearch(_starts, s);
        if (index < 0)
            index = ~index - 1;
        index = Math.Clamp(index, 0, Primitives.Count - 1);
        return (index, Math.Min(s - _starts[index], Primitives[index].Length));
    }

    public double StartOf(int index) => _starts[index];

    public WorldPoint PointAt(double s)
    {
        var (i, local) = Locate(s);
        return Primitives[i].PointAt(local);
    }

    public double HeadingAt(double s)
    {
        var (i, local) = Locate(s);
        return Primitives[i].HeadingAt(local);
    }

    /// <summary>Разделить путь в точке <paramref name="s"/> (строго внутри) на два с общей точкой.</summary>
    public (PathGeometry Before, PathGeometry After) Split(double s)
    {
        if (s <= SpatialScale.EpsilonM || s >= Length - SpatialScale.EpsilonM)
            throw new ArgumentOutOfRangeException(nameof(s), $"Делить путь можно только внутри: {s} из {Length} м.");
        var (index, local) = Locate(s);
        var before = Primitives.Take(index).ToList();
        var after = new List<PathPrimitive>();
        var primitive = Primitives[index];
        if (local <= SpatialScale.EpsilonM)
            after.Add(primitive);
        else if (local >= primitive.Length - SpatialScale.EpsilonM)
            before.Add(primitive);
        else
        {
            var (a, b) = primitive.Split(local);
            before.Add(a);
            after.Add(b);
        }
        after.AddRange(Primitives.Skip(index + 1));
        return (new PathGeometry(before), new PathGeometry(after));
    }

    /// <summary>Тот же путь в обратную сторону.</summary>
    public PathGeometry Reversed() => new(Primitives.Reverse().Select(p => p.Reversed()).ToList());

    /// <summary>Ломаная с допуском <see cref="FlattenToleranceM"/> и шагом не больше <see cref="FlattenSegmentM"/>.</summary>
    public IReadOnlyList<FlatSegment> Flat => _flat ??= BuildFlat();

    private List<FlatSegment> BuildFlat()
    {
        var segments = new List<FlatSegment>();
        for (var i = 0; i < Primitives.Count; i++)
        {
            var primitive = Primitives[i];
            var points = primitive.Flatten(FlattenToleranceM, FlattenSegmentM).ToList();
            var start = _starts[i];
            for (var k = 1; k < points.Count; k++)
            {
                var from = start + primitive.Length * (k - 1) / (points.Count - 1);
                var to = start + primitive.Length * k / (points.Count - 1);
                segments.Add(new FlatSegment(points[k - 1], points[k], from, to));
            }
        }
        return segments;
    }

    /// <summary>Наименьшее расстояние от точки до оси пути.</summary>
    public double DistanceTo(WorldPoint p) => Flat.Min(seg => Distances.PointSegment(p, seg.A, seg.B));
}

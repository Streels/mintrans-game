using Mintrans.Core.Spatial;

namespace Mintrans.Core.Geometry;

/// <summary>
/// Примитив линейной геометрии в метрах (docs/10в, раздел 1): прямой отрезок или дуга окружности.
/// У каждого известны начало, конец, касательные и точная длина. Переходных кривых в v1 нет.
/// </summary>
public abstract record PathPrimitive
{
    public abstract WorldPoint Start { get; }
    public abstract WorldPoint End { get; }
    public abstract double StartHeading { get; }
    public abstract double EndHeading { get; }
    public abstract double Length { get; }
    public abstract WorldBounds Bounds { get; }

    /// <summary>Точка на расстоянии <paramref name="s"/> метров от начала (0 ≤ s ≤ Length).</summary>
    public abstract WorldPoint PointAt(double s);

    /// <summary>Направление движения в точке <paramref name="s"/>.</summary>
    public abstract double HeadingAt(double s);

    /// <summary>Разделить в точке <paramref name="s"/> на две части с общей точкой и касательной.</summary>
    public abstract (PathPrimitive Before, PathPrimitive After) Split(double s);

    /// <summary>Тот же примитив, пройденный от конца к началу.</summary>
    public abstract PathPrimitive Reversed();

    /// <summary>
    /// Точки ломаной, приближающей примитив: хорда отходит от него не больше чем на <paramref name="toleranceM"/>,
    /// отрезок не длиннее <paramref name="maxSegmentM"/>. Первая точка — начало, последняя — конец.
    /// </summary>
    public abstract IEnumerable<WorldPoint> Flatten(double toleranceM, double maxSegmentM);
}

/// <summary>Прямой отрезок.</summary>
public sealed record StraightPrimitive(WorldPoint From, WorldPoint To) : PathPrimitive
{
    public override WorldPoint Start => From;
    public override WorldPoint End => To;
    public double Heading => (To - From).Angle;
    public override double StartHeading => Heading;
    public override double EndHeading => Heading;
    public override double Length => From.DistanceTo(To);
    public override WorldBounds Bounds => WorldBounds.Of(From, To);

    public override WorldPoint PointAt(double s) => Length > 0 ? WorldPoint.Lerp(From, To, s / Length) : From;

    public override double HeadingAt(double s) => Heading;

    public override (PathPrimitive Before, PathPrimitive After) Split(double s)
    {
        var at = PointAt(s);
        return (new StraightPrimitive(From, at), new StraightPrimitive(at, To));
    }

    public override PathPrimitive Reversed() => new StraightPrimitive(To, From);

    public override IEnumerable<WorldPoint> Flatten(double toleranceM, double maxSegmentM)
    {
        var count = Math.Max(1, (int)Math.Ceiling(Length / maxSegmentM));
        for (var i = 0; i <= count; i++)
            yield return i == count ? To : WorldPoint.Lerp(From, To, (double)i / count);
    }
}

/// <summary>
/// Дуга окружности: центр, радиус, угол начальной точки относительно центра и угол поворота со знаком.
/// <paramref name="Sweep"/> &gt; 0 — поворот к +Y (по часовой стрелке на карте с севером вверху), &lt; 0 — обратно.
/// </summary>
public sealed record ArcPrimitive(WorldPoint Center, double Radius, double StartAngle, double Sweep) : PathPrimitive
{
    /// <summary>+1 — поворот по часовой стрелке на карте (к +Y), −1 — против.</summary>
    public int TurnSign => Sweep >= 0 ? 1 : -1;

    public double EndAngle => StartAngle + Sweep;

    public override WorldPoint Start => Center + WorldPoint.FromAngle(StartAngle) * Radius;
    public override WorldPoint End => Center + WorldPoint.FromAngle(EndAngle) * Radius;
    public override double StartHeading => Angles.Normalize2Pi(StartAngle + TurnSign * Math.PI / 2);
    public override double EndHeading => Angles.Normalize2Pi(EndAngle + TurnSign * Math.PI / 2);
    public override double Length => Radius * Math.Abs(Sweep);

    public override WorldBounds Bounds
    {
        get
        {
            var bounds = WorldBounds.Of(Start, End);
            // Крайние точки окружности по осям — если их угол попадает в дугу.
            for (var k = 0; k < 4; k++)
            {
                var axis = k * Math.PI / 2;
                if (ContainsAngle(axis))
                    bounds = bounds.Include(Center + WorldPoint.FromAngle(axis) * Radius);
            }
            return bounds;
        }
    }

    /// <summary>Лежит ли направление из центра <paramref name="angle"/> внутри дуги.</summary>
    public bool ContainsAngle(double angle)
    {
        var offset = Angles.Normalize2Pi(TurnSign * (angle - StartAngle));
        return offset <= Math.Abs(Sweep) + 1e-12;
    }

    private double AngleAt(double s) => Length > 0 ? StartAngle + Sweep * (s / Length) : StartAngle;

    public override WorldPoint PointAt(double s) => Center + WorldPoint.FromAngle(AngleAt(s)) * Radius;

    public override double HeadingAt(double s) => Angles.Normalize2Pi(AngleAt(s) + TurnSign * Math.PI / 2);

    public override (PathPrimitive Before, PathPrimitive After) Split(double s)
    {
        var t = Length > 0 ? s / Length : 0;
        return (this with { Sweep = Sweep * t }, this with { StartAngle = StartAngle + Sweep * t, Sweep = Sweep * (1 - t) });
    }

    public override PathPrimitive Reversed() => this with { StartAngle = EndAngle, Sweep = -Sweep };

    public override IEnumerable<WorldPoint> Flatten(double toleranceM, double maxSegmentM)
    {
        // Стрелка прогиба хорды R(1 − cos(φ/2)) не больше допуска; длина хорды не больше maxSegmentM.
        var bySagitta = toleranceM < Radius ? 2 * Math.Acos(1 - toleranceM / Radius) : Math.PI / 2;
        var byLength = 2 * Math.Asin(Math.Min(1, maxSegmentM / (2 * Radius)));
        var step = Math.Min(bySagitta, byLength);
        var count = Math.Max(1, (int)Math.Ceiling(Math.Abs(Sweep) / step));
        for (var i = 0; i <= count; i++)
            yield return i == count ? End : Center + WorldPoint.FromAngle(StartAngle + Sweep * i / count) * Radius;
    }
}

namespace Mintrans.Core.Spatial;

/// <summary>
/// Точка мира в метрах (docs/02в). Оси те же, что у клеток <see cref="Grid.GridPos"/>: X растёт на восток, Y — на юг.
/// Это исходная геометрия: её не округляют ни при сохранении, ни при построении графа.
/// Тот же тип служит двумерным вектором (разность точек, направление).
/// </summary>
public readonly record struct WorldPoint(double X, double Y)
{
    public static readonly WorldPoint Zero = new(0, 0);

    public static WorldPoint operator +(WorldPoint a, WorldPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static WorldPoint operator -(WorldPoint a, WorldPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static WorldPoint operator -(WorldPoint a) => new(-a.X, -a.Y);
    public static WorldPoint operator *(WorldPoint a, double k) => new(a.X * k, a.Y * k);
    public static WorldPoint operator *(double k, WorldPoint a) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    /// <summary>Угол вектора: 0 — восток (+X), π/2 — юг (+Y).</summary>
    public double Angle => Math.Atan2(Y, X);

    public double Dot(WorldPoint other) => X * other.X + Y * other.Y;

    /// <summary>Псевдоскалярное произведение: положительно, если <paramref name="other"/> повёрнут от этого вектора к +Y.</summary>
    public double Cross(WorldPoint other) => X * other.Y - Y * other.X;

    public double DistanceTo(WorldPoint other) => (other - this).Length;

    public WorldPoint Normalized()
    {
        var length = Length;
        return length > 0 ? new WorldPoint(X / length, Y / length) : Zero;
    }

    /// <summary>Поворот на четверть оборота: <paramref name="sign"/> = +1 — от +X к +Y, −1 — обратно.</summary>
    public WorldPoint Perpendicular(int sign) => sign >= 0 ? new WorldPoint(-Y, X) : new WorldPoint(Y, -X);

    /// <summary>Единичный вектор направления <paramref name="angleRad"/>.</summary>
    public static WorldPoint FromAngle(double angleRad) => new(Math.Cos(angleRad), Math.Sin(angleRad));

    public static WorldPoint Lerp(WorldPoint a, WorldPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>Совпадают ли точки с допуском <paramref name="epsilonM"/> метров.</summary>
    public bool ApproxEquals(WorldPoint other, double epsilonM) => DistanceTo(other) <= epsilonM;

    public override string ToString() => $"({X:0.###} м, {Y:0.###} м)";
}

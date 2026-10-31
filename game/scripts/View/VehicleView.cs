using Godot;
using Mintrans.Core.Grid;
using Mintrans.Simulation.Time;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using Mintrans.World;

namespace Mintrans.Game.View;

/// <summary>
/// Техника на карте. Поезд — вагоны из набора изометрических спрайтов (docs/18), выставленные вдоль рельсов
/// по длине каждой единицы состава; без набора или по клавише V — толстая линия по занятым клеткам.
/// Грузовик — маленький ромб. Между игровыми часами голова плавно сдвигается к следующей клетке; на расчёты это не влияет.
/// Цвет (линии или огонька над головой поезда) показывает состояние: едет, грузится, стоит; разбитый состав темнеет.
/// На мосту техника рисуется на высоте настила.
/// </summary>
public partial class VehicleView : Node2D
{
    private static readonly Color Outline = new(0.1f, 0.1f, 0.12f);

    private WorldMap _map = null!;
    private IsoProjection _projection = null!;
    private TransportState _transport = null!;
    private SimulationRunner _runner = null!;
    private MapStyle _style = null!;
    private TrainSprites? _sprites;
    private bool _useSprites = true;

    /// <param name="sprites">Набор спрайтов вагонов или null — тогда поезда рисуются линией.</param>
    public void Init(WorldMap map, IsoProjection projection, TransportState transport, SimulationRunner runner, MapStyle style,
        TrainSprites? sprites)
    {
        _map = map;
        _projection = projection;
        _transport = transport;
        _runner = runner;
        _style = style;
        _sprites = sprites;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Keycode: Key.V, Pressed: true, Echo: false })
        {
            _useSprites = !_useSprites;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        var cars = new List<CarSprite>();
        var lamps = new List<(Vector2 At, Color Color)>();
        foreach (var vehicle in _transport.Vehicles)
        {
            if (vehicle.State is VehicleState.InDepot or VehicleState.Servicing)
                continue;
            var color = StateColor(vehicle);
            if (vehicle.Network.Trains && _useSprites && _sprites is { } sprites)
            {
                var placed = PlaceCars(vehicle, sprites);
                cars.AddRange(placed);
                if (placed.Count > 0)
                    lamps.Add((placed[0].At, color));
            }
            else if (vehicle.Network.Trains)
                DrawTrainLine(vehicle, color);
            else
            {
                var head = Project(Path(vehicle).Front);
                Vector2[] diamond = [head + new Vector2(0, -5), head + new Vector2(7, 0), head + new Vector2(0, 5), head + new Vector2(-7, 0)];
                DrawColoredPolygon(diamond, color);
                DrawPolyline([.. diamond, diamond[0]], Outline, 1.5f);
            }
        }

        // Вагоны всех поездов — по экранной глубине: дальний рисуется раньше.
        foreach (var car in cars.OrderBy(c => c.At.Y))
        {
            var size = car.Texture.GetSize() * car.Scale;
            DrawTextureRect(car.Texture, new Rect2(car.At - _sprites!.Pivot * car.Scale, size), false, car.Modulate);
        }
        // Огонёк состояния над головным вагоном — одного экранного размера при любом масштабе карты.
        var zoom = Math.Max(0.1f, GetCanvasTransform().Scale.X);
        foreach (var (at, color) in lamps)
        {
            var lamp = at + new Vector2(0, -(2f + 7f / zoom));
            DrawCircle(lamp, 3.2f / zoom, Outline);
            DrawCircle(lamp, 2.2f / zoom, color);
        }
    }

    private Color StateColor(Vehicle vehicle) => vehicle.State switch
    {
        VehicleState.Travelling when vehicle.BlockedHours > 0 => new Color(1f, 0.3f, 0.25f),
        VehicleState.Travelling => vehicle.Cargo.IsZero ? new Color(0.85f, 0.85f, 0.9f) : new Color(1f, 0.82f, 0.25f),
        VehicleState.Loading or VehicleState.Unloading => new Color(0.4f, 0.85f, 1f),
        VehicleState.Crashed => _style.Rail.Wreck,
        _ => new Color(1f, 0.3f, 0.25f),
    };

    private void DrawTrainLine(Vehicle vehicle, Color color)
    {
        var path = Path(vehicle);
        var points = path.Points.Take(path.HeadIndex + 1).Select(Project).Append(Project(path.Front)).Reverse().ToArray();
        DrawPolyline(points, Outline, 7f);
        DrawPolyline(points, color, 4f);
        DrawCircle(points[0], 4.5f, new Color(0.15f, 0.15f, 0.18f));
    }

    // ---------- Вагоны ----------

    private readonly record struct CarSprite(Vector2 At, Texture2D Texture, float Scale, Color Modulate);

    /// <summary>
    /// Вагоны состава от головы к хвосту. Вагон набора длиной <see cref="MapStyle.TrainCarCells"/> клетки покрывает
    /// столько условных единиц длины состава, сколько их помещается в эту длину, — так нарисованный состав
    /// занимает столько же клеток, сколько в расчёте занятости пути. Первый вагон — головной, дальше прицепные
    /// и моторные через один; у состава из трёх и более вагонов последний — головной, развёрнутый назад, как у электропоезда.
    /// </summary>
    private List<CarSprite> PlaceCars(Vehicle vehicle, TrainSprites sprites)
    {
        var carCells = _style.TrainCarCells;
        var units = vehicle.Units.Sum(u => Math.Max(1, u.Model.Length));
        var count = Math.Max(1, (int)Math.Ceiling(units / (carCells * Math.Max(1, vehicle.Network.UnitsPerCell)) - 1e-6));
        var parts = new List<(string Part, bool Reversed)>(count);
        for (var i = 0; i < count; i++)
        {
            var part = i == 0 ? "head" : i % 2 == 1 ? "trailer" : "motor";
            parts.Add((sprites.HasPart(part) ? part : "head", false));
        }
        if (parts.Count >= 3 && sprites.HasPart("head"))
            parts[^1] = ("head", true);

        var scale = (float)(_projection.TileWidth / 2f / (sprites.PitchMetres * Math.Sqrt(0.5) * sprites.PixelsPerMetre) * carCells);
        var path = Path(vehicle);
        var modulate = vehicle.State == VehicleState.Crashed ? new Color(0.35f, 0.3f, 0.28f) : Colors.White;

        var result = new List<CarSprite>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var center = path.FrontDistance - (i + 0.5f) * carCells;
            var front = path.Sample(center + carCells / 2);
            var back = path.Sample(center - carCells / 2);
            var direction = new Vector2(front.X - back.X, front.Y - back.Y);
            if (direction.LengthSquared() < 1e-6f)
                direction = path.Heading;
            if (parts[i].Reversed)
                direction = -direction;
            result.Add(new CarSprite(Project(path.Sample(center)), sprites.Frame(parts[i].Part, direction), scale, modulate));
        }
        return result;
    }

    // ---------- Путь состава ----------

    /// <summary>
    /// Ломаная, по которой стоит состав, в координатах сетки (x, y) с высотой (z), — ровно по нарисованным рельсам:
    /// через середины общих сторон клеток и середины кусков пути (у поворота это не центр клетки). От въезда в клетку
    /// хвоста до головы и дальше на несколько клеток маршрута вперёд — чтобы голова двигалась между часами без остановок.
    /// Расстояния — длина ломаной в клетках сетки.
    /// </summary>
    private sealed class TrainPath(List<Vector3> points, int headIndex, float frontDistance, Vector2 heading)
    {
        public List<Vector3> Points { get; } = points;
        public int HeadIndex { get; } = headIndex;
        public float FrontDistance { get; } = frontDistance;
        public Vector2 Heading { get; } = heading;
        public Vector3 Front => Sample(FrontDistance);

        /// <summary>Точка на расстоянии от начала ломаной; до начала — продолжение первого отрезка назад.</summary>
        public Vector3 Sample(float distance)
        {
            if (Points.Count == 1)
                return Points[0];
            if (distance <= 0)
            {
                var back = Points[0] - Points[1];
                var length = Flat(back);
                return length > 0 ? Points[0] + back / length * -distance : Points[0];
            }
            var left = distance;
            for (var i = 1; i < Points.Count; i++)
            {
                var length = Flat(Points[i] - Points[i - 1]);
                if (length > 0 && left <= length)
                    return Points[i - 1].Lerp(Points[i], left / length);
                left -= length;
            }
            return Points[^1];
        }

        public static float Flat(Vector3 v) => new Vector2(v.X, v.Y).Length();
    }

    private TrainPath Path(Vehicle vehicle)
    {
        // Голова движется по клеткам маршрута: за час проходит «скорость» клеток. Чтобы показ был непрерывным,
        // впереди нужны клетки, которые она пройдёт до конца часа; дальше разворота — нет, там состав меняет голову.
        var moving = vehicle.State == VehicleState.Travelling && vehicle.BlockedHours == 0;
        IReadOnlyList<RoutePoint> ahead = moving ? vehicle.PointsAhead((int)Math.Ceiling(vehicle.Speed) + 2) : [];
        var turn = ahead.ToList().FindIndex(p => p.IsReversal);
        if (turn >= 0)
            ahead = ahead.Take(turn + 1).ToList();

        var cells = vehicle.TrailPoints.Reverse().Append(vehicle.Head).Concat(ahead).ToList();
        var headCell = vehicle.TrailPoints.Count;
        var points = new List<Vector3> { cells[0].Entry is { } entry && entry != cells[0].Exit ? Edge(cells[0], entry) : PieceCenter(cells[0]) };
        var centers = new List<int>(cells.Count);
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                var side = cells[i - 1].Exit ?? Directions.Between(cells[i - 1].Cell, cells[i].Cell);
                if (side is { } s && Directions.Between(cells[i - 1].Cell, cells[i].Cell) == s)
                    points.Add((Edge(cells[i - 1], s) + Edge(cells[i], s.Opposite())) / 2);
            }
            points.Add(PieceCenter(cells[i]));
            centers.Add(points.Count - 1);
        }

        // Голова — в середине своего куска пути плюс пройденная за этот час доля пути до следующих клеток.
        var progress = moving ? (float)(vehicle.MoveBudget + _runner.HourProgress * vehicle.Speed) : 0f;
        progress = Math.Clamp(progress, 0f, cells.Count - 1 - headCell);
        var step = Math.Min((int)progress, cells.Count - 2 - headCell);
        var fromIndex = centers[headCell + Math.Max(0, step)];
        var front = Length(points, centers[headCell + Math.Max(0, step)]);
        if (step >= 0 && headCell + step + 1 < cells.Count)
        {
            var toIndex = centers[headCell + step + 1];
            front += (progress - step) * (Length(points, toIndex) - Length(points, fromIndex));
        }

        var headIndex = centers[headCell];
        var heading = headIndex > 0 ? Flat(points[headIndex] - points[headIndex - 1]) : Vector2.Zero;
        if (heading.LengthSquared() < 1e-6f)
            heading = vehicle.Head.Exit is { } x ? Grid(x) : vehicle.Head.Entry is { } e ? -Grid(e) : new Vector2(0, 1);
        return new TrainPath(points, headIndex, front, heading);
    }

    /// <summary>Длина ломаной от начала до точки с номером <paramref name="index"/>.</summary>
    private static float Length(List<Vector3> points, int index)
    {
        var length = 0f;
        for (var i = 1; i <= index && i < points.Count; i++)
            length += TrainPath.Flat(points[i] - points[i - 1]);
        return length;
    }

    /// <summary>
    /// Середина куска пути, по которому едут через клетку: между серединами сторон въезда и выезда.
    /// У прямого куска это центр клетки, у поворота — точка на диагонали, где и нарисован рельс.
    /// </summary>
    private Vector3 PieceCenter(RoutePoint point) =>
        point.Entry is { } entry && point.Exit is { } exit && entry != exit
            ? (Edge(point, entry) + Edge(point, exit)) / 2
            : Center(point);

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    private static Vector2 Grid(Direction side)
    {
        var (dx, dy) = side.Offset();
        return new Vector2(dx, dy);
    }

    private float Height(RoutePoint point) => Bridges.HeightAt(_map, point.Cell, point.Elevated, _style.Rail.LiftLevels);

    private Vector3 Center(RoutePoint point) => new(point.Cell.X + 0.5f, point.Cell.Y + 0.5f, Height(point));

    /// <summary>Середина стороны клетки на высоте самой клетки.</summary>
    private Vector3 Edge(RoutePoint point, Direction side)
    {
        var (dx, dy) = side.Offset();
        return new Vector3(point.Cell.X + 0.5f + dx * 0.5f, point.Cell.Y + 0.5f + dy * 0.5f, Height(point));
    }

    private Vector2 Project(Vector3 point) => _projection.GridPoint(point.X, point.Y, point.Z);
}

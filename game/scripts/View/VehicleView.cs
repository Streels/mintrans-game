using Godot;
using Mintrans.Core.Grid;
using Mintrans.Simulation.Time;
using Mintrans.Transport.Definitions;
using Mintrans.Transport.Routing;
using Mintrans.Transport.Runtime;
using Mintrans.Core.Spatial;
using Mintrans.Transport.Rail;
using Mintrans.World;
using Mintrans.World.Rail;

namespace Mintrans.Game.View;

/// <summary>
/// Техника на карте. Поезд — вагоны из набора изометрических спрайтов (docs/18), выставленные вдоль рельсов в метрах
/// (docs/10в, раздел 6): положение каждого вагона — по непрерывной дистанции вдоль путей, ракурс — ближайший из 16
/// к хорде между его концами. Без набора или по клавише V — толстая линия по путям под составом. Грузовик — маленький ромб.
/// Между игровыми часами голова плавно продвигается по маршруту; на расчёты это не влияет.
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
        if (TrackOf(vehicle) is not { } track)
            return;
        var points = new List<Vector2>();
        for (var d = track.Front; d >= track.Front - track.Length - 1e-6; d -= RailLines.StepM)
            points.Add(Project(track.At(d)));
        points.Add(Project(track.At(track.Front - track.Length)));
        if (points.Count < 2)
            return;
        var zoom = Math.Max(0.05f, GetCanvasTransform().Scale.X);
        DrawPolyline(points.ToArray(), Outline, Math.Max(7f, 3f / zoom));
        DrawPolyline(points.ToArray(), color, Math.Max(4f, 2f / zoom));
        DrawCircle(points[0], 4.5f, new Color(0.15f, 0.15f, 0.18f));
    }

    // ---------- Вагоны ----------

    private readonly record struct CarSprite(Vector2 At, Texture2D Texture, float Scale, Color Modulate);

    /// <summary>
    /// Вагоны состава от головы к хвосту. Вагон набора длиной <see cref="MapStyle.TrainCarCells"/> клетки покрывает
    /// столько условных единиц длины состава, сколько их помещается в эту длину, — так нарисованный состав
    /// занимает столько же метров, сколько в расчёте занятости пути. Первый вагон — головной, дальше прицепные
    /// и моторные через один; у состава из трёх и более вагонов последний — головной, развёрнутый назад, как у электропоезда.
    /// </summary>
    private List<CarSprite> PlaceCars(Vehicle vehicle, TrainSprites sprites)
    {
        if (TrackOf(vehicle) is not { } track)
            return [];
        var carCells = _style.TrainCarCells;
        var carM = carCells * SpatialScale.CellM;
        var units = vehicle.Units.Sum(u => Math.Max(1, u.Model.Length));
        var count = Math.Max(1, (int)Math.Ceiling(units / (carCells * Math.Max(1, vehicle.Network.UnitsPerCell)) - 1e-6));
        // Состав, ещё выходящий из депо, показываем только той частью, что уже на путях.
        count = Math.Max(1, Math.Min(count, (int)Math.Ceiling(track.Length / carM - 1e-6)));
        var parts = new List<(string Part, bool Reversed)>(count);
        for (var i = 0; i < count; i++)
        {
            var part = i == 0 ? "head" : i % 2 == 1 ? "trailer" : "motor";
            parts.Add((sprites.HasPart(part) ? part : "head", false));
        }
        if (parts.Count >= 3 && sprites.HasPart("head"))
            parts[^1] = ("head", true);

        var scale = (float)(_projection.TileWidth / 2f / (sprites.PitchMetres * Math.Sqrt(0.5) * sprites.PixelsPerMetre) * carCells);
        var modulate = vehicle.State == VehicleState.Crashed ? new Color(0.35f, 0.3f, 0.28f) : Colors.White;

        var result = new List<CarSprite>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var center = track.Front - (i + 0.5) * carM;
            var front = track.At(center + carM / 2);
            var back = track.At(center - carM / 2);
            // Ракурс по хорде между сцепками, в координатах сетки (x — на восток, y — на юг).
            var direction = new Vector2((float)(front.X - back.X), (float)(front.Y - back.Y));
            if (direction.LengthSquared() < 1e-6f)
                direction = new Vector2(1, 0);
            if (parts[i].Reversed)
                direction = -direction;
            result.Add(new CarSprite(Project(track.At(center)), sprites.Frame(parts[i].Part, direction), scale, modulate));
        }
        return result;
    }

    // ---------- Положение поезда на путях ----------

    private readonly record struct TrackPointH(double X, double Y, float Height);

    /// <summary>
    /// Пути под составом и маршрут впереди как одна линия по дистанции: 0 — голова по расчёту, отрицательные — к хвосту,
    /// положительные — вперёд по маршруту. <see cref="Front"/> — где голова сейчас на экране (с долей часа),
    /// <see cref="Length"/> — сколько метров состава на путях.
    /// </summary>
    private sealed class RailTrack(List<(DirectedEdge Edge, double From, double To, float? Deck)> pieces, double origin, double front, double length,
        RailNetwork rails, WorldMap map)
    {
        public double Front { get; } = front;
        public double Length { get; } = length;

        public TrackPointH At(double d)
        {
            // Пройти по кусочкам от хвоста: origin — дистанция начала первого кусочка относительно головы.
            var left = d - origin;
            foreach (var (edge, from, to, deck) in pieces)
            {
                var span = to - from;
                if (left <= span)
                {
                    var along = from + Math.Clamp(left, 0, span);
                    var position = new TrackPosition(edge, along);
                    var (at, _) = rails.Locate(position);
                    var geometry = rails.Edge(edge.Edge).Geometry;
                    return new TrackPointH(at.X, at.Y, RailLines.Height(map, geometry, rails.EdgeDistance(position), deck));
                }
                left -= span;
            }
            var last = pieces[^1];
            var end = new TrackPosition(last.Edge, last.To);
            var (point, _) = rails.Locate(end);
            return new TrackPointH(point.X, point.Y, RailLines.Height(map, rails.Edge(last.Edge.Edge).Geometry, rails.EdgeDistance(end), last.Deck));
        }
    }

    private RailTrack? TrackOf(Vehicle vehicle)
    {
        if (vehicle.Body is not { } body || _map.Rails is not { } rails)
            return null;
        var lift = _style.Rail.LiftLevels;
        float? Deck(DirectedEdge edge) => RailLines.DeckOf(_map, rails, rails.Edge(edge.Edge), lift);

        // Хвост → голова: пути состава в обратном порядке, на каждом — от точки хвоста до конца по ходу движения.
        var pieces = new List<(DirectedEdge, double, double, float?)>();
        var behind = new List<(DirectedEdge, double, double, float?)>();
        var left = body.ExtentM;
        for (var i = 0; i < body.Edges.Count && left > 1e-6; i++)
        {
            var edge = body.Edges[i];
            var top = i == 0 ? body.HeadM : rails.LengthOf(edge);
            var from = Math.Max(0, top - left);
            behind.Add((edge, from, top, Deck(edge)));
            left -= top - from;
        }
        behind.Reverse();
        pieces.AddRange(behind);
        if (pieces.Count == 0)
            pieces.Add((body.HeadEdge, body.HeadM, body.HeadM, Deck(body.HeadEdge)));
        var onTrack = pieces.Sum(p => p.Item3 - p.Item2);

        // Вперёд по маршруту — столько, сколько голова пройдёт до конца часа; дальше разворота — нет.
        var moving = vehicle.State == VehicleState.Travelling && vehicle.BlockedHours == 0;
        var ahead = 0.0;
        if (moving && vehicle.RailRoute is { } route)
        {
            var budget = vehicle.SpeedMph + 1;
            var from = body.HeadM;
            for (var i = vehicle.RailRouteIndex; i < route.Edges.Count && ahead < budget; i++)
            {
                var edge = route.Edges[i];
                var end = i == route.Edges.Count - 1 ? route.TargetM : rails.LengthOf(edge);
                var to = Math.Min(end, from + budget - ahead);
                if (to > from)
                    pieces.Add((edge, from, to, Deck(edge)));
                ahead += Math.Max(0, to - from);
                if (route.ReversesAfter(i))
                    break;
                from = 0;
            }
        }
        var progress = moving ? Math.Min(ahead, _runner.HourProgress * vehicle.SpeedMph) : 0;
        return new RailTrack(pieces.Select(p => (p.Item1, p.Item2, p.Item3, p.Item4)).ToList(), -onTrack, progress, onTrack, rails, _map);
    }

    private Vector2 Project(TrackPointH point) =>
        _projection.World(new WorldPoint(point.X, point.Y), point.Height);

    // ---------- Путь грузовика по клеткам ----------

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

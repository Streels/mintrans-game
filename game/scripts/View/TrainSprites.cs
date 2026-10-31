using Godot;
using Mintrans.Data.Json;

namespace Mintrans.Game.View;

/// <summary>
/// Набор изометрических спрайтов вагонов (docs/18): паспорт <c>*.json</c> художественного ассета — восемь ракурсов
/// каждого вида вагона, точка привязки, масштаб в пикселях на метр и шаг сцепления. Первый набор — ЭД4М
/// (<c>game/assets/vehicles/ed4m/ed4m.json</c>). Только оформление: на правила игры не влияет.
/// </summary>
public sealed class TrainSprites
{
    /// <summary>Направления ракурсов в координатах сетки (x — на восток, y — на юг) и их имена в паспорте.</summary>
    private static readonly (string Name, Vector2 Delta)[] Headings =
    [
        ("s", new Vector2(0, 1)), ("sw", new Vector2(-1, 1)), ("w", new Vector2(-1, 0)), ("nw", new Vector2(-1, -1)),
        ("n", new Vector2(0, -1)), ("ne", new Vector2(1, -1)), ("e", new Vector2(1, 0)), ("se", new Vector2(1, 1)),
    ];

    private readonly Dictionary<(string Part, string Heading), Texture2D> _frames;

    private TrainSprites(Dictionary<(string, string), Texture2D> frames, Vector2 pivot, double pixelsPerMetre, double pitchMetres)
    {
        _frames = frames;
        Pivot = pivot;
        PixelsPerMetre = pixelsPerMetre;
        PitchMetres = pitchMetres;
    }

    /// <summary>Проекция центра вагона на плоскость рельсов в пикселях кадра.</summary>
    public Vector2 Pivot { get; }

    public double PixelsPerMetre { get; }

    /// <summary>Расстояние между центрами соседних вагонов, м.</summary>
    public double PitchMetres { get; }

    public bool HasPart(string part) => _frames.ContainsKey((part, "s"));

    /// <summary>Кадр вагона, ракурс которого ближе всего к направлению <paramref name="gridDirection"/> по сетке.</summary>
    public Texture2D Frame(string part, Vector2 gridDirection)
    {
        var best = Headings.MaxBy(h => gridDirection.Normalized().Dot(h.Delta.Normalized()));
        return _frames[(part, best.Name)];
    }

    /// <summary>
    /// Прочитать паспорт набора. Картинки грузятся напрямую из PNG с мипмапами, чтобы вагон оставался гладким
    /// при сильном уменьшении на карте; импорт редактора для этого не нужен.
    /// </summary>
    public static TrainSprites Load(string manifestPath)
    {
        var json = Godot.FileAccess.GetFileAsString(manifestPath);
        if (string.IsNullOrEmpty(json))
            throw new ContentException(manifestPath, "паспорт набора спрайтов не найден или пуст");
        var root = JsonNode.Parse(json, manifestPath);
        var folder = manifestPath[..(manifestPath.LastIndexOf('/') + 1)];
        var pivot = root.Get("pivot_px").Items().Select(n => (float)n.AsDouble()).ToArray();

        var frames = new Dictionary<(string, string), Texture2D>();
        foreach (var part in root.Get("parts").Element.EnumerateObject())
        {
            var node = new JsonNode(part.Value, $"{manifestPath} → parts.{part.Name}");
            foreach (var frame in node.Get("frames").Items())
            {
                var file = folder + frame.String("file");
                var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(file));
                if (image is null || image.IsEmpty())
                    throw frame.Error($"не удалось прочитать картинку {file}");
                image.GenerateMipmaps();
                frames[(part.Name, frame.String("direction"))] = ImageTexture.CreateFromImage(image);
            }
        }
        return new TrainSprites(frames, new Vector2(pivot[0], pivot[1]), root.Double("pixels_per_metre"), root.Double("coupling_pitch_m"));
    }
}

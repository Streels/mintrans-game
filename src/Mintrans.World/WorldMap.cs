using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.World.Facilities;
using Mintrans.World.Networks;
using Mintrans.World.Rail;
using Mintrans.World.Sites;
using Mintrans.World.Spatial;
using Mintrans.World.Terrain;

namespace Mintrans.World;

/// <summary>
/// Карта мира: строительная сетка 5×5 м с природными слоями, клеточными сетями (дороги) и объектами, плюс
/// пространственный мир в метрах (docs/02в) с рельсовым графом (docs/10в). Ничего не знает об изометрии, Godot и экономике.
/// </summary>
public sealed class WorldMap
{
    /// <param name="railSettings">Свойства рельсового графа; по умолчанию — черновые параметры v1 (docs/10в).</param>
    public WorldMap(int width, int height, ulong seed, TerrainCatalog terrainCatalog, IEnumerable<NetworkDefinition> networks,
        RailNetworkSettings? railSettings = null)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Размер карты должен быть положительным: {width}x{height}.");
        Width = width;
        Height = height;
        Seed = seed;
        Terrain = new TerrainLayers(terrainCatalog, width, height);
        var definitions = networks.ToList();
        Networks = new NetworkSet(definitions, width, height);
        Sites = new SiteRegistry(width, height);
        Facilities = new FacilityRegistry(width, height);
        Spatial = new SpatialWorld(
            new WorldBounds(0, 0, width * SpatialScale.CellM, height * SpatialScale.CellM),
            new MapTerrainField(Terrain, width, height));
        var graphs = definitions.Where(d => d.IsGraph).ToList();
        if (graphs.Count > 1)
            throw new ArgumentException($"Рельсовый граф пока один на карту, а объявлено: {string.Join(", ", graphs.Select(g => g.Id))}.");
        if (graphs.Count == 1)
            Rails = new RailNetwork(Spatial, (railSettings ?? new RailNetworkSettings(graphs[0].Id)) with { Kind = graphs[0].Id });
        Crossings = new RailCrossings(this);
    }

    public int Width { get; }
    public int Height { get; }
    public GridRect Bounds => new(0, 0, Width, Height);

    /// <summary>Семя мира. Будущий генератор при одинаковом семени и настройках даёт одинаковую карту.</summary>
    public ulong Seed { get; }

    public TerrainLayers Terrain { get; }
    public NetworkSet Networks { get; }
    public SiteRegistry Sites { get; }

    /// <summary>Транспортные сооружения вне площадок: депо, гаражи, городские пункты.</summary>
    public FacilityRegistry Facilities { get; }

    /// <summary>Объекты с метрической геометрией и разреженный индекс клеток (docs/02в).</summary>
    public SpatialWorld Spatial { get; }

    /// <summary>Рельсовый граф (docs/10в) или null, если на карте нет рельсовой сети.</summary>
    public RailNetwork? Rails { get; }

    /// <summary>Переезды: клетки дорог, через которые проходят рельсы на земле (docs/10а).</summary>
    public RailCrossings Crossings { get; }

    /// <summary>Рельсовый граф; ошибка, если на карте нет рельсовой сети.</summary>
    public RailNetwork RequireRails() =>
        Rails ?? throw new InvalidOperationException("На карте нет рельсовой сети (в networks.json нет сети с раскладкой «graph»).");

    public bool Contains(GridPos pos) => Bounds.Contains(pos);
}

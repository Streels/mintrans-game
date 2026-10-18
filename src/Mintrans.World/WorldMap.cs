using Mintrans.Core.Grid;
using Mintrans.World.Networks;
using Mintrans.World.Sites;
using Mintrans.World.Terrain;

namespace Mintrans.World;

/// <summary>
/// Карта мира: квадратная сетка с природными слоями, транспортными сетями и объектами.
/// Ничего не знает об изометрии, Godot и экономике.
/// </summary>
public sealed class WorldMap
{
    public WorldMap(int width, int height, ulong seed, TerrainCatalog terrainCatalog, IEnumerable<NetworkDefinition> networks)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Размер карты должен быть положительным: {width}x{height}.");
        Width = width;
        Height = height;
        Seed = seed;
        Terrain = new TerrainLayers(terrainCatalog, width, height);
        Networks = new NetworkSet(networks, width, height);
        Sites = new SiteRegistry(width, height);
    }

    public int Width { get; }
    public int Height { get; }
    public GridRect Bounds => new(0, 0, Width, Height);

    /// <summary>Семя мира. Будущий генератор при одинаковом семени и настройках даёт одинаковую карту.</summary>
    public ulong Seed { get; }

    public TerrainLayers Terrain { get; }
    public NetworkSet Networks { get; }
    public SiteRegistry Sites { get; }

    public bool Contains(GridPos pos) => Bounds.Contains(pos);
}

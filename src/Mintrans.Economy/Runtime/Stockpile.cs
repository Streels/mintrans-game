using Mintrans.Core.Ids;

namespace Mintrans.Economy.Runtime;

/// <summary>
/// Склад объекта: запас и вместимость по каждому ресурсу. Хранить можно только ресурсы,
/// для которых есть место. Запас меняется только через операции объекта (<see cref="EconomicSite"/>).
/// </summary>
public sealed class Stockpile
{
    private readonly Dictionary<TypeId, (Mass Amount, Mass Capacity)> _slots;

    public Stockpile(IReadOnlyDictionary<TypeId, Mass> capacities)
    {
        _slots = capacities.ToDictionary(c => c.Key, c => (Mass.Zero, c.Value));
    }

    public IEnumerable<TypeId> Resources => _slots.Keys;

    public bool Stores(TypeId resource) => _slots.ContainsKey(resource);

    public Mass Amount(TypeId resource) => _slots.TryGetValue(resource, out var slot) ? slot.Amount : Mass.Zero;

    public Mass Capacity(TypeId resource) => _slots.TryGetValue(resource, out var slot) ? slot.Capacity : Mass.Zero;

    public Mass Free(TypeId resource) => Capacity(resource) - Amount(resource);

    internal Mass Add(TypeId resource, Mass mass)
    {
        if (!_slots.TryGetValue(resource, out var slot))
            return Mass.Zero;
        var accepted = Mass.Min(mass, slot.Capacity - slot.Amount);
        _slots[resource] = (slot.Amount + accepted, slot.Capacity);
        return accepted;
    }

    internal Mass Remove(TypeId resource, Mass mass)
    {
        if (!_slots.TryGetValue(resource, out var slot))
            return Mass.Zero;
        var taken = Mass.Min(mass, slot.Amount);
        _slots[resource] = (slot.Amount - taken, slot.Capacity);
        return taken;
    }
}

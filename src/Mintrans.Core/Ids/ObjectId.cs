namespace Mintrans.Core.Ids;

/// <summary>
/// Постоянный номер объекта с геометрией в мире (docs/02в): по нему пространственный индекс ссылается на объект.
/// Номера выдаются по порядку и не переиспользуются, поэтому одинаковая последовательность построек даёт одинаковые номера.
/// </summary>
public readonly record struct ObjectId(long Value) : IComparable<ObjectId>
{
    public int CompareTo(ObjectId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"#{Value}";
}

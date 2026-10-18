namespace Mintrans.Core.Ids;

/// <summary>
/// Идентификатор конкретного объекта на карте (города, шахты, завода).
/// Через него другие модули (экономика, транспорт) привязывают к объекту свои данные.
/// </summary>
public readonly record struct SiteId(int Value)
{
    /// <summary>Пустое значение: «здесь нет объекта».</summary>
    public static readonly SiteId None = new(0);

    public bool IsNone => Value == 0;

    public override string ToString() => $"site#{Value}";
}

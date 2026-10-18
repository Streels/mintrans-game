namespace Mintrans.Data.Tests;

/// <summary>Находит папку content/ в репозитории, поднимаясь от папки сборки тестов.</summary>
internal static class ContentLocator
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "world", "terrain.json")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Не найдена папка content/ над " + AppContext.BaseDirectory);
    }

    public static GameContent Load() => GameContent.Load(Root());
}

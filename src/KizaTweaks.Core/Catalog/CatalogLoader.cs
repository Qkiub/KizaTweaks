using System.Reflection;
using System.Text.Json;

namespace KizaTweaks.Core.Catalog;

public static class CatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static TweakCatalog LoadTweaks() => Load<TweakCatalog>("KizaTweaks.Catalog.tweaks.json");

    public static AppCatalog LoadApps() => Load<AppCatalog>("KizaTweaks.Catalog.apps.json");

    public static TweakCatalog ParseTweaks(string json) =>
        JsonSerializer.Deserialize<TweakCatalog>(json, Options) ?? new TweakCatalog();

    private static T Load<T>(string resource) where T : new()
    {
        var asm = typeof(CatalogLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Recurso no encontrado: {resource}");
        return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
    }
}

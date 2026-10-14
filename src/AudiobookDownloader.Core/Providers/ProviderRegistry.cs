namespace AudiobookDownloader.Core;

/// <summary>
/// All book sources registered with the app. The ComboBox is built from this list;
/// adding a provider here is all it takes to make it appear in the UI.
/// </summary>
public static class ProviderRegistry
{
    private static readonly List<IBookProvider> _providers = [];

    public static IReadOnlyList<IBookProvider> Providers => _providers;

    public static void Register(IBookProvider provider)
    {
        if (_providers.Any(p => p.Id == provider.Id))
            return;
        _providers.Add(provider);
    }

    public static IBookProvider? Find(string? id) =>
        _providers.FirstOrDefault(p => p.Id == id);

    public static void Reset() => _providers.Clear();
}

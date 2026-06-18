using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;

namespace WebMediaManager.Providers;

public interface IMetadataProviderResolver
{
    IReadOnlyList<IMetadataProvider> ProvidersFor(LibraryType type);

    IMetadataProvider? Get(MetadataSource source);
}

public sealed class MetadataProviderResolver(IEnumerable<IMetadataProvider> providers) : IMetadataProviderResolver
{
    public IReadOnlyList<IMetadataProvider> ProvidersFor(LibraryType type) =>
        providers.Where(p => p.Supports(type)).ToList();

    public IMetadataProvider? Get(MetadataSource source) =>
        providers.FirstOrDefault(p => p.Source == source);
}

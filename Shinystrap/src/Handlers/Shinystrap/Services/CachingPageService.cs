using Wpf.Ui.Abstractions;

namespace Shinystrap.Handlers.Shinystrap.Services
{
    public class CachingPageService : INavigationViewPageProvider
    {
        private readonly Dictionary<Type, object> _cache = new();
        private readonly IServiceProvider? _serviceProvider;

        // Use this constructor if you have a DI container
        public CachingPageService(IServiceProvider? serviceProvider = null)
        {
            _serviceProvider = serviceProvider;
        }

        public object? GetPage(Type pageType)
        {
            if (_cache.TryGetValue(pageType, out var cached))
                return cached;

            var page = _serviceProvider is not null
                ? _serviceProvider.GetService(pageType) ?? Activator.CreateInstance(pageType)!
                : Activator.CreateInstance(pageType)!;

            _cache[pageType] = page;
            return page;
        }
    }
}
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions;

namespace DeepClean.App.Services;

/// <summary>
/// Resolves navigation targets from the dependency injection container so pages
/// receive their view models and services through the constructor.
/// </summary>
public sealed class PageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _services;

    public PageProvider(IServiceProvider services) => _services = services;

    public object GetPage(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        return _services.GetService(pageType)
            ?? throw new NavigationException($"The page '{pageType.FullName}' is not registered in the container.");
    }
}

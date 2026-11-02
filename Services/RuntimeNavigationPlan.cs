using FisPresentationPlatform.Models;

namespace FisPresentationPlatform.Services;

public sealed record RuntimeNavigationPlan(IReadOnlyList<NavigationPage> Pages, int EntryIndex)
{
    public static RuntimeNavigationPlan Create(PresentationConfig config)
    {
        var pages = config.Navigation.Enabled
            ? config.Navigation.Sections.SelectMany(section => section.Pages).ToList()
            : [];
        var entryIndex = pages.FindIndex(page => string.Equals(page.Path.Replace('\\', '/'), config.EntryPoint.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (entryIndex < 0)
        {
            pages.Insert(0, new NavigationPage { Id = "__runtime-entry", Title = "Home", Path = config.EntryPoint });
            entryIndex = 0;
        }
        return new RuntimeNavigationPlan(pages, entryIndex);
    }
}
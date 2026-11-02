using System.IO;

namespace FisPresentationPlatform.Services;

public static class NavigationBaselineInstaller
{
    public static async Task<int> InstallAsync(DemoLibraryService library, string? source = null)
    {
        if ((await library.LoadAsync()).Any(demo => demo.Config.Id == "fis-navigation-baseline" && demo.Config.Version == "1.1.0" && demo.Available)) return 0;
        source ??= Path.Combine(AppContext.BaseDirectory, "samples", "NavigationBaseline");
        var service = new PresentationService();
        var config = service.Deserialize(await File.ReadAllTextAsync(Path.Combine(source, "presentation.json")));
        var temporary = Path.Combine(Path.GetTempPath(), "fis-baseline-" + Guid.NewGuid().ToString("N") + ".fisxpkg");
        try
        {
            await service.BuildPackageAsync(source, temporary, config);
            await library.ImportAsync(temporary);
            return 1;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
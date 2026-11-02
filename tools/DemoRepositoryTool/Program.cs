using System.IO;
using FisPresentationPlatform.Services;

// Seeds and maintains a local folder that stands in for the shared demo repository.
return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    var force = args.Any(arg => arg.Equals("--force", StringComparison.OrdinalIgnoreCase));
    args = [.. args.Where(arg => !arg.Equals("--force", StringComparison.OrdinalIgnoreCase))];
    try
    {
        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case "init" when args.Length >= 2:
                await InitAsync(args[1], args.Length >= 3 ? args[2] : DemoRepositoryService.CurrentUser);
                return 0;
            case "release" when args.Length >= 4:
                await ReleaseAsync(args[1], args[2], args[3], args.Length >= 5 ? args[4] : "", force);
                return 0;
            case "retire" when args.Length >= 3:
                await RetireAsync(args[1], args[2], args.Length >= 4 ? args[3] : null, force);
                return 0;
            case "install" when args.Length >= 4:
                await InstallAsync(args[1], args[2], args[3], args.Length >= 5 ? args[4] : null);
                return 0;
            case "assign" when args.Length >= 4:
                await AssignAsync(args[1], args[2], args[3..]);
                return 0;
            case "list" when args.Length >= 2:
                await ListAsync(args[1]);
                return 0;
            default:
                Console.WriteLine("""
                    FIS demo repository tool

                      init    <repo> [user]                              Publish the bundled demos at 1.0.0 (skips any already published) and assign the user
                      release <repo> <demo-folder|id> <version> [notes]  Publish a new version of a demo (refuses an existing version unless --force)
                      retire  <repo> <id> [version]                      Withdraw one version, or the whole demo with --force and no version
                      install <repo> <library-root> <id> [version]       Install a published version straight into a local library
                      assign  <repo> <user> <L8 solution>...             Replace a user's solution assignments
                      list    <repo>                                     Show published demos and versions
                    """);
                return args.Length == 0 ? 0 : 1;
        }
    }
    catch (DemoRepositoryPublisher.VersionAlreadyPublishedException exception)
    {
        Console.Error.WriteLine("Refused: " + exception.Message);
        return 3;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine("Error: " + exception.Message);
        return 2;
    }
}

static async Task InitAsync(string repository, string user)
{
    foreach (var folder in DemoRepositoryPublisher.BundledDemoFolders)
    {
        try
        {
            var published = await DemoRepositoryPublisher.PublishAsync(repository, Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, folder), "1.0.0", "Initial release.");
            Console.WriteLine($"Published {folder} v{published.Version} -> {published.Package}");
        }
        catch (DemoRepositoryPublisher.VersionAlreadyPublishedException)
        {
            Console.WriteLine($"Skipped {folder}: v1.0.0 is already published.");
        }
    }
    await AssignAsync(repository, user, ["Instant Payments Hub", "Digital Account Origination"]);
}

static async Task ReleaseAsync(string repository, string demo, string version, string notes, bool force)
{
    var folder = ResolveFolder(demo);
    var published = await DemoRepositoryPublisher.PublishAsync(repository, folder, version, notes, force: force);
    Console.WriteLine($"Published {Path.GetFileName(folder)} v{published.Version} ({published.Bytes:N0} bytes, sha256 {published.Sha256[..12]}...)");
}

static async Task RetireAsync(string repository, string id, string? version, bool force)
{
    if (version is null && !force)
        throw new InvalidOperationException($"Specify a version to retire, or add --force to withdraw every version of '{id}'.");
    var removed = await DemoRepositoryPublisher.RetireAsync(repository, id, version);
    Console.WriteLine($"Retired {id} v{string.Join(", v", removed)}. Laptops that installed it keep their copy until a higher version is released.");
}

static async Task InstallAsync(string repository, string libraryRoot, string id, string? version)
{
    var service = new DemoRepositoryService(repository, Path.Combine(libraryRoot, "repository-cache"));
    var index = (await service.LoadIndexAsync()).Index;
    var demo = index.Demos.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Demo '{id}' is not published in {repository}.");
    var selected = version is null ? demo.Latest : demo.Versions.FirstOrDefault(entry => entry.Version == version);
    if (selected is null) throw new InvalidOperationException($"Version {version} of '{id}' is not published.");
    var installed = await service.DownloadAsync(demo, selected, new DemoLibraryService(libraryRoot));
    Console.WriteLine($"Installed {installed.Config.Name} v{installed.Config.Version} into {libraryRoot}");
}

static async Task AssignAsync(string repository, string user, string[] solutions)
{
    var path = Path.Combine(repository, DemoRepositoryService.AssignmentsFileName);
    var file = File.Exists(path) ? DemoRepositoryService.ParseAssignments(await File.ReadAllTextAsync(path)) : new AssignmentFile();
    file.Assignments.RemoveAll(entry => entry.User.Equals(user, StringComparison.OrdinalIgnoreCase));
    file.Assignments.Add(new UserAssignment { User = user, Solutions = [.. solutions] });
    await DemoRepositoryPublisher.WriteAssignmentsAsync(repository, file);
    Console.WriteLine($"Assigned {user} -> {string.Join(", ", solutions)}");
}

static async Task ListAsync(string repository)
{
    var index = DemoRepositoryService.ParseIndex(await File.ReadAllTextAsync(Path.Combine(repository, DemoRepositoryService.IndexFileName)));
    foreach (var demo in index.Demos)
        Console.WriteLine($"{demo.Id,-28} {demo.Organization.L3,-16} {string.Join(", ", demo.Versions.Select(entry => entry.Version))}");
}

static string ResolveFolder(string demo)
{
    if (Directory.Exists(demo)) return Path.GetFullPath(demo);
    foreach (var folder in DemoRepositoryPublisher.BundledDemoFolders)
    {
        var path = Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, folder);
        if (folder.Equals(demo, StringComparison.OrdinalIgnoreCase)) return path;
        var config = new PresentationService().Deserialize(File.ReadAllText(Path.Combine(path, PresentationService.ConfigurationFileName)));
        if (config.Id.Equals(demo, StringComparison.OrdinalIgnoreCase)) return path;
    }
    throw new DirectoryNotFoundException($"No demo folder or bundled demo id matches '{demo}'.");
}

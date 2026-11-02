using System.Text.Json.Serialization;

namespace FisPresentationPlatform.Models;

public sealed class PresentationConfig
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Id { get; set; } = "new-presentation";
    public string Name { get; set; } = "New presentation";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string EntryPoint { get; set; } = "index.html";
    public BrandingConfig Branding { get; set; } = new();
    public CatalogConfig Catalog { get; set; } = new();
    public NavigationConfig Navigation { get; set; } = new();
    public LoadingConfig Loading { get; set; } = new();
    /// <summary>Selectable personas / data sets offered when the demo is launched.</summary>
    public List<DemoDataSet> DataSets { get; set; } = [];
}

/// <summary>A persona and the sample data a demo is presented with. <see cref="File"/> is a package-relative JSON file.</summary>
public sealed class DemoDataSet
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Default";
    public string Persona { get; set; } = "";
    public string Description { get; set; } = "";
    public string File { get; set; } = "";
    public bool Default { get; set; }
}

public sealed class LoadingConfig
{
    public string ReadyMode { get; set; } = "navigation";
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class BrandingConfig
{
    public string AccentColor { get; set; } = "#4BCD3E";
    public string Logo { get; set; } = "";
    /// <summary>Library/Downloads tile colour theme: green (default), raspberry, eggplant, navy or charcoal.</summary>
    public string TileTheme { get; set; } = "";
}

public sealed class CatalogConfig
{
    public string Product { get; set; } = "";
    public List<string> Functions { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string CoverImage { get; set; } = "";
    public OrganizationConfig Organization { get; set; } = new();
}

/// <summary>Business hierarchy: L3 is the business line, L8 is the actual solution.</summary>
public sealed class OrganizationConfig
{
    public static readonly string[] BusinessLines = ["Capital Markets", "Banking", "Payments", "Merchant Solutions", "Corporate"];

    public string L3 { get; set; } = "";
    public string L4 { get; set; } = "";
    public string L5 { get; set; } = "";
    public string L6 { get; set; } = "";
    public string L7 { get; set; } = "";
    public string L8 { get; set; } = "";

    [JsonIgnore] public bool IsEmpty => new[] { L3, L4, L5, L6, L7, L8 }.All(string.IsNullOrWhiteSpace);
    [JsonIgnore] public string Path => string.Join(" › ", new[] { L3, L4, L5, L6, L7, L8 }.Where(level => !string.IsNullOrWhiteSpace(level)));
}

public sealed class NavigationConfig
{
    public bool Enabled { get; set; } = true;
    public List<NavigationSection> Sections { get; set; } = [];
}

public sealed class NavigationSection
{
    public string Id { get; set; } = "section";
    public string Title { get; set; } = "Section";
    public List<NavigationPage> Pages { get; set; } = [];
}

public sealed class NavigationPage
{
    public string Id { get; set; } = "page";
    public string Title { get; set; } = "Page";
    public string Path { get; set; } = "index.html";
}

public sealed class PackageManifest
{
    public string PackageId { get; set; } = "";
    public string PresentationId { get; set; } = "";
    public string PresentationVersion { get; set; } = "";
    public string EntryPoint { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public string BuilderVersion { get; set; } = "0.1.0";
    public List<ManifestFile> Files { get; set; } = [];
}

public sealed class ManifestFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string Sha256 { get; set; } = "";
}

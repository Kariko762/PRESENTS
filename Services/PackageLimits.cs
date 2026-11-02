namespace FisPresentationPlatform.Services;

public sealed record PackageLimits
{
    public int MaxEntries { get; init; } = 10_000;
    public long MaxExpandedBytes { get; init; } = 8L * 1024 * 1024 * 1024;
    public long MaxFileBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public long MaxManifestBytes { get; init; } = 8 * 1024 * 1024;
    public long MaxConfigurationBytes { get; init; } = 1024 * 1024;
    public double MaxCompressionRatio { get; init; } = 1000;
}
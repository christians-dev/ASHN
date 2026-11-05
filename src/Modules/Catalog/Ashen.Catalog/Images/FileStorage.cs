using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ashen.Catalog.Images;

/// <summary>Where product images live. Local disk now, S3 later (constitution III).</summary>
internal interface IFileStorage
{
    Task Save(string relativePath, Stream content, CancellationToken cancellationToken);
    Task Delete(string relativePath, CancellationToken cancellationToken);

    /// <summary>The URL browsers load the file from.</summary>
    string PublicUrl(string relativePath);
}

internal sealed class LocalFileStorage(IConfiguration configuration, IHostEnvironment environment) : IFileStorage
{
    /// <summary>Same key the host uses to serve <c>/media</c>.</summary>
    public const string RootPathKey = "Catalog:Media:RootPath";

    private readonly string _root = Path.GetFullPath(
        Path.Combine(environment.ContentRootPath, configuration[RootPathKey] ?? "media"));

    public async Task Save(string relativePath, Stream content, CancellationToken cancellationToken)
    {
        var path = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task Delete(string relativePath, CancellationToken cancellationToken)
    {
        File.Delete(FullPath(relativePath));
        return Task.CompletedTask;
    }

    public string PublicUrl(string relativePath) => "/media/" + relativePath.Replace('\\', '/');

    // Never let a stored path escape the media root.
    private string FullPath(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException("Path is outside the media root.", nameof(relativePath));
    }
}

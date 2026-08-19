using Vastora.Application.Common.Interfaces;

namespace Vastora.Infrastructure.Storage;

/// <summary>
/// Default IFileStorageService: writes to local disk under the app's base directory. Fine for
/// a single-instance/dev deployment; won't survive a redeploy on most PaaS hosts and doesn't
/// scale past one instance — swap for an S3/Azure Blob/Cloudinary implementation before real
/// production use (Roadmap §9.5). The physical path here must match the one StaticFileOptions
/// is configured against in Program.cs, or saved files won't be servable back.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private static readonly string UploadsRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads");

    public async Task<string> SaveAsync(string businessId, Stream content, string fileExtension, CancellationToken ct = default)
    {
        var directory = Path.Combine(UploadsRoot, businessId);
        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}{fileExtension}";
        var filePath = Path.Combine(directory, fileName);

        await using var fileStream = File.Create(filePath);
        await content.CopyToAsync(fileStream, ct);

        // Deliberately host-relative, not absolute: baking today's host into the stored value
        // would freeze it to whatever domain happened to be current at upload time (a dev
        // tunnel, a staging host, ...) — wrong the moment that domain changes, and silently so,
        // since nothing re-writes old rows. Whoever needs an absolute URL (BusinessAssetUrls, for
        // the one consumer — outbound email — that has no implicit base to resolve against)
        // resolves this against the *current* base at read time instead.
        return $"/uploads/{businessId}/{fileName}";
    }
}

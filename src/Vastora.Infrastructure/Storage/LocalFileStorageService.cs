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

        return $"/uploads/{businessId}/{fileName}";
    }
}

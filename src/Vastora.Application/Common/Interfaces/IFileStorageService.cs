namespace Vastora.Application.Common.Interfaces;

/// <summary>
/// Stores an uploaded file and returns a URL it can be fetched back from. One implementation
/// today — local disk (see LocalFileStorageService in Infrastructure) — behind this interface
/// specifically so swapping to S3/Azure Blob/Cloudinary later (Roadmap §9.5) is a new
/// implementation + one DI registration change, not a rewrite of every caller.
/// </summary>
public interface IFileStorageService
{
    /// <summary>businessId scopes the storage path so files from different Businesses never collide.</summary>
    Task<string> SaveAsync(string businessId, Stream content, string fileExtension, CancellationToken ct = default);
}

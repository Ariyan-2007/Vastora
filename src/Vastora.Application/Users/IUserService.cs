using Vastora.Application.Auth;
using Vastora.Domain.Enums;

namespace Vastora.Application.Users;

public interface IUserService
{
    Task<UserSummaryResponse> CreateStaffAsync(string tenantId, string businessId, CreateStaffRequest request, CancellationToken ct = default);

    Task<List<UserSummaryResponse>> GetBusinessStaffAsync(string tenantId, string businessId, CancellationToken ct = default);

    Task<List<UserSummaryResponse>> GetBusinessCustomersAsync(string tenantId, string businessId, CancellationToken ct = default);

    Task<UserSummaryResponse> GetByIdAsync(string tenantId, string userId, CancellationToken ct = default);

    Task<UserSummaryResponse> GetMeAsync(string userId, CancellationToken ct = default);

    Task<UserSummaryResponse> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default);

    Task<UserSummaryResponse> UpdateStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken ct = default);
}

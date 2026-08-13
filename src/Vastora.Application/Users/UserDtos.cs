using Vastora.Domain.Enums;

namespace Vastora.Application.Users;

/// <summary>Only BusinessAdmin, BusinessStaff and DeliveryAgent may be created this way — Customers self-register, TenantOwners are created at tenant sign-up.</summary>
public record CreateStaffRequest(string FullName, string Email, string Password, string Phone, UserRole Role);

public record UpdateProfileRequest(string FullName, string Phone);

public record UpdateUserStatusRequest(UserStatus Status);

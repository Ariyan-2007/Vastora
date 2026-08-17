using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Users;

/// <summary>Only BusinessAdmin, BusinessStaff and DeliveryAgent may be created this way — Customers self-register, TenantOwners are created at tenant sign-up.</summary>
public record CreateStaffRequest(string FullName, string Email, string Password, string Phone, UserRole Role);

public record UpdateProfileRequest(string FullName, string Phone);

public record UpdateUserStatusRequest(UserStatus Status);

/// <summary>One entry in AppUser.Addresses — the saved address book (distinct from the one-off address captured per order at checkout).</summary>
public record AddressResponse(
    string Id, string Label, string Line1, string Line2, string City, string State,
    string PostalCode, string Country, string Phone, bool IsDefault)
{
    public static AddressResponse From(Address a) =>
        new(a.Id, a.Label, a.Line1, a.Line2, a.City, a.State, a.PostalCode, a.Country, a.Phone, a.IsDefault);
}

public record SaveAddressRequest(
    string Label, string Line1, string Line2, string City, string State,
    string PostalCode, string Country, string Phone, bool IsDefault);

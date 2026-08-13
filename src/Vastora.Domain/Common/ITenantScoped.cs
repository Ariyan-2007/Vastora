namespace Vastora.Domain.Common;

/// <summary>
/// Marks an entity as belonging to a single Tenant (the paying subscriber account).
/// Repositories use this to automatically scope every query and prevent cross-tenant leaks.
/// </summary>
public interface ITenantScoped
{
    string TenantId { get; set; }
}

/// <summary>
/// Marks an entity as belonging to a single Business (storefront) within a Tenant.
/// A Tenant may own one Business (single-business subscription) or many (group/SuperOffice subscription).
/// </summary>
public interface IBusinessScoped
{
    string BusinessId { get; set; }
}

namespace Vastora.Domain.Entities;

/// <summary>Embedded value object — stored inline inside AppUser / Order documents, never its own collection.</summary>
public class Address
{
    /// <summary>
    /// Only meaningful for an entry in AppUser.Addresses (the saved address book) — lets one
    /// entry be targeted for update/delete. Left empty for the one-off address snapshotted onto
    /// an Order at checkout, which never needs to be individually addressed again.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string Line2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

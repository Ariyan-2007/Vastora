using Vastora.Domain.Enums;

namespace Vastora.Application.GiftCards;

public record IssueGiftCardRequest(decimal Amount, string? IssuedToEmail, DateTime? ExpiresAt);

/// <summary>
/// The plaintext <paramref name="Code"/> is populated exactly once, in the response to the issue
/// call, and is unrecoverable afterwards — only its hash is stored. Every later read returns null
/// for it and identifies the card by CodeSuffix instead.
/// </summary>
public record GiftCardResponse(
    string Id,
    string? Code,
    string CodeSuffix,
    decimal InitialBalance,
    decimal RemainingBalance,
    string Currency,
    string? IssuedToEmail,
    DateTime? ExpiresAt,
    bool IsActive,
    DateTime CreatedAt);

public record GiftCardBalanceResponse(string CodeSuffix, decimal RemainingBalance, string Currency, DateTime? ExpiresAt, bool IsRedeemable);

public record StoreCreditEntryResponse(
    string Id,
    decimal Amount,
    string Currency,
    StoreCreditReason Reason,
    string Note,
    string? ReferenceOrderId,
    DateTime CreatedAt);

public record StoreCreditBalanceResponse(decimal Balance, string Currency, List<StoreCreditEntryResponse> RecentEntries);

public record GrantStoreCreditRequest(decimal Amount, string Note);

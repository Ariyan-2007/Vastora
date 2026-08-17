namespace Vastora.Application.Shipping;

/// <summary>One selectable shipping option at checkout — §9.20.</summary>
public record ShippingQuote(
    string ZoneId,
    string RateId,
    string Name,
    decimal Price,
    int? EstimatedDaysMin,
    int? EstimatedDaysMax);

public record ShippingRateRequest(
    string Name,
    decimal Price,
    decimal? MinOrderSubtotal,
    decimal? MaxOrderSubtotal,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    int? EstimatedDaysMin,
    int? EstimatedDaysMax,
    bool IsActive);

public record ShippingRateResponse(
    string Id,
    string Name,
    decimal Price,
    decimal? MinOrderSubtotal,
    decimal? MaxOrderSubtotal,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    int? EstimatedDaysMin,
    int? EstimatedDaysMax,
    bool IsActive);

public record CreateShippingZoneRequest(
    string Name,
    List<string> Countries,
    List<string> Regions,
    List<ShippingRateRequest> Rates,
    int Priority,
    bool IsActive);

public record ShippingZoneResponse(
    string Id,
    string Name,
    List<string> Countries,
    List<string> Regions,
    List<ShippingRateResponse> Rates,
    int Priority,
    bool IsActive);

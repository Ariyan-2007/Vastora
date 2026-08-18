using System.Net;
using System.Text;
using Vastora.Domain.Entities;

namespace Vastora.Application.Notifications;

/// <summary>
/// Builds (Subject, PlainTextBody, HtmlBody) for every email the platform sends. One shared
/// HTML layout (<see cref="Layout"/>) carries the Business's logo/name/brand color into each
/// message, so a customer's inbox always reads as "this shop", not "Vastora" (§9.10). Falls back
/// to generic Vastora branding when <see cref="Business"/> is null — e.g. a password reset for a
/// BackOffice staff account, which has no single storefront to brand as.
/// </summary>
public static class EmailTemplates
{
    private const string DefaultThemeColor = "#111827";
    private const string FontFamily = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    public static (string Subject, string PlainBody, string HtmlBody) VerifyEmail(
        Business? business, string recipientName, string verifyLink, DateTime expiresAt)
    {
        const string subject = "Confirm your email address";
        var plain =
            $"Hi {recipientName},\n\n" +
            "Confirm your email address to finish setting up your account:\n" +
            $"{verifyLink}\n\n" +
            $"This link expires {expiresAt:dd MMM yyyy HH:mm} UTC. If you didn't create this account, you can ignore this email.";

        var body = new StringBuilder()
            .Append(Paragraph($"Hi {Enc(recipientName)},"))
            .Append(Paragraph("Confirm your email address to finish setting up your account."))
            .Append(Button("Confirm email address", verifyLink, ThemeColor(business)))
            .Append(Muted($"This link expires {expiresAt:dd MMM yyyy HH:mm} UTC. If you didn't create this account, you can safely ignore this email."))
            .ToString();

        return (subject, plain, Layout(business, subject, body));
    }

    public static (string Subject, string PlainBody, string HtmlBody) PasswordReset(
        Business? business, string recipientName, string resetLink, DateTime expiresAt)
    {
        const string subject = "Reset your password";
        var plain =
            $"Hi {recipientName},\n\n" +
            "We received a request to reset your password. Use the link below to choose a new one:\n" +
            $"{resetLink}\n\n" +
            $"This link expires {expiresAt:dd MMM yyyy HH:mm} UTC. If you didn't request this, you can ignore this email — your password won't change.";

        var body = new StringBuilder()
            .Append(Paragraph($"Hi {Enc(recipientName)},"))
            .Append(Paragraph("We received a request to reset your password. Use the button below to choose a new one."))
            .Append(Button("Reset password", resetLink, ThemeColor(business)))
            .Append(Muted($"This link expires {expiresAt:dd MMM yyyy HH:mm} UTC. If you didn't request this, you can safely ignore this email — your password won't change."))
            .ToString();

        return (subject, plain, Layout(business, subject, body));
    }

    public static (string Subject, string PlainBody, string HtmlBody) OrderConfirmation(Business? business, Order order)
    {
        var subject = $"Order confirmed — {order.OrderNumber}";
        var plain = new StringBuilder()
            .AppendLine($"Thanks for your order, {order.OrderNumber}!")
            .AppendLine()
            .AppendJoin('\n', order.Items.Select(i => $"  {i.Quantity} x {i.ProductName} — {Money(i.LineTotal, order.Currency)}"))
            .AppendLine()
            .AppendLine()
            .AppendLine($"Total: {Money(order.Total, order.Currency)}")
            .ToString();

        var body = new StringBuilder()
            .Append(Paragraph("Thanks for your order! Here's what we're getting ready for you."))
            .Append(OrderSummaryCard(order))
            .Append(Muted("We'll email you again as soon as your order ships."))
            .ToString();

        return (subject, plain, Layout(business, subject, body));
    }

    public static (string Subject, string PlainBody, string HtmlBody) OrderStatusUpdate(Business? business, Order order, string? note)
    {
        var subject = $"Order {order.OrderNumber} is now {order.Status}";
        var plain =
            $"Your order {order.OrderNumber} is now '{order.Status}'." +
            (string.IsNullOrWhiteSpace(note) ? "" : $"\n\n{note}");

        var body = new StringBuilder()
            .Append(Paragraph($"Your order <strong>{Enc(order.OrderNumber)}</strong> status has changed to:"))
            .Append(StatusPill(order.Status.ToString(), ThemeColor(business)));

        if (!string.IsNullOrWhiteSpace(note))
        {
            body.Append(Paragraph(Enc(note)));
        }

        body.Append(OrderSummaryCard(order));

        return (subject, plain, Layout(business, subject, body.ToString()));
    }

    public static (string Subject, string PlainBody, string HtmlBody) OrderShipped(
        Business? business, Order order, string? carrierName, string? trackingNumber, string? trackingUrl)
    {
        var subject = $"Your order {order.OrderNumber} has shipped";
        var plain =
            $"Order {order.OrderNumber} is on its way." +
            (string.IsNullOrWhiteSpace(trackingNumber) ? "" : $"\nCarrier: {carrierName}\nTracking: {trackingNumber}") +
            (string.IsNullOrWhiteSpace(trackingUrl) ? "" : $"\n{trackingUrl}");

        var body = new StringBuilder()
            .Append(Paragraph($"Good news — order <strong>{Enc(order.OrderNumber)}</strong> is on its way."));

        if (!string.IsNullOrWhiteSpace(trackingNumber))
        {
            body.Append(InfoTable(
            [
                ("Carrier", string.IsNullOrWhiteSpace(carrierName) ? "—" : carrierName!),
                ("Tracking number", trackingNumber!)
            ]));
        }

        if (!string.IsNullOrWhiteSpace(trackingUrl))
        {
            body.Append(Button("Track your package", trackingUrl!, ThemeColor(business)));
        }

        body.Append(OrderSummaryCard(order));

        return (subject, plain, Layout(business, subject, body.ToString()));
    }

    public static (string Subject, string PlainBody, string HtmlBody) ReturnDecision(Business? business, ReturnRequest entity, string? note)
    {
        var approved = entity.Status == Domain.Enums.ReturnStatus.Approved;
        var subject = $"Return {entity.RmaNumber} {(approved ? "approved" : "rejected")}";
        var plain = $"Your return request {entity.RmaNumber} was {entity.Status}." +
            (string.IsNullOrWhiteSpace(note) ? "" : $"\n\n{note}");

        var body = new StringBuilder()
            .Append(Paragraph($"Your return request <strong>{Enc(entity.RmaNumber)}</strong> was:"))
            .Append(StatusPill(entity.Status.ToString(), approved ? "#15803d" : "#b91c1c"));

        if (!string.IsNullOrWhiteSpace(note))
        {
            body.Append(Paragraph(Enc(note)));
        }

        body.Append(ReturnItemsTable(entity));

        return (subject, plain, Layout(business, subject, body.ToString()));
    }

    public static (string Subject, string PlainBody, string HtmlBody) RefundIssued(Business? business, ReturnRequest entity, decimal amount)
    {
        var subject = $"Refund issued for {entity.RmaNumber}";
        var plain = $"We've refunded {Money(amount, entity.Currency)} for order {entity.OrderNumber}.";

        var body = new StringBuilder()
            .Append(Paragraph($"We've refunded <strong>{Money(amount, entity.Currency)}</strong> for order <strong>{Enc(entity.OrderNumber)}</strong>."))
            .Append(Muted("Refunds can take a few business days to appear, depending on your bank or card issuer."))
            .Append(ReturnItemsTable(entity));

        return (subject, plain, Layout(business, subject, body.ToString()));
    }

    public static (string Subject, string PlainBody, string HtmlBody) AbandonedCart(
        Business? business, string? recipientName, List<string> itemNames, int itemCount, string? shopLink, string? unsubscribeUrl)
    {
        const string subject = "You left something behind";
        var names = string.Join(", ", itemNames);
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hi there," : $"Hi {recipientName},";
        var plain = $"{greeting}\n\nYour cart still has {itemCount} item(s): {names}.";

        var body = new StringBuilder()
            .Append(Paragraph(Enc(greeting)))
            .Append(Paragraph($"Your cart still has <strong>{itemCount}</strong> item(s) waiting for you: {Enc(names)}."));

        if (!string.IsNullOrWhiteSpace(shopLink))
        {
            body.Append(Button("Finish checking out", shopLink!, ThemeColor(business)));
        }

        return (subject, plain, Layout(business, subject, body.ToString(), unsubscribeUrl));
    }

    public static (string Subject, string PlainBody, string HtmlBody) BackInStock(
        Business? business, string productName, string? productLink, string? unsubscribeUrl)
    {
        var subject = $"{productName} is back in stock";
        var plain = $"'{productName}' is available again." + (string.IsNullOrWhiteSpace(productLink) ? "" : $"\n{productLink}");

        var body = new StringBuilder()
            .Append(Paragraph($"Good news — <strong>{Enc(productName)}</strong> is back in stock."));

        if (!string.IsNullOrWhiteSpace(productLink))
        {
            body.Append(Button("Shop now", productLink!, ThemeColor(business)));
        }

        return (subject, plain, Layout(business, subject, body.ToString(), unsubscribeUrl));
    }

    public static (string Subject, string PlainBody, string HtmlBody) LowStockMerchant(
        Business? business, IReadOnlyList<(string Name, string Sku, int StockQuantity, int? ReorderThreshold)> items)
    {
        var subject = $"{items.Count} product(s) low on stock";
        var plain = "These products are at or below their reorder threshold:\n" +
            string.Join('\n', items.Select(i => $"- {i.Name} ({i.Sku}): {i.StockQuantity} left"));

        var rows = new StringBuilder();
        foreach (var item in items)
        {
            rows.Append(TableRow(
                Enc(item.Name) + " <span style=\"color:#6b7280;\">(" + Enc(item.Sku) + ")</span>",
                item.StockQuantity + " left",
                item.ReorderThreshold is null ? "—" : "reorder at " + item.ReorderThreshold));
        }

        var body = new StringBuilder()
            .Append(Paragraph("These products are at or below their reorder threshold:"))
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:16px 0;\">")
            .Append(rows)
            .Append("</table>");

        return (subject, plain, Layout(business, subject, body.ToString()));
    }

    /// <summary>
    /// §9.43. A code targeted at one customer or a segment — the delivery mechanism for a Hidden
    /// coupon/promotion, which never appears in the storefront's available-offers listing and so
    /// can only be discovered this way.
    /// </summary>
    public static (string Subject, string PlainBody, string HtmlBody) DiscountCode(
        Business? business, string? recipientName, string code, string label, string description, DateTime? expiresAt, string? unsubscribeUrl)
    {
        var subject = $"A code just for you: {code}";
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hi there," : $"Hi {recipientName},";
        var expiry = expiresAt is null ? "" : $" Valid until {expiresAt:dd MMM yyyy}.";
        var plain = $"{greeting}\n\nHere's a code for you: {code} — {description}.{expiry}";

        var body = new StringBuilder()
            .Append(Paragraph(Enc(greeting)))
            .Append(Paragraph($"Here's a code just for you — <strong>{Enc(label)}</strong>: {Enc(description)}."))
            .Append(CodeBox(code, ThemeColor(business)));

        if (expiresAt is not null)
        {
            body.Append(Muted($"Valid until {expiresAt:dd MMM yyyy}."));
        }

        return (subject, plain, Layout(business, subject, body.ToString(), unsubscribeUrl));
    }

    public static (string Subject, string PlainBody, string HtmlBody) ReviewRequest(
        Business? business, string? recipientName, List<string> productNames, string? unsubscribeUrl)
    {
        const string subject = "How was your order?";
        var names = string.Join(", ", productNames);
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hi there," : $"Hi {recipientName},";
        var plain = $"{greeting}\n\nTell others what you thought of: {names}.";

        var body = new StringBuilder()
            .Append(Paragraph(Enc(greeting)))
            .Append(Paragraph($"We'd love to hear what you thought of: <strong>{Enc(names)}</strong>."))
            .Append(Muted("A quick review helps other shoppers — and takes less than a minute."));

        return (subject, plain, Layout(business, subject, body.ToString(), unsubscribeUrl));
    }

    // -----------------------------------------------------------------------------------------
    // Shared layout
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Same treatment applied two ways: as an actual &lt;span&gt; when a Business has no logo at
    /// all, and inlined onto the &lt;img&gt; tag's alt text when it does — most mail clients that
    /// block remote images (or fail to render an unsupported format) still render styled alt
    /// text, so a shop with a logo degrades to the same brand-title look rather than a bare
    /// system-font label or a blank box.
    /// </summary>
    private const string WordmarkStyle = "color:#ffffff;font-size:22px;font-weight:700;letter-spacing:0.3px;font-family:" + FontFamily + ";";

    private static string Layout(Business? business, string title, string bodyHtml, string? unsubscribeUrl = null)
    {
        var name = string.IsNullOrWhiteSpace(business?.Name) ? "Vastora" : business.Name;
        var color = ThemeColor(business);

        // Business.LogoUrl is expected to be an SVG asset. Support for <img src="*.svg"> varies
        // by client (Apple Mail/modern Gmail render it, most Outlook builds don't) — the styled
        // alt text below is the fallback for exactly that case, not just for blocked images.
        var header = string.IsNullOrWhiteSpace(business?.LogoUrl)
            ? "<span style=\"" + WordmarkStyle + "\">" + Enc(name) + "</span>"
            : "<img src=\"" + Enc(business!.LogoUrl) + "\" alt=\"" + Enc(name) + "\" style=\"max-height:40px;max-width:220px;display:block;border:0;" + WordmarkStyle + "\">";

        var footer = new StringBuilder();
        footer.Append("<p style=\"margin:0 0 4px;\">").Append(Enc(name)).Append("</p>");
        if (business?.Address is { } addr && !string.IsNullOrWhiteSpace(addr.Line1))
        {
            footer.Append("<p style=\"margin:0 0 4px;\">")
                .Append(Enc(addr.Line1)).Append(", ").Append(Enc(addr.City)).Append(' ').Append(Enc(addr.PostalCode))
                .Append("</p>");
        }
        if (!string.IsNullOrWhiteSpace(business?.ContactEmail))
        {
            footer.Append("<p style=\"margin:0 0 4px;\">").Append(Enc(business!.ContactEmail)).Append("</p>");
        }
        footer.Append("<p style=\"margin:12px 0 0;color:#c1c5cd;\">This is an automated message — please don't reply directly to this email.</p>");
        if (!string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            footer.Append("<p style=\"margin:8px 0 0;\"><a href=\"").Append(Enc(unsubscribeUrl!))
                .Append("\" style=\"color:#9ca3af;text-decoration:underline;\">Unsubscribe from these emails</a></p>");
        }

        return
            "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            "<title>" + Enc(title) + "</title></head>" +
            "<body style=\"margin:0;padding:0;background-color:#f4f4f7;\">" +
            "<span style=\"display:none;font-size:1px;color:#f4f4f7;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;\">" + Enc(title) + "</span>" +
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:#f4f4f7;padding:32px 16px;\">" +
            "<tr><td align=\"center\">" +
            "<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:600px;max-width:100%;background-color:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.08);\">" +
            "<tr><td style=\"background-color:" + color + ";padding:24px 32px;\">" + header + "</td></tr>" +
            "<tr><td style=\"padding:32px;font-family:" + FontFamily + ";color:#1f2937;font-size:15px;line-height:1.6;\">" + bodyHtml + "</td></tr>" +
            "<tr><td style=\"padding:24px 32px;background-color:#f9fafb;border-top:1px solid #e5e7eb;font-family:" + FontFamily + ";font-size:12px;color:#9ca3af;line-height:1.6;\">" + footer + "</td></tr>" +
            "</table></td></tr></table></body></html>";
    }

    private static string OrderSummaryCard(Order order)
    {
        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:16px 0;\">");
        foreach (var item in order.Items)
        {
            var label = Enc(item.ProductName);
            if (!string.IsNullOrWhiteSpace(item.VariantSummary))
            {
                label += "<br><span style=\"color:#6b7280;font-size:12px;\">" + Enc(item.VariantSummary) + "</span>";
            }

            sb.Append("<tr>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#1f2937;\">").Append(label).Append("</td>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#6b7280;text-align:center;white-space:nowrap;\">x").Append(item.Quantity).Append("</td>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#1f2937;text-align:right;white-space:nowrap;\">").Append(Money(item.LineTotal, order.Currency)).Append("</td>")
                .Append("</tr>");
        }

        sb.Append("<tr><td colspan=\"2\" style=\"padding:12px 0 0;font-size:14px;color:#6b7280;text-align:right;\">Total</td>")
            .Append("<td style=\"padding:12px 0 0;font-size:16px;font-weight:600;color:#1f2937;text-align:right;white-space:nowrap;\">").Append(Money(order.Total, order.Currency)).Append("</td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string ReturnItemsTable(ReturnRequest entity)
    {
        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:16px 0;\">");
        foreach (var item in entity.Items)
        {
            sb.Append("<tr>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#1f2937;\">").Append(Enc(item.ProductName)).Append("</td>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#6b7280;text-align:center;white-space:nowrap;\">x").Append(item.Quantity).Append("</td>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#1f2937;text-align:right;white-space:nowrap;\">").Append(Money(item.LineRefund, entity.Currency)).Append("</td>")
                .Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    private static string InfoTable(IEnumerable<(string Label, string Value)> rows)
    {
        var sb = new StringBuilder("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:16px 0;\">");
        foreach (var (label, value) in rows)
        {
            sb.Append(TableRow(Enc(label), Enc(value), null));
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    private static string TableRow(string left, string middle, string? right)
    {
        var sb = new StringBuilder("<tr>")
            .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#1f2937;\">").Append(left).Append("</td>")
            .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:14px;color:#6b7280;text-align:right;white-space:nowrap;\">").Append(middle).Append("</td>");

        if (right is not null)
        {
            sb.Append("<td style=\"padding:8px 0;border-bottom:1px solid #e5e7eb;font-size:12px;color:#9ca3af;text-align:right;white-space:nowrap;\">").Append(right).Append("</td>");
        }

        sb.Append("</tr>");
        return sb.ToString();
    }

    private static string Paragraph(string html) =>
        "<p style=\"margin:0 0 16px;\">" + html + "</p>";

    private static string Muted(string html) =>
        "<p style=\"margin:16px 0 0;color:#6b7280;font-size:13px;\">" + html + "</p>";

    private static string StatusPill(string status, string color) =>
        "<p style=\"margin:0 0 16px;\"><span style=\"display:inline-block;padding:6px 14px;border-radius:999px;background-color:" + color + ";color:#ffffff;font-size:13px;font-weight:600;\">" + Enc(status) + "</span></p>";

    private static string CodeBox(string code, string color) =>
        "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:8px 0 24px;\"><tr><td style=\"border:2px dashed " + color + ";border-radius:6px;padding:14px 24px;\">" +
        "<span style=\"font-family:'SFMono-Regular',Consolas,monospace;font-size:20px;font-weight:700;letter-spacing:2px;color:" + color + ";\">" + Enc(code) + "</span>" +
        "</td></tr></table>";

    private static string Button(string text, string url, string color) =>
        "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:8px 0 24px;\"><tr><td style=\"border-radius:6px;background-color:" + color + ";\">" +
        "<a href=\"" + Enc(url) + "\" style=\"display:inline-block;padding:12px 24px;font-family:" + FontFamily + ";font-size:14px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:6px;\">" + Enc(text) + "</a>" +
        "</td></tr></table>";

    private static string ThemeColor(Business? business) =>
        string.IsNullOrWhiteSpace(business?.ThemeColor) ? DefaultThemeColor : business.ThemeColor;

    private static string Money(decimal amount, string currency) =>
        amount.ToString("0.00") + " " + Enc(currency);

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

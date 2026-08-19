using System.Net;
using System.Text;
using Vastora.Domain.Entities;

namespace Vastora.Application.Notifications;

/// <summary>
/// Builds (Subject, PlainTextBody, HtmlBody) for every email the platform sends. One shared HTML
/// design system (<see cref="Layout"/> + the private component helpers below it) carries the
/// Business's logo/name/brand color into each message, so a customer's inbox always reads as
/// "this shop", not "Vastora" (§9.10). Falls back to generic Vastora branding when
/// <see cref="Business"/> is null — e.g. a password reset for a BackOffice/SuperOffice/Platform
/// staff account, which has no single storefront to brand as, and always sends through the
/// platform's own mail domain rather than any Business's (see NotificationMessage.BusinessId /
/// SmtpNotificationService).
/// </summary>
public static class EmailTemplates
{
    // -----------------------------------------------------------------------------------------
    // Design tokens — the shared visual language every template below is built from.
    // -----------------------------------------------------------------------------------------

    private const string PageBg = "#f5ead8";
    private const string CardBg = "#ffffff";
    private const string TextDark = "#201e1d";
    private const string TextMuted = "#645c50";
    private const string TextFaint = "#82796a";
    private const string ChipBg = "#ebddc5";
    private const string SwatchBg = "#ebddc5";
    private const string DividerColor = "#dcd3c4";

    /// <summary>Accent used for buttons/badges/links when a message has no Business to brand as (see class remarks).</summary>
    private const string DefaultAccent = "#201e1d";

    private const string BodyFont = "'Figtree',Arial,Helvetica,sans-serif";
    private const string HeadingFont = "'Caprasimo',Georgia,serif";

    public static (string Subject, string PlainBody, string HtmlBody) VerifyEmail(
        Business? business, string recipientName, string verifyLink, DateTime expiresAt)
    {
        const string subject = "Confirm your email address";
        var plain =
            $"Hi {recipientName},\n\n" +
            "Confirm your email address to finish setting up your account:\n" +
            $"{verifyLink}\n\n" +
            $"This link expires {expiresAt:dd MMM yyyy HH:mm} UTC. If you didn't create this account, you can ignore this email.";

        var name = DisplayName(business);
        var intro = business is null
            ? $"Hi {Enc(recipientName)}, confirm your email address to finish setting up your account."
            : $"Hi {Enc(recipientName)}, thanks for creating {Article(name)} {Enc(name)} account. Verify your address below so we can send order and delivery updates to the right place.";

        var content = new StringBuilder()
            .Append(Row("0 40px 28px", ButtonHtml("Confirm email address", verifyLink, Accent(business))))
            .Append(Row("0 40px 32px", PlainLinkChip("Button not working? Paste this link into your browser:", verifyLink)))
            .Append(Divider())
            .Append(Row("20px 40px 36px", MutedHtml($"This link expires {expiresAt:dd MMM yyyy HH:mm} UTC. If you didn't create this account, you can safely ignore this email.")))
            .ToString();

        return (subject, plain, Layout(business, subject, "One quick step", "Confirm your email", intro, content));
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

        var name = DisplayName(business);
        var intro = $"Hi {Enc(recipientName)}, we received a request to reset the password on your {Enc(name)} account. Choose a new one below.";

        var content = new StringBuilder()
            .Append(Row("0 40px 28px", ButtonHtml("Reset password", resetLink, Accent(business))))
            .Append(Row("0 40px 32px", PlainLinkChip("Link not working? Paste this into your browser:", resetLink)))
            .Append(Divider())
            .Append(Row("20px 40px 36px", MutedHtml($"This link expires {expiresAt:dd MMM yyyy HH:mm} UTC and can only be used once. If you didn't request this, your password is still safe — just ignore this email.")))
            .ToString();

        return (subject, plain, Layout(business, subject, "Password reset", "Let's get you back in", intro, content));
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

        var intro = $"Order <strong style=\"color:{TextDark};\">#{Enc(order.OrderNumber)}</strong> is confirmed and being prepared.";

        var content = new StringBuilder()
            .Append(Row("0 40px 6px", ItemsBlock(order.Items.Select(i => (i.ProductName, i.VariantSummary, i.Quantity, (decimal?)i.LineTotal)).ToList(), order.Currency)))
            .Append(Row("8px 40px 0", TotalsBlock(order)))
            .Append(ShippingChipRow(order))
            .Append(Row("26px 40px 36px", MutedHtml("We'll email you again as soon as your order ships.")))
            .ToString();

        return (subject, plain, Layout(business, subject, "Order confirmed", "Thanks for your order!", intro, content));
    }

    public static (string Subject, string PlainBody, string HtmlBody) OrderStatusUpdate(Business? business, Order order, string? note)
    {
        var subject = $"Order {order.OrderNumber} is now {order.Status}";
        var plain =
            $"Your order {order.OrderNumber} is now '{order.Status}'." +
            (string.IsNullOrWhiteSpace(note) ? "" : $"\n\n{note}");

        var intro = $"Your order <strong style=\"color:{TextDark};\">#{Enc(order.OrderNumber)}</strong> status has changed to:";

        var content = new StringBuilder();
        var stepper = StatusStepper(order.Status, Accent(business));
        content.Append(Row("0 40px 24px", stepper ?? StatusChipHtml(StatusLabel(order.Status), Accent(business))));

        if (!string.IsNullOrWhiteSpace(note))
        {
            content.Append(Row("0 40px 24px", $"<p style=\"margin:0;font-size:14px;line-height:1.6;color:{TextMuted};\">{Enc(note)}</p>"));
        }

        content.Append(Row("0 40px 6px", ItemsBlock(order.Items.Select(i => (i.ProductName, i.VariantSummary, i.Quantity, (decimal?)i.LineTotal)).ToList(), order.Currency)));
        content.Append(Row("8px 40px 36px", TotalsBlock(order)));

        return (subject, plain, Layout(business, subject, "Order update", StatusLabel(order.Status), intro, content.ToString()));
    }

    public static (string Subject, string PlainBody, string HtmlBody) OrderShipped(
        Business? business, Order order, string? carrierName, string? trackingNumber, string? trackingUrl)
    {
        var subject = $"Your order {order.OrderNumber} has shipped";
        var plain =
            $"Order {order.OrderNumber} is on its way." +
            (string.IsNullOrWhiteSpace(trackingNumber) ? "" : $"\nCarrier: {carrierName}\nTracking: {trackingNumber}") +
            (string.IsNullOrWhiteSpace(trackingUrl) ? "" : $"\n{trackingUrl}");

        var intro = $"Good news — order <strong style=\"color:{TextDark};\">#{Enc(order.OrderNumber)}</strong> is on its way to you.";

        var content = new StringBuilder();
        var stepper = StatusStepper(order.Status, Accent(business));
        if (stepper is not null)
        {
            content.Append(Row("0 40px 24px", stepper));
        }

        if (!string.IsNullOrWhiteSpace(trackingNumber))
        {
            content.Append(Row("0 40px 28px", InfoChip("Tracking",
            [
                ("Carrier", string.IsNullOrWhiteSpace(carrierName) ? "—" : carrierName!),
                ("Tracking number", trackingNumber!)
            ])));
        }

        if (!string.IsNullOrWhiteSpace(trackingUrl))
        {
            content.Append(Row("0 40px 36px", ButtonHtml("Track your package", trackingUrl!, Accent(business))));
        }

        content.Append(Row("0 40px 6px", ItemsBlock(order.Items.Select(i => (i.ProductName, i.VariantSummary, i.Quantity, (decimal?)i.LineTotal)).ToList(), order.Currency)));
        content.Append(Row("8px 40px 36px", TotalsBlock(order)));

        return (subject, plain, Layout(business, subject, "On its way", "Your order has shipped", intro, content.ToString()));
    }

    public static (string Subject, string PlainBody, string HtmlBody) ReturnDecision(Business? business, ReturnRequest entity, string? note)
    {
        var approved = entity.Status == Domain.Enums.ReturnStatus.Approved;
        var subject = $"Return {entity.RmaNumber} {(approved ? "approved" : "rejected")}";
        var plain = $"Your return request {entity.RmaNumber} was {entity.Status}." +
            (string.IsNullOrWhiteSpace(note) ? "" : $"\n\n{note}");

        var intro = $"Your return request <strong style=\"color:{TextDark};\">#{Enc(entity.RmaNumber)}</strong> was:";

        var content = new StringBuilder()
            .Append(Row("0 40px 24px", StatusChipHtml(entity.Status.ToString(), approved ? "#3d7a3d" : "#b0453a")));

        if (!string.IsNullOrWhiteSpace(note))
        {
            content.Append(Row("0 40px 24px", $"<p style=\"margin:0;font-size:14px;line-height:1.6;color:{TextMuted};\">{Enc(note)}</p>"));
        }

        content.Append(Row("0 40px 36px", ItemsBlock(entity.Items.Select(i => (i.ProductName, (string?)null, i.Quantity, (decimal?)i.LineRefund)).ToList(), entity.Currency)));

        return (subject, plain, Layout(business, subject, "Return update", $"Return {(approved ? "approved" : "rejected")}", intro, content.ToString()));
    }

    public static (string Subject, string PlainBody, string HtmlBody) RefundIssued(Business? business, ReturnRequest entity, decimal amount)
    {
        var subject = $"Refund issued for {entity.RmaNumber}";
        var plain = $"We've refunded {Money(amount, entity.Currency)} for order {entity.OrderNumber}.";

        var intro = $"We've refunded <strong style=\"color:{TextDark};\">{Money(amount, entity.Currency)}</strong> for order <strong style=\"color:{TextDark};\">#{Enc(entity.OrderNumber)}</strong>.";

        var content = new StringBuilder()
            .Append(Row("0 40px 24px", MutedHtml("Refunds can take a few business days to appear, depending on your bank or card issuer.")))
            .Append(Row("0 40px 36px", ItemsBlock(entity.Items.Select(i => (i.ProductName, (string?)null, i.Quantity, (decimal?)i.LineRefund)).ToList(), entity.Currency)))
            .ToString();

        return (subject, plain, Layout(business, subject, "Refund issued", "Refund issued", intro, content));
    }

    /// <summary>§9.49. No amount to report — a same-price exchange moved no money.</summary>
    public static (string Subject, string PlainBody, string HtmlBody) ExchangeProcessed(Business? business, ReturnRequest entity)
    {
        var subject = $"Exchange processed for {entity.RmaNumber}";
        var plain = $"Your exchange for order {entity.OrderNumber} has shipped.";

        var intro = $"Your exchange for order <strong style=\"color:{TextDark};\">#{Enc(entity.OrderNumber)}</strong> has shipped.";
        var content = Row("0 40px 36px", ItemsBlock(entity.Items.Select(i => (i.ProductName, (string?)null, i.Quantity, (decimal?)i.LineRefund)).ToList(), entity.Currency));

        return (subject, plain, Layout(business, subject, "Exchange processed", "Your exchange has shipped", intro, content));
    }

    public static (string Subject, string PlainBody, string HtmlBody) AbandonedCart(
        Business? business, string? recipientName, List<string> itemNames, int itemCount, string? shopLink, string? unsubscribeUrl)
    {
        const string subject = "You left something behind";
        var names = string.Join(", ", itemNames);
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hi there," : $"Hi {recipientName},";
        var plain = $"{greeting}\n\nYour cart still has {itemCount} item(s): {names}.";

        var intro = $"{Enc(greeting)} these are still waiting in your cart — complete your order before they're gone.";

        var rows = new StringBuilder();
        foreach (var itemName in itemNames)
        {
            rows.Append(SwatchRow(itemName, null, null));
        }

        if (itemCount > itemNames.Count)
        {
            rows.Append($"<p style=\"margin:10px 0 0;font-size:12px;color:{TextFaint};\">+{itemCount - itemNames.Count} more item(s) in your cart.</p>");
        }

        var content = new StringBuilder()
            .Append(Row("0 40px 8px", rows.ToString()));

        if (!string.IsNullOrWhiteSpace(shopLink))
        {
            content.Append(Row("24px 40px 36px", ButtonHtml("Finish checking out", shopLink!, Accent(business))));
        }
        else
        {
            content.Append(Row("0 40px 36px", string.Empty));
        }

        return (subject, plain, Layout(business, subject, "Still in your cart", "You left something behind", intro, content.ToString(), unsubscribeUrl));
    }

    public static (string Subject, string PlainBody, string HtmlBody) BackInStock(
        Business? business, string productName, string? productLink, string? unsubscribeUrl)
    {
        var subject = $"{productName} is back in stock";
        var plain = $"'{productName}' is available again." + (string.IsNullOrWhiteSpace(productLink) ? "" : $"\n{productLink}");

        var intro = $"Good news — <strong style=\"color:{TextDark};\">{Enc(productName)}</strong> is back in stock.";
        var content = string.IsNullOrWhiteSpace(productLink)
            ? string.Empty
            : Row("0 40px 36px", ButtonHtml("Shop now", productLink!, Accent(business)));

        return (subject, plain, Layout(business, subject, "Back in stock", "It's back!", intro, content, unsubscribeUrl));
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
            rows.Append(LabelValueRow(
                Enc(item.Name) + $" <span style=\"color:{TextFaint};\">({Enc(item.Sku)})</span>",
                $"{item.StockQuantity} left",
                item.ReorderThreshold is null ? null : $"reorder at {item.ReorderThreshold}"));
        }

        var content = Row("0 40px 36px", $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;\">{rows}</table>");
        var intro = "These products are at or below their reorder threshold:";

        return (subject, plain, Layout(business, subject, "Inventory alert", $"{items.Count} product(s) low on stock", intro, content));
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

        var intro = $"{Enc(greeting)} here's a code just for you — <strong style=\"color:{TextDark};\">{Enc(label)}</strong>: {Enc(description)}.";

        var content = new StringBuilder()
            .Append(Row("8px 40px 24px", CodeBoxHtml(code, Accent(business))));

        if (expiresAt is not null)
        {
            content.Append(Row("0 40px 36px", MutedHtml($"Valid until {expiresAt:dd MMM yyyy}.")));
        }
        else
        {
            content.Append(Row("0 40px 36px", string.Empty));
        }

        return (subject, plain, Layout(business, subject, "Just for you", "Here's a code, just for you", intro, content.ToString(), unsubscribeUrl));
    }

    public static (string Subject, string PlainBody, string HtmlBody) ReviewRequest(
        Business? business, string? recipientName, List<string> productNames, string? unsubscribeUrl)
    {
        const string subject = "How was your order?";
        var names = string.Join(", ", productNames);
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hi there," : $"Hi {recipientName},";
        var plain = $"{greeting}\n\nTell others what you thought of: {names}.";

        var intro = $"{Enc(greeting)} we'd love to hear what you thought of: <strong style=\"color:{TextDark};\">{Enc(names)}</strong>.";
        var content = Row("0 40px 36px", MutedHtml("A quick review helps other shoppers — and takes less than a minute."));

        return (subject, plain, Layout(business, subject, "How was it?", "How was your order?", intro, content, unsubscribeUrl));
    }

    // -----------------------------------------------------------------------------------------
    // Shared layout
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Same treatment applied two ways: as an actual &lt;span&gt; badge with the Business's first
    /// initial when it has no logo at all, and inlined onto the &lt;img&gt; tag's alt text when
    /// it does — most mail clients that block remote images still render styled alt text, so a
    /// shop with a logo degrades to a readable brand-name label rather than a blank box.
    /// </summary>
    private static string HeaderRow(Business? business)
    {
        var name = DisplayName(business);
        var accent = Accent(business);

        if (!string.IsNullOrWhiteSpace(business?.LogoUrl))
        {
            return
                $"<img src=\"{Enc(business!.LogoUrl)}\" alt=\"{Enc(name)}\" " +
                $"style=\"max-height:40px;max-width:200px;display:block;border:0;border-radius:8px;color:{TextDark};font-family:{HeadingFont};font-size:18px;\">";
        }

        return
            "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>" +
            $"<td style=\"width:40px;height:40px;background:{accent};border-radius:16px;text-align:center;vertical-align:middle;\">" +
            $"<span style=\"font-family:{HeadingFont};color:{PageBg};font-size:20px;line-height:40px;\">{Enc(Initial(name))}</span></td>" +
            $"<td style=\"padding-left:12px;font-family:{HeadingFont};font-size:20px;color:{TextDark};\">{Enc(name)}</td>" +
            "</tr></table>";
    }

    private const string MsoTableStyle = "<!--[if mso]><style>table {border-collapse:collapse;}</style><![endif]-->";

    private static string Layout(Business? business, string title, string eyebrow, string heading, string introHtml, string contentHtml, string? unsubscribeUrl = null)
    {
        var name = DisplayName(business);

        var footer = new StringBuilder();
        footer.Append($"<p style=\"margin:0 0 6px;font-size:11px;line-height:1.7;color:{TextFaint};\">").Append(Enc(name));
        if (business?.Address is { } addr && !string.IsNullOrWhiteSpace(addr.Line1))
        {
            footer.Append(" &middot; ").Append(Enc(addr.Line1)).Append(", ").Append(Enc(addr.City)).Append(' ').Append(Enc(addr.PostalCode));
        }
        footer.Append("</p>");

        if (!string.IsNullOrWhiteSpace(business?.ContactEmail))
        {
            footer.Append($"<p style=\"margin:0;font-size:11px;line-height:1.7;color:{TextFaint};\">Questions? ")
                .Append($"<a href=\"mailto:{Enc(business!.ContactEmail)}\" style=\"color:{Accent(business)};\">{Enc(business.ContactEmail)}</a></p>");
        }

        if (!string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            footer.Append($"<p style=\"margin:8px 0 0;font-size:11px;line-height:1.7;\">")
                .Append($"<a href=\"{Enc(unsubscribeUrl!)}\" style=\"color:{Accent(business)};\">Unsubscribe from these emails</a></p>");
        }

        return $"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light"><meta name="supported-color-schemes" content="light">
            <title>{Enc(title)} — {Enc(name)}</title>
            {MsoTableStyle}</head>
            <body style="margin:0;padding:0;background:{PageBg};color:{TextDark};font-family:{BodyFont};">
            <span style="display:none;font-size:1px;color:{PageBg};line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{Enc(title)}</span>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{PageBg};">
            <tr><td align="center" style="padding:32px 16px;">
            <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px;max-width:600px;background:{CardBg};border-radius:28px;">
            <tr><td style="padding:36px 40px 8px;">{HeaderRow(business)}</td></tr>
            <tr><td style="padding:28px 40px 0;">
            <p style="margin:0 0 10px;font-size:11px;letter-spacing:0.1em;text-transform:uppercase;color:{Accent(business)};">{Enc(eyebrow)}</p>
            <h1 style="margin:0 0 14px;font-family:{HeadingFont};font-weight:400;font-size:30px;line-height:1.15;color:{TextDark};">{Enc(heading)}</h1>
            <p style="margin:0 0 4px;font-size:15px;line-height:1.6;color:{TextDark};">{introHtml}</p>
            </td></tr>
            {contentHtml}
            </table>
            <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px;max-width:600px;">
            <tr><td style="padding:24px 40px;text-align:center;">{footer}</td></tr>
            </table>
            </td></tr></table></body></html>
            """;
    }

    // -----------------------------------------------------------------------------------------
    // Content blocks — each returns one or more complete <tr> rows to slot into Layout.
    // -----------------------------------------------------------------------------------------

    private static string Row(string padding, string innerHtml) =>
        $"<tr><td style=\"padding:{padding};\">{innerHtml}</td></tr>";

    private static string Divider() =>
        $"<tr><td style=\"padding:0 40px;\"><div style=\"height:1px;background:{DividerColor};\"></div></td></tr>";

    private static string ButtonHtml(string text, string url, string color) =>
        "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>" +
        $"<td bgcolor=\"{color}\" style=\"border-radius:16px;\">" +
        $"<a href=\"{Enc(url)}\" style=\"display:block;padding:14px 30px;font-family:{HeadingFont};font-size:15px;color:{PageBg};text-decoration:none;border-radius:16px;\">{Enc(text)}</a>" +
        "</td></tr></table>";

    private static string PlainLinkChip(string label, string url) =>
        $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:{ChipBg};border-radius:16px;\"><tr><td style=\"padding:16px 18px;\">" +
        $"<p style=\"margin:0;font-size:12px;line-height:1.6;color:{TextMuted};\">{Enc(label)}<br><a href=\"{Enc(url)}\" style=\"color:{TextDark};word-break:break-all;\">{Enc(url)}</a></p>" +
        "</td></tr></table>";

    private static string MutedHtml(string text) =>
        $"<p style=\"margin:0;font-size:12px;line-height:1.6;color:{TextFaint};\">{text}</p>";

    private static string StatusChipHtml(string status, string color) =>
        $"<span style=\"display:inline-block;padding:6px 14px;border-radius:999px;background:{color};color:#ffffff;font-size:13px;font-weight:600;\">{Enc(status)}</span>";

    private static string CodeBoxHtml(string code, string color) =>
        $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td style=\"border:2px dashed {color};border-radius:16px;padding:14px 24px;\">" +
        $"<span style=\"font-family:'SFMono-Regular',Consolas,monospace;font-size:20px;font-weight:700;letter-spacing:2px;color:{color};\">{Enc(code)}</span>" +
        "</td></tr></table>";

    /// <summary>One product row: a plain-colored swatch (no product images exist in the data model yet), name, optional variant/qty line, and an optional right-aligned price.</summary>
    private static string SwatchRow(string name, string? subline, string? priceText)
    {
        var price = priceText is null ? string.Empty : $"<td align=\"right\" style=\"font-size:14px;font-weight:600;white-space:nowrap;color:{TextDark};\">{Enc(priceText)}</td>";
        var subtitle = subline is null ? string.Empty : $"<p style=\"margin:2px 0 0;font-size:12px;color:{TextFaint};\">{Enc(subline)}</p>";

        return
            $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 12px;padding-top:12px;border-top:1px solid {DividerColor};\"><tr>" +
            $"<td style=\"width:56px;height:56px;background:{SwatchBg};border-radius:12px;\"></td>" +
            $"<td style=\"padding-left:14px;\"><p style=\"margin:0;font-size:14px;font-weight:600;color:{TextDark};\">{Enc(name)}</p>{subtitle}</td>" +
            $"{price}</tr></table>";
    }

    private static string ItemsBlock(List<(string Name, string? Sub, int Quantity, decimal? LineTotal)> items, string currency)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var sub = string.IsNullOrWhiteSpace(item.Sub) ? $"Qty {item.Quantity}" : $"{item.Sub} · Qty {item.Quantity}";
            sb.Append(SwatchRow(item.Name, sub, item.LineTotal is null ? null : Money(item.LineTotal.Value, currency)));
        }

        return sb.ToString();
    }

    private static string TotalsBlock(Order order)
    {
        var sb = new StringBuilder()
            .Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-top:1px solid {DividerColor};padding-top:8px;\">")
            .Append(TotalLine("Subtotal", Money(order.Subtotal, order.Currency), false));

        if (order.DiscountAmount > 0)
        {
            sb.Append(TotalLine("Discount", $"-{Money(order.DiscountAmount, order.Currency)}", false));
        }

        sb.Append(TotalLine("Delivery", Money(order.DeliveryFee, order.Currency), false));

        if (order.TaxAmount > 0)
        {
            sb.Append(TotalLine("Tax", Money(order.TaxAmount, order.Currency), false));
        }

        sb.Append(TotalLine("Total", Money(order.Total, order.Currency), true));
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string TotalLine(string label, string value, bool emphasize)
    {
        var size = emphasize ? "15px" : "13px";
        var weight = emphasize ? "700" : "400";
        var color = emphasize ? TextDark : TextMuted;
        var pad = emphasize ? "10px 0 0" : "4px 0";
        return $"<tr><td style=\"padding:{pad};font-size:{size};font-weight:{weight};color:{color};\">{Enc(label)}</td>" +
               $"<td align=\"right\" style=\"padding:{pad};font-size:{size};font-weight:{weight};color:{color};\">{Enc(value)}</td></tr>";
    }

    private static string ShippingChipRow(Order order)
    {
        if (order.ShippingAddress is not { } addr || string.IsNullOrWhiteSpace(addr.Line1))
        {
            return string.Empty;
        }

        var line = string.Join(", ", new[] { addr.Line1, addr.Line2, addr.City, addr.PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return Row("28px 40px 0", InfoChip("Delivering to", [("", line)]));
    }

    /// <summary>A tan rounded box holding one uppercase-label heading plus one or more label/value lines — the "Estimated arrival" / "Delivering to" treatment.</summary>
    private static string InfoChip(string title, IEnumerable<(string Label, string Value)> lines)
    {
        var sb = new StringBuilder($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:{ChipBg};border-radius:16px;\"><tr><td style=\"padding:16px 18px;\">")
            .Append($"<p style=\"margin:0 0 6px;font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:{TextMuted};\">{Enc(title)}</p>");

        foreach (var (label, value) in lines)
        {
            sb.Append(string.IsNullOrEmpty(label)
                ? $"<p style=\"margin:0;font-size:13px;line-height:1.6;color:{TextDark};\">{Enc(value)}</p>"
                : $"<p style=\"margin:0 0 2px;font-size:13px;line-height:1.6;color:{TextDark};\">{Enc(label)}: {Enc(value)}</p>");
        }

        sb.Append("</td></tr></table>");
        return sb.ToString();
    }

    private static string LabelValueRow(string leftHtml, string middle, string? right)
    {
        var sb = new StringBuilder("<tr>")
            .Append($"<td style=\"padding:8px 0;border-bottom:1px solid {DividerColor};font-size:14px;color:{TextDark};\">").Append(leftHtml).Append("</td>")
            .Append($"<td style=\"padding:8px 0;border-bottom:1px solid {DividerColor};font-size:14px;color:{TextMuted};text-align:right;white-space:nowrap;\">").Append(Enc(middle)).Append("</td>");

        if (right is not null)
        {
            sb.Append($"<td style=\"padding:8px 0;border-bottom:1px solid {DividerColor};font-size:12px;color:{TextFaint};text-align:right;white-space:nowrap;\">").Append(Enc(right)).Append("</td>");
        }

        sb.Append("</tr>");
        return sb.ToString();
    }

    /// <summary>4-step Processing → Confirmed → Out-for-delivery/Ready-for-pickup → Delivered/Picked-up tracker. Null for statuses with no place on that path (PendingPayment, Cancelled, Refunded) — callers fall back to <see cref="StatusChipHtml"/> for those.</summary>
    private static string? StatusStepper(Domain.Enums.OrderStatus status, string accent)
    {
        (Domain.Enums.OrderStatus Status, string Label)[] path = status is Domain.Enums.OrderStatus.AwaitingPickup or Domain.Enums.OrderStatus.PickedUp
            ?
            [
                (Domain.Enums.OrderStatus.Processing, "Processing"),
                (Domain.Enums.OrderStatus.Confirmed, "Confirmed"),
                (Domain.Enums.OrderStatus.AwaitingPickup, "Ready for pickup"),
                (Domain.Enums.OrderStatus.PickedUp, "Picked up")
            ]
            :
            [
                (Domain.Enums.OrderStatus.Processing, "Processing"),
                (Domain.Enums.OrderStatus.Confirmed, "Confirmed"),
                (Domain.Enums.OrderStatus.OutForDelivery, "Out for delivery"),
                (Domain.Enums.OrderStatus.Delivered, "Delivered")
            ];

        var index = Array.FindIndex(path, p => p.Status == status);
        if (index < 0)
        {
            return null;
        }

        var cells = new StringBuilder();
        for (var i = 0; i < path.Length; i++)
        {
            var (completed, active) = (i < index, i == index);
            var circleBg = completed || active ? accent : DividerColor;
            var circleColor = completed || active ? PageBg : TextFaint;
            var glyph = completed ? "&#10003;" : active ? "&#9679;" : (i + 1).ToString();
            var labelWeight = active ? "700" : "600";
            var labelColor = active || completed ? TextDark : TextFaint;

            cells.Append($"<td width=\"25%\" align=\"center\">")
                .Append($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"center\"><tr><td style=\"width:24px;height:24px;background:{circleBg};border-radius:12px;color:{circleColor};font-size:12px;text-align:center;line-height:24px;font-weight:700;\">{glyph}</td></tr></table>")
                .Append($"<p style=\"margin:6px 0 0;font-size:10px;color:{labelColor};font-weight:{labelWeight};\">{Enc(path[i].Label)}</p>")
                .Append("</td>");
        }

        return $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>{cells}</tr></table>";
    }

    // -----------------------------------------------------------------------------------------
    // Small helpers
    // -----------------------------------------------------------------------------------------

    private static string DisplayName(Business? business) =>
        string.IsNullOrWhiteSpace(business?.Name) ? "Vastora" : business.Name;

    private static string Accent(Business? business) =>
        business is null ? DefaultAccent : (string.IsNullOrWhiteSpace(business.ThemeColor) ? DefaultAccent : business.ThemeColor);

    private static string Article(string name) =>
        name.Length > 0 && "AEIOUaeiou".IndexOf(name[0]) >= 0 ? "an" : "a";

    private static string Initial(string name) =>
        name.Length == 0 ? "V" : char.ToUpperInvariant(name[0]).ToString();

    private static string StatusLabel(Domain.Enums.OrderStatus status) => status switch
    {
        Domain.Enums.OrderStatus.PendingPayment => "Pending payment",
        Domain.Enums.OrderStatus.OutForDelivery => "Out for delivery",
        Domain.Enums.OrderStatus.AwaitingPickup => "Ready for pickup",
        Domain.Enums.OrderStatus.PickedUp => "Picked up",
        _ => status.ToString()
    };

    private static string Money(decimal amount, string currency) =>
        amount.ToString("0.00") + " " + Enc(currency);

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

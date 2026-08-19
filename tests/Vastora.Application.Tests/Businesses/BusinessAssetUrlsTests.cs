using Vastora.Application.Businesses;
using Vastora.Domain.Entities;
using Xunit;

namespace Vastora.Application.Tests.Businesses;

public class BusinessAssetUrlsTests
{
    [Fact]
    public void ResolveLogo_RelativeUrl_PrefixesWithPlatformBaseUrl()
    {
        var business = new Business { LogoUrl = "/uploads/biz-1/logo.png" };

        var resolved = BusinessAssetUrls.ResolveLogo(business, "https://api.vastora.app");

        Assert.Equal("https://api.vastora.app/uploads/biz-1/logo.png", resolved!.LogoUrl);
    }

    [Fact]
    public void ResolveLogo_BusinessHasShopDomain_PrefersItOverPlatformBaseUrl()
    {
        var business = new Business { LogoUrl = "/uploads/biz-1/logo.png", ShopDomain = "antivaly.com" };

        var resolved = BusinessAssetUrls.ResolveLogo(business, "https://api.vastora.app");

        Assert.Equal("https://antivaly.com/uploads/biz-1/logo.png", resolved!.LogoUrl);
    }

    [Fact]
    public void ResolveLogo_ShopDomainWithoutScheme_DefaultsToHttps()
    {
        var business = new Business { LogoUrl = "/uploads/biz-1/logo.png", ShopDomain = "http://antivaly.com" };

        var resolved = BusinessAssetUrls.ResolveLogo(business, "https://api.vastora.app");

        Assert.Equal("http://antivaly.com/uploads/biz-1/logo.png", resolved!.LogoUrl);
    }

    [Fact]
    public void ResolveLogo_AlreadyAbsoluteUrl_LeftUnchanged()
    {
        var business = new Business { LogoUrl = "https://cdn.example.com/logo.png", ShopDomain = "antivaly.com" };

        var resolved = BusinessAssetUrls.ResolveLogo(business, "https://api.vastora.app");

        Assert.Equal("https://cdn.example.com/logo.png", resolved!.LogoUrl);
    }

    [Fact]
    public void ResolveLogo_NullBusiness_ReturnsNull()
    {
        Assert.Null(BusinessAssetUrls.ResolveLogo(null, "https://api.vastora.app"));
    }

    [Fact]
    public void ResolveLogo_EmptyLogoUrl_LeftUnchanged()
    {
        var business = new Business { LogoUrl = "" };

        var resolved = BusinessAssetUrls.ResolveLogo(business, "https://api.vastora.app");

        Assert.Equal("", resolved!.LogoUrl);
    }

    [Fact]
    public void ResolveLogo_TrailingSlashOnBaseUrl_DoesNotDoubleSlash()
    {
        var business = new Business { LogoUrl = "/uploads/biz-1/logo.png" };

        var resolved = BusinessAssetUrls.ResolveLogo(business, "https://api.vastora.app/");

        Assert.Equal("https://api.vastora.app/uploads/biz-1/logo.png", resolved!.LogoUrl);
    }
}

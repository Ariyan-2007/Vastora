using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Vastora.Application.Accounting;
using Vastora.Application.Analytics;
using Vastora.Application.ApiKeys;
using Vastora.Application.Auth;
using Vastora.Application.Businesses;
using Vastora.Application.Cart;
using Vastora.Application.Categories;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Content;
using Vastora.Application.Coupons;
using Vastora.Application.CustomerGroups;
using Vastora.Application.DeliveryAgents;
using Vastora.Application.GiftCards;
using Vastora.Application.Inventory;
using Vastora.Application.Notifications;
using Vastora.Application.Orders;
using Vastora.Application.Pricing;
using Vastora.Application.Privacy;
using Vastora.Application.Products;
using Vastora.Application.Promotions;
using Vastora.Application.Returns;
using Vastora.Application.Reviews;
using Vastora.Application.Shipping;
using Vastora.Application.Tax;
using Vastora.Application.Tenants;
using Vastora.Application.Users;
using Vastora.Application.Webhooks;
using Vastora.Application.Wishlists;

namespace Vastora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IAuthTokenIssuer, AuthTokenIssuer>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IBusinessService, BusinessService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IDeliveryAgentService, DeliveryAgentService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IAccountingService, AccountingService>();

        // §9B — commerce-completeness services.
        services.AddScoped<ITaxService, TaxService>();
        services.AddScoped<IShippingService, ShippingService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<ICustomerGroupService, CustomerGroupService>();
        services.AddScoped<IGiftCardService, GiftCardService>();
        services.AddScoped<IStoreCreditService, StoreCreditService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IWebhookService, WebhookService>();
        services.AddScoped<IPrivacyService, PrivacyService>();
        services.AddScoped<ILifecycleNotificationService, LifecycleNotificationService>();

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}

using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Vastora.Application.Auth;
using Vastora.Application.Businesses;
using Vastora.Application.Cart;
using Vastora.Application.Categories;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Application.DeliveryAgents;
using Vastora.Application.Orders;
using Vastora.Application.Products;
using Vastora.Application.Tenants;
using Vastora.Application.Users;

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

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}

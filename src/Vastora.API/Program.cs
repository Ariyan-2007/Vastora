using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Vastora.API.Authorization;
using Vastora.API.BackgroundJobs;
using Vastora.API.Filters;
using Vastora.API.Middleware;
using Vastora.Application;
using Vastora.Infrastructure;
using Vastora.Infrastructure.Identity;
using Vastora.Infrastructure.Persistence;

// Load .env from the repo root (or any parent of the working directory) before configuration
// is built, so a single MONGODB_URI / MongoDb__ConnectionString etc. just works locally.
try
{
    DotNetEnv.Env.TraversePath().Load();
}
catch (FileNotFoundException)
{
    // No .env file found — fine in containers/production where real env vars are set instead.
}

var builder = WebApplication.CreateBuilder(args);

// Structured logging (§9.11) — console sink only, since no external log aggregator (Seq/ELK/
// Datadog/...) is configured yet; swap/add sinks here once one exists to point at.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Friendly aliases: accept a plain MONGODB_URI / MONGODB_CONNECTION_STRING env var in addition
// to the strict MongoDb__ConnectionString double-underscore form ASP.NET Core expects.
// appsettings.json ships an empty "ConnectionString" placeholder, so treat blank as unset too.
static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

var mongoConnectionString = NullIfBlank(builder.Configuration["MongoDb:ConnectionString"])
    ?? Environment.GetEnvironmentVariable("MONGODB_URI")
    ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
if (!string.IsNullOrWhiteSpace(mongoConnectionString))
{
    builder.Configuration["MongoDb:ConnectionString"] = mongoConnectionString;
}

builder.Services.AddControllers(options =>
    {
        // Auto-runs the registered FluentValidation IValidator<T> (if any) for every action
        // argument before the action body executes — see ValidationActionFilter for the "why".
        options.Filters.Add<ValidationActionFilter>();

        // §9.35 — records every mutating request centrally, so a new endpoint can't be added
        // without an audit trail. Ordered after validation so rejected requests aren't logged
        // as if they had done something.
        options.Filters.Add<AuditLogFilter>();
    })
    .AddJsonOptions(options =>
    {
        // Enums cross the wire as readable names ("TenantOwner") instead of raw ints (2) —
        // both in responses and in request bodies (e.g. the bare-enum PATCH .../status endpoints).
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Vastora API",
        Version = "v1",
        Description = "Multi-tenant e-commerce platform API — Platform, SuperOffice, BackOffice and Shop endpoints. " +
                      "See VASTORA_BLUEPRINT.md and docs/*_BLUEPRINT.md in the repo for full context."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by /api/auth/login or /api/shop/{businessSlug}/auth/login."
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", null)] = []
    });

    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlFile))
    {
        options.IncludeXmlComments(xmlFile);
    }
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthorizationHandler, BusinessAccessAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    // Resource-based scoping for every {businessId}-rooted route — see
    // BusinessAccessAuthorizationHandler for exactly what each role is allowed to touch.
    options.AddPolicy("BusinessMember", policy => policy.Requirements.Add(new BusinessMemberRequirement()));
});

const string CorsPolicy = "VastoraCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            // Real client origins configured (§9.11) — restrict to exactly those.
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            // Cors:AllowedOrigins isn't set — wide open, fine for foundation-phase API
            // development against Swagger/Postman. Set it in config before real client traffic.
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

// A generous global limit (§9.11) — this is abuse protection, not a business-tier throttle;
// SubscriptionPlanLimits (§9.9) already governs the things that actually matter per plan.
//
// §9.40 narrows it in one place that needed it: the auth routes. Login, registration, password
// reset and email verification are credential-guessing surfaces, and giving them the same 100/min
// budget as catalog browsing meant an attacker got 100 password attempts a minute per IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = httpContext.Request.Path.Value ?? string.Empty;

        var isCredentialEndpoint =
            path.Contains("/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/auth/register", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/auth/forgot-password", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/auth/reset-password", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/auth/verify-email", StringComparison.OrdinalIgnoreCase);

        return isCredentialEndpoint
            ? RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"auth:{clientIp}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })
            : RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: clientIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
    });
});

// §9.36 — abandoned-cart, back-in-stock, low-stock and review-request sweeps.
builder.Services.AddHostedService<LifecycleNotificationWorker>();

builder.Services.AddHealthChecks()
    .AddCheck<MongoHealthCheck>("mongodb");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.RunAsync();
}

app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors(CorsPolicy);

app.UseRateLimiter();

app.MapHealthChecks("/health");

// Serves whatever LocalFileStorageService wrote (§9.5, product image uploads) — physical path
// must match LocalFileStorageService's UploadsRoot exactly. Public, no auth: product images are
// meant to be publicly viewable, same as any other product data on the public Shop.
var uploadsPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();

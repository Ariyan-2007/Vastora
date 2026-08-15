using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Vastora.API.Authorization;
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
builder.Services.AddCors(options =>
{
    // Wide open for foundation-phase API development; tighten to real client origins before launch.
    options.AddPolicy(CorsPolicy, policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.RunAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors(CorsPolicy);

app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();

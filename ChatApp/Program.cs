using ChatApp.Realtime;
using ChatApp.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ChatApp.Common;
using ChatApp.Data;
using ChatApp.Hubs;
using ChatApp.Options;
using ChatApp.Presence;
using ChatApp.Services;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var envFilePaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "ChatApp", ".env")
};

foreach (var envFilePath in (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ? envFilePaths.Distinct() : []))
{
    if (File.Exists(envFilePath))
    {
        DotNetEnv.Env.Load(envFilePath);
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);

// ---------- Options ----------
builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey) && Encoding.UTF8.GetByteCount(o.SecretKey) >= 32, "JWT key must contain at least 32 bytes")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience) && o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0, "Invalid JWT options")
    .ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var address in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<HubRateLimitFilter>();
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<AccountLockoutOptions>(builder.Configuration.GetSection(AccountLockoutOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<AppUrlsOptions>(builder.Configuration.GetSection(AppUrlsOptions.SectionName));

var rateLimitOptions = builder.Configuration
    .GetSection(RateLimitingOptions.SectionName)
    .Get<RateLimitingOptions>() ?? new RateLimitingOptions();
builder.Services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName)
    .Validate(o => new[] { o.Login, o.Register, o.RefreshToken, o.ForgotPassword, o.ResetPassword,
        o.GoogleLogin, o.ResolveFriendLink, o.GetMessages }.All(r => r.PermitLimit > 0 && r.WindowSeconds > 0),
        "Rate limits must have positive limits and windows")
    .ValidateOnStart();

// ---------- DbContext ----------
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
            .UseSnakeCaseNamingConvention(); // Chuyển tên table/column sang snake_case, ví dụ: UserName -> user_name
});

// ---------- Services ----------
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IFriendLinkService, FriendLinkService>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IBlockService, BlockService>();

builder.Services.AddChatSignalR(builder.Configuration);
var runtimeRole = builder.Services.AddChatRuntime(builder.Configuration);
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ---------- CORS ----------
// Cho phép frontend (Vite dev server) gọi API kèm cookie/credentials nếu cần.
// Production: đổi origin sang domain thật của frontend đã deploy.
var frontendOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:5173" };

if (builder.Environment.IsProduction())
{
    if (frontendOrigins.Length == 0 || frontendOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
        throw new InvalidOperationException("Production requires explicit HTTPS frontend origins.");
    builder.Services.AddOptions<SmtpOptions>().BindConfiguration(SmtpOptions.SectionName)
        .Validate(o => !string.IsNullOrWhiteSpace(o.Host) && o.Port is > 0 and <= 65535 && !string.IsNullOrWhiteSpace(o.FromEmail), "SMTP configuration is required")
        .ValidateOnStart();
    builder.Services.AddOptions<AppUrlsOptions>().BindConfiguration(AppUrlsOptions.SectionName)
        .Validate(o => Uri.TryCreate(o.FrontendBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https", "HTTPS frontend URL is required")
        .ValidateOnStart();
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(frontendOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ---------- Controllers / Swagger ----------
builder.Services.AddControllers().AddJsonOptions(options =>
{
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Nhập access token, KHÔNG cần tiền tố 'Bearer '"
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ---------- Authentication (JWT Bearer) ----------
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSecret = jwtSection["SecretKey"] ?? throw new InvalidOperationException("JWT secret is required.");
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            if (!Guid.TryParse(id, out var userId) || !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.DeletedAt == null, context.HttpContext.RequestAborted))
                context.Fail("Account unavailable");
        },
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs/chat"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// ---------- Authorization ----------
builder.Services.AddAuthorization(options =>
{
    // Ví dụ policy: bắt buộc email đã verify mới được thao tác 1 số action nhạy cảm
    options.AddPolicy("RequireVerifiedUser", policy =>
        policy.RequireClaim("email_verified", "true"));
});

// ---------- Rate Limiting ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"message\":\"Bạn thao tác quá nhiều lần, vui lòng thử lại sau ít phút.\"}",
            cancellationToken);
    };

    string PartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    options.AddPolicy("group-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? PartitionKey(context), _ =>
        new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("group-join", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? PartitionKey(context), _ =>
        new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("chat-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? PartitionKey(context), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    options.AddPolicy(RateLimitPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.Login.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.Login.WindowSeconds),
            QueueLimit = 0
        }));

    options.AddPolicy(RateLimitPolicies.Register, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.Register.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.Register.WindowSeconds),
            QueueLimit = 0
        }));

    options.AddPolicy(RateLimitPolicies.RefreshToken, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.RefreshToken.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.RefreshToken.WindowSeconds),
            QueueLimit = 0
        }));

    options.AddPolicy(RateLimitPolicies.ForgotPassword, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.ForgotPassword.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.ForgotPassword.WindowSeconds),
            QueueLimit = 0
        }));

    options.AddPolicy(RateLimitPolicies.ResetPassword, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.ResetPassword.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.ResetPassword.WindowSeconds),
            QueueLimit = 0
        }));

    options.AddPolicy(RateLimitPolicies.GoogleLogin, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.GoogleLogin.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.GoogleLogin.WindowSeconds),
            QueueLimit = 0
        }));

    options.AddPolicy(RateLimitPolicies.ResolveFriendLink, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.ResolveFriendLink.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.ResolveFriendLink.WindowSeconds),
            QueueLimit = 0
        }));

    options.AddPolicy(RateLimitPolicies.GetMessages, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.GetMessages.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.GetMessages.WindowSeconds),
            QueueLimit = 0
        }));
});

var app = builder.Build();
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("DefaultConnection")))
    throw new InvalidOperationException("Database connection is required.");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
if (runtimeRole == "worker")
{
    // The worker listens only for health probes; it exposes no API, Swagger, or client hub endpoints.
    app.Run();
    return;
}
app.UseForwardedHeaders();

// ---------- Global exception handling ----------
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (AppException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = ex.StatusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message = ex.Message, traceId = context.TraceIdentifier });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled request error {TraceId} {Method} {Path}", context.TraceIdentifier, context.Request.Method, context.Request.Path);
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message = "Đã có lỗi xảy ra, vui lòng thử lại sau.", traceId = context.TraceIdentifier });
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());

app.Use(async (context, next) =>
{
    // check load balancer
    var instance = $"{Environment.MachineName}:{context.Connection.LocalPort}";

    context.Response.Headers["X-Server-Instance"] = instance;

    app.Logger.LogInformation(
        "Instance {Instance} handling {Method} {Path}",
        instance,
        context.Request.Method,
        context.Request.Path);

    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] =
        "strict-origin-when-cross-origin";

    context.Response.Headers["Permissions-Policy"] =
        "geolocation=(), microphone=(self), camera=(self)";

    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self';";

    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("FrontendPolicy");

app.UseAuthentication();
if (app.Configuration.GetValue<bool>("Redis:Enabled")) app.UseMiddleware<RedisRateLimitMiddleware>();
else app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true);

app.Run();

public partial class Program { }

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

foreach (var envFilePath in envFilePaths.Distinct())
{
    if (File.Exists(envFilePath))
    {
        DotNetEnv.Env.Load(envFilePath);
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);

// ---------- Options ----------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<AccountLockoutOptions>(builder.Configuration.GetSection(AccountLockoutOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<AppUrlsOptions>(builder.Configuration.GetSection(AppUrlsOptions.SectionName));

var rateLimitOptions = builder.Configuration
    .GetSection(RateLimitingOptions.SectionName)
    .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

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
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IFriendLinkService, FriendLinkService>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IBlockService, BlockService>();
builder.Services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();

builder.Services.AddSignalR();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ---------- CORS ----------
// Cho phép frontend (Vite dev server) gọi API kèm cookie/credentials nếu cần.
// Production: đổi origin sang domain thật của frontend đã deploy.
var frontendOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:5173" };

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
var jwtSecret = jwtSection["SecretKey"]!;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
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

// ---------- Global exception handling ----------
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (AppException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
    catch (Exception)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message = "Đã có lỗi xảy ra, vui lòng thử lại sau." });
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
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

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

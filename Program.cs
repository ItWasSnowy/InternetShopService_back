using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Calls;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Jwt;
using InternetShopService_back.Infrastructure.Notifications;
using InternetShopService_back.Middleware;
using InternetShopService_back.Infrastructure.Sync;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Modules.UserCabinet.Services;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using InternetShopService_back.Modules.OrderManagement.Services;
using InternetShopService_back.Shared.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Internet Shop Service API",
        Version = "v1",
        Description = "API для управления кабинетами пользователей интернет-магазина"
    });

    // Добавляем поддержку JWT авторизации в Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.\n\nExample: \"Bearer 12345abcdef\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
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

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));
}

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey не настроен");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// HttpClient for external APIs
builder.Services.AddHttpClient<ZvonokCallService>();

// Repositories
builder.Services.AddScoped<IUserAccountRepository, UserAccountRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<ICounterpartyRepository, CounterpartyRepository>();
builder.Services.AddScoped<IShopRepository, ShopRepository>();
builder.Services.AddScoped<IDeliveryAddressRepository, DeliveryAddressRepository>();
builder.Services.AddScoped<ICargoReceiverRepository, CargoReceiverRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Infrastructure services
builder.Services.AddHttpContextAccessor(); // Для доступа к HttpContext в сервисах
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<IFimBizGrpcClient, FimBizGrpcClient>(); // Singleton для переиспользования канала

// Call service (Zvonok API)
var callProvider = builder.Configuration.GetValue<string>("CallsConfiguration:Provider")?.ToLower();
switch (callProvider)
{
    case "zvonokapi":
        builder.Services.AddScoped<ICallService, ZvonokCallService>();
        break;
    default:
        // Fallback на mock или заглушку
        throw new ArgumentException($"Неподдерживаемый провайдер звонков: {callProvider}");
}

// Business services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICounterpartyService, CounterpartyService>();
builder.Services.AddScoped<IDeliveryAddressService, DeliveryAddressService>();
builder.Services.AddScoped<ICargoReceiverService, CargoReceiverService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderStatusService, OrderStatusService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IPdfGenerationService, PdfGenerationService>();

// AutoMapper
builder.Services.AddAutoMapper(typeof(Program));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Background services
var enableAutoSync = builder.Configuration.GetValue<bool>("FimBiz:EnableAutoSync", true);
if (enableAutoSync)
{
    builder.Services.AddHostedService<FimBizSyncService>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.UseUserContext();
app.MapControllers();

app.Run();

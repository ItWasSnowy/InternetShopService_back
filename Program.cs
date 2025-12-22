using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Grpc.AspNetCore;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Calls;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Jwt;
using InternetShopService_back.Infrastructure.Notifications;
using InternetShopService_back.Infrastructure.Serialization;
using InternetShopService_back.Middleware;
using InternetShopService_back.Infrastructure.Sync;
using InternetShopService_back.Infrastructure.SignalR;
using OrderSyncService = InternetShopService_back.Infrastructure.Sync.OrderSyncService;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Modules.UserCabinet.Services;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using InternetShopService_back.Modules.OrderManagement.Services;
using InternetShopService_back.Shared.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Настройка сериализации DateTime в UTC формате ISO 8601
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        
        // Конвертер для DateTime - всегда сериализует в UTC ISO 8601 формате (с Z в конце)
        // Пример: "2024-01-15T10:30:00.000Z"
        options.JsonSerializerOptions.Converters.Add(new JsonConverterForDateTimeUtc());
        
        // Конвертер для DateTime? (nullable) - всегда сериализует в UTC ISO 8601 формате (с Z в конце)
        options.JsonSerializerOptions.Converters.Add(new JsonConverterForNullableDateTimeUtc());
    });

// Добавляем поддержку forwarded headers для работы за прокси (IIS)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Разрешаем все прокси (для IIS)
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddGrpc(); // Добавляем поддержку gRPC сервера
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

    // Важно для SignalR: позволяет передавать JWT в query string как access_token
    // (актуально для WebSocket/SSE подключения)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        // Проверка активности сессии в БД при каждом запросе
        OnTokenValidated = async context =>
        {
            // Получаем токен.
            // Для обычных HTTP запросов он приходит в Authorization header,
            // для SignalR (WebSocket/SSE) часто приходит в query string как access_token.
            var tokenString = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                tokenString = context.Request.Query["access_token"].ToString();
            }
            if (string.IsNullOrEmpty(tokenString))
            {
                context.Fail("Токен не предоставлен");
                return;
            }

            // Получаем сервисы из DI
            var serviceProvider = context.HttpContext.RequestServices;
            var sessionRepository = serviceProvider.GetRequiredService<ISessionRepository>();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Проверяем активность сессии в БД
            var session = await sessionRepository.GetByAccessTokenAsync(tokenString);
            if (session == null)
            {
                logger.LogWarning("Сессия не найдена для токена");
                context.Fail("Сессия не найдена или деактивирована");
                return;
            }

            if (!session.IsActive)
            {
                logger.LogWarning("Сессия {SessionId} деактивирована", session.Id);
                context.Fail("Сессия деактивирована");
                return;
            }

            if (session.ExpiresAt <= DateTime.UtcNow)
            {
                logger.LogWarning("Сессия {SessionId} истекла", session.Id);
                context.Fail("Сессия истекла");
                return;
            }

            // Токен валиден, сессия активна - доступ разрешен
        }
    };

});

// SignalR
builder.Services
    .AddSignalR(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(3);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(10);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    })
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Изолированные настройки SignalR для ShopHub
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions<ShopHub>>(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(8);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(20);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<ShopConnectionManager>();
builder.Services.AddSingleton<IShopNotificationService, ShopNotificationService>();

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
    builder.Services.AddScoped<IOrderCommentRepository, OrderCommentRepository>();

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
builder.Services.AddScoped<FimBizSessionService>();
builder.Services.AddScoped<SessionControlService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICounterpartyService, CounterpartyService>();
builder.Services.AddScoped<IDeliveryAddressService, DeliveryAddressService>();
builder.Services.AddScoped<ICargoReceiverService, CargoReceiverService>();
    builder.Services.AddScoped<IOrderService, OrderService>();
    builder.Services.AddScoped<IOrderStatusService, OrderStatusService>();
    builder.Services.AddScoped<IInvoiceService, InvoiceService>();
    builder.Services.AddScoped<IPdfGenerationService, PdfGenerationService>();
    builder.Services.AddScoped<IOrderCommentService, OrderCommentService>();

// AutoMapper
builder.Services.AddAutoMapper(typeof(Program));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? new[]
            {
                "https://hydrolan.fimbiz.ru"
                "https://test.fimbiz.ru",
                "https://tdapi.fimbiz.ru",
                "http://localhost:5000",
                "http://localhost:5173",
                "http://localhost:3000"
            };

        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Background services
var enableAutoSync = builder.Configuration.GetValue<bool>("FimBiz:EnableAutoSync", true);
if (enableAutoSync)
{
    builder.Services.AddHostedService<FimBizSyncService>();
    builder.Services.AddHostedService<OrderSyncService>();
}

// Настройка Kestrel для поддержки HTTP/2 (требуется для gRPC)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        // Включаем поддержку HTTP/2 для gRPC
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

var app = builder.Build();

// Автоматическое создание БД и применение миграций при запуске
if (!string.IsNullOrEmpty(connectionString))
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var dbContext = services.GetRequiredService<ApplicationDbContext>();
            var logger = services.GetRequiredService<ILogger<Program>>();
            
            // Проверяем, существует ли база данных
            if (!dbContext.Database.CanConnect())
            {
                logger.LogInformation("База данных не существует. Создание базы данных и применение миграций...");
                dbContext.Database.Migrate();
                logger.LogInformation("База данных успешно создана и миграции применены.");
            }
            else
            {
                logger.LogInformation("База данных существует. Проверка и применение миграций...");
                dbContext.Database.Migrate();
                logger.LogInformation("Миграции применены успешно.");
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000")
        {
            // База данных не существует - создаем через миграции
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("База данных не найдена. Попытка создания через миграции...");
            
            try
            {
                var dbContext = services.GetRequiredService<ApplicationDbContext>();
                dbContext.Database.Migrate();
                logger.LogInformation("База данных успешно создана через миграции.");
            }
            catch (Exception migrateEx)
            {
                logger.LogError(migrateEx, "Ошибка при создании базы данных через миграции. Убедитесь, что PostgreSQL запущен и у пользователя есть права на создание баз данных.");
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Произошла ошибка при применении миграций базы данных. Приложение продолжит работу, но некоторые функции могут быть недоступны.");
            // Не прерываем запуск приложения, просто логируем ошибку
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ВАЖНО: ForwardedHeaders должен быть ПЕРЕД UseHttpsRedirection
app.UseForwardedHeaders();

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// Статические файлы ДО аутентификации - чтобы они были доступны без токена
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot")),
    RequestPath = "", // Файлы доступны по корневому пути
    OnPrepareResponse = ctx =>
    {
        // Устанавливаем заголовки для кеширования
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=3600");
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.UseUserContext();

// Map gRPC service
app.MapGrpcService<ContractorSyncGrpcService>();
app.MapGrpcService<OrderSyncGrpcService>();
app.MapGrpcService<OrderCommentSyncGrpcService>();

// SignalR hubs
app.MapHub<ShopHub>("/shophub");

app.MapControllers();

app.Run();

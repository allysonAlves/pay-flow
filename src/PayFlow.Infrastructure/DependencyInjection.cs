using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayFlow.Application.Interfaces;
using PayFlow.Domain.Interfaces;
using PayFlow.Infrastructure.Gateway;
using PayFlow.Infrastructure.Idempotency;
using PayFlow.Infrastructure.Notifications;
using PayFlow.Infrastructure.Outbox;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Persistence.Repositories;
using StackExchange.Redis;

namespace PayFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")!));

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentReadRepository, PaymentReadRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxPublisher, OutboxPublisher>();
        services.AddScoped<IIdempotencyService, RedisIdempotencyService>();

        services.AddScoped<IMerchantNotifier, FakeMerchantNotifier>();
        services.AddScoped<ICustomerNotifier, FakeCustomerNotifier>();

        services.AddScoped<IOutboxStore, EfOutboxStore>();
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.AddHostedService<OutboxProcessor>();

        services.Configure<FakeGatewayOptions>(configuration.GetSection("FakeGateway"));
        services.AddScoped<IPaymentGateway, FakePaymentGateway>();

        // Registra INotificationHandlers definidos em Infrastructure
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddHealthChecks()
            .AddDbContextCheck<PaymentDbContext>()
            .AddRedis(configuration.GetConnectionString("Redis")!);

        return services;
    }
}

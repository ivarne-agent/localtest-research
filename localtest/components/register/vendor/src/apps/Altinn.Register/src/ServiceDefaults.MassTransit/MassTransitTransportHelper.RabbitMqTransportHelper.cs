using System.Diagnostics;
using Altinn.Authorization.ServiceDefaults.Npgsql.Yuniql;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Quartz;
using Quartz.AspNetCore;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Configuration helper for MassTransit.
/// </summary>
internal abstract partial class MassTransitTransportHelper
{
    private sealed class RabbitMqTransportHelper(MassTransitSettings settings, string busName)
        : MassTransitTransportHelper(settings, busName)
    {
        private MassTransitRabbitMqSettings Settings => BusSettings.RabbitMq;

        public override void ConfigureHost(IHostApplicationBuilder builder)
        {
            // TODO: make better
            var descriptor = builder.GetAltinnServiceDescriptor();
            var quartzSchema = builder.Configuration.GetValue($"Altinn:MassTransit:{descriptor.Name}:Quartz:Schema", defaultValue: $"{descriptor.Name}_quartz")!;
            var connectionString = builder.Configuration.GetValue<string>($"Altinn:Npgsql:{descriptor.Name}:ConnectionString");
            AddMigrations(builder);

            builder.Services.AddOptions<RabbitMqTransportOptions>()
                .Configure(options =>
                {
                    options.Host = Settings.Host;
                    options.Port = Settings.Port;
                    options.ManagementPort = Settings.ManagementPort;
                    options.VHost = Settings.VHost;
                    options.User = Settings.UserName;
                    options.Pass = Settings.Password;
                    options.UseSsl = Settings.UseSsl;
                    options.ConnectionName = BusName;
                });

            builder.Services.AddQuartz(q =>
            {
                q.SchedulerId = "AUTO"; // TODO: use instance ID from pod-name or similar
                q.UsePersistentStore(s =>
                {
                    s.UseSystemTextJsonSerializer();
                    s.UseClustering();
                    s.UsePostgres(p =>
                    {
                        p.ConnectionString = connectionString!;
                        p.TablePrefix = $"{quartzSchema}.";
                    });
                });
            });

            builder.Services.AddQuartzServer(opt =>
            {
                opt.WaitForJobsToComplete = true;
            });
        }

        public override void AddMigrations(IHostApplicationBuilder builder)
        {
            // TODO: make better
            var descriptor = builder.GetAltinnServiceDescriptor();
            var quartzSchema = builder.Configuration.GetValue($"Altinn:MassTransit:{descriptor.Name}:Quartz:Schema", defaultValue: $"{descriptor.Name}_quartz")!;
            var yuniqlSchema = builder.Configuration.GetValue($"Altinn:Npgsql:{descriptor.Name}:Yuniql:MigrationsTable:Schema", defaultValue: "yuniql");
            var yuniqlTable = builder.Configuration.GetValue($"Altinn:Npgsql:{descriptor.Name}:Yuniql:MigrationsTable:QuartzTable", defaultValue: $"{descriptor.Name}_quartz_migrations");
            var migrationsFs = new ManifestEmbeddedFileProvider(typeof(MassTransitTransportHelper).Assembly, "Migration");

            builder.AddAltinnPostgresDataSource()
                .AddYuniqlMigrations(typeof(MassTransitTransportHelper), y =>
                {
                    y.WorkspaceFileProvider = migrationsFs;
                    y.MigrationsTable.Schema = yuniqlSchema;
                    y.MigrationsTable.Name = yuniqlTable;
                    y.Tokens.Add("SCHEMA", quartzSchema);
                });
        }

        public override void ConfigureBus(IBusRegistrationConfigurator configurator, Action<IBusRegistrationContext, IBusFactoryConfigurator> configureBus)
        {
            var schedulerQueueName = $"{BusName}-scheduler";
            var schedulerEndpoint = new Uri($"queue:{schedulerQueueName}");

            configurator.AddMessageScheduler(schedulerEndpoint);
            configurator.AddQuartzConsumers(
                q => q.QueueName = schedulerQueueName);

            configurator.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.UseMessageScheduler(schedulerEndpoint);
                cfg.ReceiveEndpoint(schedulerQueueName, sch =>
                {
                    sch.ConfigureQuartzConsumers(ctx);
                });

                configureBus(ctx, cfg);
                cfg.ConfigureEndpoints(ctx);
            });
        }

        public override void RegisterTracing(TracerProviderBuilder builder)
        {
            builder.AddQuartzInstrumentation();
            builder.AddProcessor<FilterRootQuartzQueriesProcessor>();
        }

        private sealed class FilterRootQuartzQueriesProcessor
            : BaseProcessor<Activity>
        {
            public override void OnStart(Activity data)
            {
                if (data.Source.Name == "Npgsql" && data.Parent is null)
                {
                    data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
                    data.IsAllDataRequested = false;
                }
            }
        }
    }
}

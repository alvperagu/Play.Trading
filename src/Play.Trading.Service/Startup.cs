using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Play.Common.Identity;
using Play.Common.MongoDB;
using Play.Common.MassTransit;
using MassTransit;
using Play.Trading.Service.StateMachines;
using Play.Common.Settings;
using System.Text.Json.Serialization;
using Play.Trading.Service.Entities;
using System.Reflection;
using GreenPipes;
using System;
using Play.Trading.Service.Exceptions;
using Play.Trading.Service.Settings;
using Play.Inventory.Contracts;
using Play.Identity.Contracts;
using Microsoft.AspNetCore.SignalR;
using Play.Trading.Service.SignalR;

namespace Play.Trading.Service
{
    public class Startup
    {

        private const string AllowedOriginSettings = "AllowedOrigins"; 
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMongo()
                    .AddMongoRepository<CatalogItem>("catalogitems")
                    .AddMongoRepository<InventoryItem>("inventoryitems")
                    .AddMongoRepository<ApplicationUser>("users")
                    .AddJwtBearerAuthentication();
            AddMassTransit(services);
            services.AddControllers(options => 
            {
                options.SuppressAsyncSuffixInActionNames = false; //otherwise the post will fail as suffix "Async" is removed in runtime and can't find the method
            })
            .AddJsonOptions(options => 
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Play.Trading.Service", Version = "v1" });
            });

            services.AddSingleton<IUserIdProvider, UserIdProvider>()
                    .AddSingleton<MessageHub>()
                    .AddSignalR();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Play.Trading.Service v1"));

                app.UseCors(builder => {
                    builder.WithOrigins(Configuration[AllowedOriginSettings])
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials(); //allows for cookies
                });
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.UseAuthentication();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<MessageHub>("/messagehub");
            });
        }

        private void AddMassTransit(IServiceCollection services)
        {
            services.AddMassTransit(configure =>
            {
                configure.UsingPlayEconomyRabbitMq(retryConfigurator => {
                    retryConfigurator.Interval(3, TimeSpan.FromSeconds(5));
                    retryConfigurator.Ignore(typeof(UnknownItemException));
                });

                configure.AddConsumers(Assembly.GetEntryAssembly());
                configure.AddSagaStateMachine<PurchaseStateMachine, PurchaseState>(sagaConfigurator => 
                {
                    sagaConfigurator.UseInMemoryOutbox(); //no message will not be sent from the saga pipeline until we haven't transictioned to the state in the database 
                })
                    .MongoDbRepository(r =>
                    {
                        var serviceSettings = Configuration.GetSection(nameof(ServiceSettings))
                                                           .Get<ServiceSettings>();
                        var mongoSettings = Configuration.GetSection(nameof(MongoDbSettings))
                                                           .Get<MongoDbSettings>();                                                           

                        r.Connection = mongoSettings.ConnectionString;
                        r.DatabaseName = serviceSettings.ServiceName;
                    });
            });

            var queueSettings = Configuration.GetSection(nameof(QueueSettings)).Get<QueueSettings>();

            EndpointConvention.Map<GrantItems>(new Uri(queueSettings.GrantItemsQueueAddress));
            EndpointConvention.Map<DebitGil>(new Uri(queueSettings.DebitGilQueueAddress));
            EndpointConvention.Map<SubtractItems>(new Uri(queueSettings.SubtractItemsQueueAddress));

            services.AddMassTransitHostedService();
            services.AddGenericRequestClient();
        }
    }
}

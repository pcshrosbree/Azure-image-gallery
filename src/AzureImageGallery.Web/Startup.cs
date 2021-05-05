// ----------------------------------------------------------------------------------------------------
// <copyright file="Startup.cs" company="Microsoft">
//     Copyright &#169; Microsoft Corporation. All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------------------------------

namespace AzureImageGallery.Web
{
    using System;
    using Azure.Core;
    using Azure.Core.Pipeline;
    using Azure.Storage.Blobs;
    using AzureImageGallery.Data;
    using AzureImageGallery.Data.Models;
    using AzureImageGallery.Services;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using static System.Environment;

    public class Startup
    {
        private readonly IConfiguration _config;

        public Startup(IConfiguration config)
        {
            _config = config;
        }

        public IConfiguration Configuration { get; }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseDeveloperExceptionPage();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        public void ConfigureDevelopmentServices(IServiceCollection services)
        {
            services.AddDbContext<AzureImageGalleryDbContext>(_ =>
                _.UseSqlite(_config.GetConnectionString("Sqlite")));

            ConfigureServices(services);
        }

        public void ConfigureProductionServices(IServiceCollection services)
        {
            services.AddDbContext<AzureImageGalleryDbContext>(x =>
                x.UseSqlServer(_config.GetConnectionString("DefaultConnection")));

            ConfigureServices(services);
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IImage, ImageService>();

            services.AddControllersWithViews();
            services.AddSingleton(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FaultInjectionTransport>>();
                var config = provider.GetRequiredService<IConfiguration>();
                var section = config.GetSection("Throttling");
                var text = section?["AvailableInterval"];
                var available = text is not null && TimeSpan.TryParse(text, out var interval)
                    ? interval
                    : TimeSpan.FromSeconds(30);
                text = section?["ThrottlingInterval"];
                var throttled = text is not null && TimeSpan.TryParse(text, out interval)
                    ? interval
                    : TimeSpan.FromSeconds(10);
                return new FaultInjectionTransport(HttpClientTransport.Shared, available, throttled, logger);
            });
            services.AddSingleton<HttpPipelineTransport>(provider =>
            {
                var azureSdkTransport = GetEnvironmentVariable("AZURE_SDK_TRANSPORT");
                if (azureSdkTransport is null || !bool.TryParse(azureSdkTransport, out var defaultTransport) || defaultTransport)
                    return HttpClientTransport.Shared;
                return provider.GetRequiredService<FaultInjectionTransport>();
            });
            services.AddScoped(provider =>
            {
                var options = new BlobClientOptions
                {
                    Transport = provider.GetRequiredService<HttpPipelineTransport>()
                };
                var config = provider.GetRequiredService<IConfiguration>();
                LoadRetryOptions(options.Retry, config);
                return options;
            });
            services.AddScoped(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<BlobClientOptions>();
                return new BlobServiceClient(Configuration.GetValue<string>("AzureStorageConnectionString"), options);
            });

            services.AddIdentity<AppUser, IdentityRole<Guid>>()
               .AddEntityFrameworkStores<AzureImageGalleryDbContext>()
               .AddSignInManager<SignInManager<AppUser>>()
               .AddDefaultTokenProviders();

            services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/User/Login";
            });

            services.AddAuthentication();
        }

        private static void LoadRetryOptions(RetryOptions retry, IConfiguration configuration)
        {
            var section = configuration.GetSection("RetryPolicy");
            if (section is not null)
            {
                var text = section["MaxRetries"];
                retry.MaxRetries = text is not null && int.TryParse(text, out var count) ? count : 3;
                text = section["Delay"];
                retry.Delay = text is not null && TimeSpan.TryParse(text, out var period) ? period : TimeSpan.FromSeconds(0.8);
                text = section["MaxDelay"];
                retry.MaxDelay = text is not null && TimeSpan.TryParse(text, out period) ? period : TimeSpan.FromMinutes(1);
                text = section["Mode"];
                retry.Mode = text is not null && Enum.TryParse(text, out RetryMode mode) ? mode : RetryMode.Exponential;
                text = section["NetworkTimeout"];
                retry.NetworkTimeout = text is not null && TimeSpan.TryParse(text, out period) ? period : TimeSpan.FromSeconds(100);
            }
        }
    }
}

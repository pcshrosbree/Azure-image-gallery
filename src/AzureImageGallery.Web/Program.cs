// ----------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft">
//     Copyright &#169; Microsoft Corporation. All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------------------------------

namespace AzureImageGallery.Web
{
    using AzureImageGallery.Data;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Hosting;

    public static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args)
                .Build()
                .MigrateDatabase()
                .Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}

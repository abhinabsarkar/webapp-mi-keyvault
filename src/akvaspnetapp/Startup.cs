using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.IO;

namespace akvwebapp
{
    public class Startup
    {

        string vaultUri = "";
        string dbCredentials = "";

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
                 .AddJsonFile("config/appsettings.json");

            var config = builder.Build();

            var appConfig = config.GetSection("EnvironmentConfig").Get<EnvironmentConfig>();
            vaultUri = appConfig.VaultUri;
            dbCredentials = appConfig.DBCredentials;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();            

            SecretClientOptions options = new SecretClientOptions()
            {
                Retry =
                {
                    Delay= TimeSpan.FromSeconds(2),
                    MaxDelay = TimeSpan.FromSeconds(16),                    
                    // Mode = RetryMode.Exponential,
                    MaxRetries = 5
                }
            };

            string secretValue = null;
            try
            {
                var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential(),options);
                KeyVaultSecret secret = client.GetSecret(dbCredentials);
                secretValue = secret.Value;
            }
            catch (Exception)
            {                                
                secretValue = "Cannot access key vault. Set up Managed Identity!!!";
            }

            app.UseEndpoints(endpoints =>
            {
                // Set up the response for base path
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Hello World from a .Net core web app!!!");
                });

                // Set up the response to demonstrate Azure Key Vault
                endpoints.MapGet("/keyvault", async context =>
                {                    
                    await context.Response.WriteAsync(secretValue);
                });
            });
        }
    }
}

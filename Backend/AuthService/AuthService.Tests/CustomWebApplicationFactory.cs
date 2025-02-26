using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AuthService.Tests
{
    public class CustomWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Здесь настройте окружение, если необходимо
            builder.UseEnvironment("Development");
            var host = builder.Build();
            host.Start();
            return host;
        }
    }
}

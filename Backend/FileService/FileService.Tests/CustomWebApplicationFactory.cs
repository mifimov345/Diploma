using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace YourTestsNamespace
{
    public class CustomWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Можно задать окружение, если нужно:
            builder.UseEnvironment("Development");
            var host = builder.Build();
            host.Start();
            return host;
        }
    }
}

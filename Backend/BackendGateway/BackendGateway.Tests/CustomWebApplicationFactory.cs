using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendGateway.Tests
{
    // Фиктивный обработчик, который возвращает заранее заданный ответ
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _fakeResponse;
        public FakeHttpMessageHandler(HttpResponseMessage fakeResponse)
        {
            _fakeResponse = fakeResponse;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_fakeResponse);
        }
    }

    // Реализация IHttpClientFactory, возвращающая HttpClient с нашим FakeHttpMessageHandler
    public class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Test response from BackendGateway")
            };
            return new HttpClient(new FakeHttpMessageHandler(fakeResponse));
        }
    }

    // Кастомная фабрика для тестов
    public class CustomWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Задаем нужное окружение
            builder.UseEnvironment("Development");

            // Перенастраиваем DI, чтобы заменить IHttpClientFactory
            builder.ConfigureServices(services =>
            {
                var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHttpClientFactory));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<IHttpClientFactory, TestHttpClientFactory>();
            });

            var host = builder.Build();
            host.Start();
            return host;
        }
    }
}

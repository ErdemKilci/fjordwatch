using FjordWatch.Web;
using FjordWatch.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

var apiBaseUrl = builder.Configuration["PublicApiBaseUrl"] ?? "http://localhost:8080";
var hubUrl = builder.Configuration["PublicHubUrl"] ?? $"{apiBaseUrl}/hubs/vessels";

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/"),
});

builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AgentClient>();
builder.Services.AddScoped(_ => new VesselsHubClient(new Uri(hubUrl)));

await builder.Build().RunAsync();

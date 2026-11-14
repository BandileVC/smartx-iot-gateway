using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SmartX.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);


builder.RootComponents.Add<SmartX.Client.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");


const string apiBaseAddress = "http://localhost:5050";


builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseAddress) });
builder.Services.AddScoped(_ => new ApiClient(apiBaseAddress));

builder.Services.AddSingleton(new StreamApiClient(apiBaseAddress));
builder.Services.AddSingleton<StreamStore>();

builder.Services.AddSingleton(new MeshApiClient(apiBaseAddress));
builder.Services.AddSingleton<MeshStore>();

await builder.Build().RunAsync();

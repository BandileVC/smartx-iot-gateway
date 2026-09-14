using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SmartX.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Root component + host page.
builder.RootComponents.Add<SmartX.Client.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// API base address - matches SmartX.Api's launch profile (http://localhost:5050).
const string apiBaseAddress = "http://localhost:5050";

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseAddress) });
builder.Services.AddScoped(_ => new ApiClient(apiBaseAddress));

await builder.Build().RunAsync();

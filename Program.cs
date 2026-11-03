using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Godzinki;
using System.Globalization;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(_=>new HttpClient { Timeout=TimeSpan.FromSeconds(25) });
builder.Services.AddScoped<DataStore>();
await builder.Build().RunAsync();


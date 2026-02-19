using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DwgMapOverlayPoc;
using DwgMapOverlayPoc.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Named HttpClient scoped to the app origin – used for tile/asset fetching
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// Application services (registered here so they are available via DI throughout)
builder.Services.AddScoped<TileService>();      // retained for future offline tile-pack work
builder.Services.AddScoped<OverlayService>();
builder.Services.AddScoped<BaseMapService>();   // base-map provider catalogue + Mapbox token
builder.Services.AddScoped<DwgAssetService>(); // file uploads: PNG / GeoJSON / ZIP

// Tasks 4–7 services
builder.Services.AddScoped<QuickAlignService>(); // 3-point georeferencing + affine math
builder.Services.AddScoped<TmsTileService>();    // ZIP → in-memory tile cache
builder.Services.AddScoped<SnappingService>();   // GeoJSON vertex/edge snap via Turf.js
builder.Services.AddScoped<EquipmentService>();  // marker CRUD, snap, GeoJSON export

await builder.Build().RunAsync();

using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using PakTotal.Api;
using PakTotal.Components;
using PakTotal.Core;
using PakTotal.Core.Modules;
using PakTotal.Core.Results;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection(PakTotalOptions.Section).Get<PakTotalOptions>() ?? new PakTotalOptions();

if (args.Contains("--download-natives"))
{
    using var factory = LoggerFactory.Create(logging => logging.AddConsole());
    await NativeLibraries.InitializeAsync(options.NativeDirectory, factory.CreateLogger("PakTotal"));
    return;
}

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = options.MaxUploadBytes + 1024 * 1024);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddPakTotalCore(builder.Configuration);
builder.Services.AddPakTotalRateLimiting(options);
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in options.TrustedProxies) forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
    foreach (var network in options.TrustedNetworks) forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

var app = builder.Build();

await NativeLibraries.InitializeAsync(options.NativeDirectory, app.Logger);
app.Services.GetRequiredService<ModuleCatalog>();
app.Services.GetRequiredService<ResultCache>();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapPakTotalApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

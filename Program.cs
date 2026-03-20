using DotnetAppManager.Endpoints;
using DotnetAppManager.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddTransient<ProjectDiscoveryService>();
builder.Services.AddSingleton<ProcessManagerService>();
builder.Services.AddSingleton<ProjectPreferencesService>();
builder.Services.AddSingleton<ProfileService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

SseEndpoints.MapSseEndpoints(app);
ProcessEndpoints.MapProcessEndpoints(app);

app.Run();

using PcsHmi.Components;
using PcsHmi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.Configure<PcsOptions>(builder.Configuration.GetSection("Pcs"));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<PlantState>();
builder.Services.AddSingleton<AlarmService>();
builder.Services.AddSingleton<SerialService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SerialService>());

var app = builder.Build();

// Opprett tabeller før noe annet bruker databasen
app.Services.GetRequiredService<Database>().Initialize();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

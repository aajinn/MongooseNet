using Microsoft.Extensions.Options;
using MongooseNet;
using MongooseNet.Example.Models;

var builder = WebApplication.CreateBuilder(args);

// ── MongooseNet — bind from appsettings.json with startup validation ──────
builder.Services.AddOptions<MongooseOptions>()
    .BindConfiguration("MongooseNet")
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<MongooseOptions>, MongooseOptionsValidator>();

// Register MongooseNet — auto-discovers all BaseDocument subclasses in this assembly
builder.Services.AddMongoose(opts =>
    builder.Configuration.GetSection("MongooseNet").Bind(opts));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ── Ensure indexes at startup (idempotent — safe to run every time) ───────
await app.Services.EnsureMongoIndexesAsync(typeof(User).Assembly);

app.MapControllers();
app.Run();

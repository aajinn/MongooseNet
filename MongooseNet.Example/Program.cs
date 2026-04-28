using MongooseNet;
using MongooseNet.Example.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Register MongooseNet (one line) ───────────────────────────────────────
builder.Services.AddMongoose(opts =>
{
    opts.ConnectionString = builder.Configuration["Mongo:ConnectionString"]!;
    opts.DatabaseName     = builder.Configuration["Mongo:Database"]!;
});

builder.Services.AddControllers();

var app = builder.Build();

// ── Ensure indexes at startup (idempotent) ────────────────────────────────
await app.Services.EnsureMongoIndexesAsync(typeof(User).Assembly);

app.MapControllers();
app.Run();

# MongooseNet

[![NuGet](https://img.shields.io/nuget/v/MongooseNet.svg)](https://www.nuget.org/packages/MongooseNet)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-blue)](https://dotnet.microsoft.com)

A lightweight **Mongoose-inspired ODM** for MongoDB in .NET 8/9/10.  
Fluent LINQ queries · Pre-save hooks · Auto timestamps · Declarative indexes · One-line DI setup.

---

## Install

```bash
dotnet add package MongooseNet
```

---

## Quick Start

### 1. Define a model

```csharp
using MongooseNet;
using MongooseNet.Indexes;

[CollectionName("users")]          // optional — defaults to "users"
public class User : BaseDocument
{
    public string Name  { get; set; } = string.Empty;

    [MongoIndex(unique: true, name: "idx_users_email")]  // declarative unique index
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    [BsonIgnore]
    public string? PlainTextPassword { get; set; }

    // Mongoose-style pre('save') hook
    public override void PreSave()
    {
        if (!string.IsNullOrWhiteSpace(PlainTextPassword))
        {
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PlainTextPassword);
            PlainTextPassword = null;
        }
        base.PreSave(); // stamps CreatedAt / UpdatedAt
    }
}
```

### 2. Register in Program.cs

```csharp
builder.Services.AddMongoose(opts =>
{
    opts.ConnectionString = "mongodb://localhost:27017";
    opts.DatabaseName     = "myapp";
    // opts.AutoRegisterModels = true; // default — scans calling assembly automatically
});

var app = builder.Build();

// Creates indexes declared via [MongoIndex] — idempotent, safe to call every startup
await app.Services.EnsureMongoIndexesAsync(typeof(User).Assembly);

app.Run();
```

### 3. Inject and use

```csharp
public class UsersController(IMongoRepository<User> users) : ControllerBase
{
    [HttpGet("search")]
    public Task<List<User>> Search(string email)
        => users.FindAsync(x => x.Email == email);

    [HttpGet("{id:guid}")]
    public Task<User> GetById(Guid id)
        => users.GetByIdRequiredAsync(id); // throws DocumentNotFoundException if missing

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        var user = new User { Name = req.Name, Email = req.Email, PlainTextPassword = req.Password };
        await users.InsertAsync(user); // PreSave() fires automatically
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }
}
```

---

## API Reference

### BaseDocument

| Member | Description |
|---|---|
| `Id` | `Guid`, mapped to MongoDB `_id` |
| `CreatedAt` | UTC. Set once on first save, never overwritten |
| `UpdatedAt` | UTC. Refreshed on every save |
| `PreSave()` | Virtual hook — override to run logic before any write |

### IMongoRepository\<T\>

| Method | Returns | Description |
|---|---|---|
| `GetAllAsync()` | `List<T>` | All documents in the collection |
| `GetByIdAsync(id)` | `T?` | By id, returns `null` if not found |
| `GetByIdRequiredAsync(id)` | `T` | By id, throws `DocumentNotFoundException` if not found |
| `FindAsync(predicate)` | `List<T>` | LINQ filter → all matches |
| `FindOneAsync(predicate)` | `T?` | LINQ filter → first match or `null` |
| `CountAsync(predicate?)` | `long` | Count matching documents (all if predicate omitted) |
| `ExistsAsync(predicate)` | `bool` | `true` if any document matches |
| `InsertAsync(doc)` | `T` | Insert one, fires `PreSave` |
| `InsertManyAsync(docs)` | `Task` | Batch insert, fires `PreSave` on each |
| `SaveAsync(doc)` | `T` | Upsert by id, fires `PreSave` |
| `UpdateAsync(id, update)` | `bool` | Partial update via `UpdateDefinition<T>`, returns `true` if modified |
| `DeleteAsync(id)` | `bool` | Delete by id, returns `true` if deleted |
| `DeleteManyAsync(predicate)` | `long` | Delete all matches, returns count deleted |
| `Collection` | `IMongoCollection<T>` | Raw driver collection for advanced queries |

All methods accept an optional `CancellationToken ct` parameter.

### MongooseOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | `string` | required | MongoDB connection string |
| `DatabaseName` | `string` | required | Database to connect to |
| `AutoRegisterModels` | `bool` | `true` | Auto-scan assembly and register repositories |

When `AutoRegisterModels` is `false`, register models individually:

```csharp
builder.Services.AddMongooseModel<User>();
builder.Services.AddMongooseModel<Product>();
```

### Attributes

| Attribute | Target | Description |
|---|---|---|
| `[CollectionName("name")]` | Class | Override the MongoDB collection name |
| `[MongoIndex]` | Property | Declare an index on this field |

`[MongoIndex]` parameters:

| Parameter | Type | Default | Description |
|---|---|---|---|
| `unique` | `bool` | `false` | Enforce uniqueness |
| `sparse` | `bool` | `false` | Omit documents missing this field |
| `name` | `string?` | `null` | Custom index name |
| `order` | `int` | `1` | `1` = ascending, `-1` = descending |

### Exceptions

| Exception | Thrown by | Description |
|---|---|---|
| `DocumentNotFoundException` | `GetByIdRequiredAsync` | Document with the given id was not found |
| `MongooseNetException` | base class | Base for all MongooseNet exceptions |

`DocumentNotFoundException` exposes `CollectionName` and `DocumentId` properties for structured error handling.

---

## License

MIT

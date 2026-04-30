# MongooseNet

[![NuGet](https://img.shields.io/nuget/v/MongooseNet.svg)](https://www.nuget.org/packages/MongooseNet)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-blue)](https://dotnet.microsoft.com)

A lightweight **Mongoose-inspired ODM** for MongoDB in .NET 8/9/10.  
Fluent LINQ queries · Pagination · Projection · Pre-save hooks · Auto timestamps · Soft delete · Declarative indexes · One-line DI setup.

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

[CollectionName("users")]   // optional — defaults to "users". Validated against MongoDB naming rules.
public class User : BaseDocument
{
    public string Name { get; set; } = string.Empty;

    [MongoIndex(unique: true, name: "idx_users_email")]
    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string PasswordHash { get; set; } = string.Empty;

    [BsonIgnore]
    public string? PlainTextPassword { get; set; }

    // Mongoose-style pre('save') hook — fires before every insert / save
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
// Option A — inline delegate (validated immediately at startup)
builder.Services.AddMongoose(opts =>
{
    opts.ConnectionString  = "mongodb://localhost:27017";
    opts.DatabaseName      = "myapp";
    opts.FilterSoftDeleted = true;   // exclude soft-deleted docs from all queries (default)
    opts.RetryCount        = 3;      // retry transient errors up to 3 times (default)
    opts.RetryDelay        = TimeSpan.FromMilliseconds(200); // exponential back-off base (default)
});

// Option B — bind from appsettings.json (validated at startup via MongooseOptionsValidator)
builder.Services.AddOptions<MongooseOptions>()
    .BindConfiguration("MongooseNet")
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MongooseOptions>, MongooseOptionsValidator>();

var app = builder.Build();

// Create indexes declared via [MongoIndex] — idempotent, safe to call every startup
await app.Services.EnsureMongoIndexesAsync(typeof(User).Assembly);

app.Run();
```

`appsettings.json` for Option B:

```json
{
  "MongooseNet": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "myapp"
  }
}
```

### 3. Inject and use

```csharp
public class UsersController(IMongoRepository<User> users) : ControllerBase
{
    // ── Basic queries ──────────────────────────────────────────────────────────

    [HttpGet]
    public Task<List<User>> GetAll()
        => users.GetAllAsync(); // soft-deleted docs excluded automatically

    [HttpGet("{id:guid}")]
    public Task<User> GetById(Guid id)
        => users.GetByIdRequiredAsync(id); // throws DocumentNotFoundException if missing

    [HttpGet("search")]
    public Task<List<User>> Search(string email)
        => users.FindAsync(x => x.Email == email);

    [HttpGet("first")]
    public Task<User?> FindFirst(string name)
        => users.FindOneAsync(x => x.Name == name); // returns null if not found

    [HttpGet("count")]
    public Task<long> Count()
        => users.CountAsync(x => x.IsActive);

    [HttpGet("exists")]
    public Task<bool> EmailExists(string email)
        => users.ExistsAsync(x => x.Email == email);

    // ── Projection — fetch only the fields you need ────────────────────────────

    [HttpGet("emails")]
    public Task<List<UserEmailDto>> GetEmails()
    {
        var projection = Builders<User>.Projection
            .Expression(u => new UserEmailDto { Id = u.Id, Email = u.Email });

        return users.FindProjectedAsync(x => x.IsActive, projection);
    }

    // ── Pagination ─────────────────────────────────────────────────────────────

    [HttpGet("page")]
    public Task<PagedResult<User>> GetPage(int page = 1, int pageSize = 20)
        => users.PageAsync(
            predicate:  x => x.IsActive,
            page:       page,
            pageSize:   pageSize,
            orderBy:    x => x.CreatedAt,
            descending: true);

    // ── Writes ─────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        var user = new User
        {
            Name              = req.Name,
            Email             = req.Email,
            PlainTextPassword = req.Password,
        };
        await users.InsertAsync(user); // PreSave() fires automatically
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [HttpPost("batch")]
    public async Task<IActionResult> CreateMany(List<CreateUserRequest> reqs)
    {
        var docs = reqs.Select(r => new User
        {
            Name              = r.Name,
            Email             = r.Email,
            PlainTextPassword = r.Password,
        }).ToList();

        await users.InsertManyAsync(docs); // PreSave() fires on each
        return Ok(docs.Select(d => d.Id));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateName(Guid id, string name)
    {
        var update = Builders<User>.Update.Set(x => x.Name, name);
        var updated = await users.UpdateAsync(id, update); // auto-stamps UpdatedAt
        return updated ? Ok() : NotFound();
    }

    // FindOneAndUpdate — atomic find + modify in a single round trip
    [HttpPatch("{id:guid}/activate")]
    public Task<User?> Activate(Guid id)
    {
        var update = Builders<User>.Update.Set(x => x.IsActive, true);
        return users.FindOneAndUpdateAsync(x => x.Id == id, update, returnAfter: true);
    }

    // ── Soft delete ────────────────────────────────────────────────────────────

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id)
    {
        var deleted = await users.SoftDeleteAsync(id); // stamps DeletedAt, doc stays in DB
        return deleted ? Ok() : NotFound();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        var restored = await users.RestoreAsync(id); // clears DeletedAt
        return restored ? Ok() : NotFound();
    }

    [HttpGet("deleted")]
    public Task<List<User>> GetDeleted()
        => users.GetDeletedAsync();

    // ── Hard delete ────────────────────────────────────────────────────────────

    [HttpDelete("{id:guid}/hard")]
    public async Task<IActionResult> HardDelete(Guid id)
    {
        var deleted = await users.DeleteAsync(id);
        return deleted ? Ok() : NotFound();
    }
}
```

---

## Streaming

For large collections, use `StreamAsync` to process documents one at a time via a server-side cursor instead of loading everything into memory:

```csharp
await foreach (var user in users.StreamAsync(x => x.IsActive, ct))
{
    await SendWelcomeEmail(user);
}
```

---

## Transactions

```csharp
await orders.WithTransactionAsync(async session =>
{
    await orders.InsertAsync(newOrder);
    await inventory.UpdateAsync(item.Id,
        Builders<InventoryItem>.Update.Inc(x => x.Stock, -1));
});
// Commits on success, rolls back automatically on any exception
```

---

## Bulk Writes

Execute mixed write operations in a single round trip:

```csharp
var requests = new List<WriteModel<User>>
{
    new InsertOneModel<User>(newUser),
    new UpdateOneModel<User>(
        Builders<User>.Filter.Eq(x => x.Id, existingId),
        Builders<User>.Update.Set(x => x.IsActive, false)),
    new DeleteOneModel<User>(
        Builders<User>.Filter.Eq(x => x.Id, staleId)),
};

BulkWriteResult<User> result = await users.BulkWriteAsync(requests);
```

---

## Pagination

`PageAsync` runs the count and data queries **in parallel** and returns a `PagedResult<T>` with everything you need to build pagination UI.

```csharp
var result = await users.PageAsync(
    predicate:  x => x.IsActive,
    page:       2,
    pageSize:   10,
    orderBy:    x => x.CreatedAt,
    descending: true);

Console.WriteLine($"Page {result.Page} of {result.TotalPages}");  // Page 2 of 5
Console.WriteLine($"Total: {result.TotalCount}");                  // Total: 42
Console.WriteLine($"Has next: {result.HasNextPage}");              // Has next: true

foreach (var user in result.Items) { ... }
```

### PagedResult\<T\> properties

| Property | Type | Description |
|---|---|---|
| `Items` | `List<T>` | Documents on this page |
| `TotalCount` | `long` | Total matching documents across all pages |
| `Page` | `int` | Current page number (1-based) |
| `PageSize` | `int` | Items per page |
| `TotalPages` | `int` | Total number of pages |
| `HasNextPage` | `bool` | `true` if more pages follow |
| `HasPreviousPage` | `bool` | `true` if not on the first page |

---

## API Reference

### BaseDocument

| Member | Type | Description |
|---|---|---|
| `Id` | `Guid` | Mapped to MongoDB `_id`. Auto-assigned on construction |
| `CreatedAt` | `DateTime` | UTC. Set once on first save, never overwritten |
| `UpdatedAt` | `DateTime` | UTC. Refreshed on every save |
| `DeletedAt` | `DateTime?` | Set when soft-deleted; `null` means active |
| `IsDeleted` | `bool` | `true` if `DeletedAt` has a value |
| `PreSave()` | `virtual void` | Hook fired before any write — override for custom logic |

### IMongoRepository\<T\>

#### Queries

| Method | Returns | Description |
|---|---|---|
| `GetAllAsync()` | `List<T>` | All documents (excludes soft-deleted by default) |
| `GetByIdAsync(id)` | `T?` | By id, returns `null` if not found |
| `GetByIdRequiredAsync(id)` | `T` | By id, throws `DocumentNotFoundException` if not found |
| `FindAsync(predicate)` | `List<T>` | LINQ filter → all matches |
| `FindOneAsync(predicate)` | `T?` | LINQ filter → first match or `null` |
| `FindOneAndUpdateAsync(predicate, update, returnAfter?)` | `T?` | Atomic find + update in one round trip |
| `FindProjectedAsync(predicate, projection)` | `List<TProjection>` | Fetch only specified fields |
| `PageAsync(predicate?, page, pageSize, orderBy?, descending)` | `PagedResult<T>` | Paginated, filtered, sorted results |
| `StreamAsync(predicate?)` | `IAsyncEnumerable<T>` | Server-side cursor for large collections |
| `CountAsync(predicate?)` | `long` | Count matching documents |
| `ExistsAsync(predicate)` | `bool` | `true` if any document matches |

#### Writes

| Method | Returns | Description |
|---|---|---|
| `InsertAsync(doc)` | `T` | Insert one, fires `PreSave` |
| `InsertManyAsync(docs)` | `Task` | Batch insert, fires `PreSave` on each |
| `SaveAsync(doc)` | `T` | Upsert by id, fires `PreSave` |
| `UpdateAsync(id, update)` | `bool` | Partial update via `UpdateDefinition<T>`, auto-stamps `UpdatedAt` |
| `BulkWriteAsync(requests, options?)` | `BulkWriteResult<T>` | Mixed writes in a single round trip |

#### Soft Delete

| Method | Returns | Description |
|---|---|---|
| `SoftDeleteAsync(id)` | `bool` | Stamps `DeletedAt`; document stays in MongoDB |
| `RestoreAsync(id)` | `bool` | Clears `DeletedAt`, making the document active again |
| `GetDeletedAsync()` | `List<T>` | Returns all soft-deleted documents |

#### Hard Delete

| Method | Returns | Description |
|---|---|---|
| `DeleteAsync(id)` | `bool` | Permanently delete by id |
| `DeleteManyAsync(predicate)` | `long` | Permanently delete all matches, returns count |

#### Transactions & Raw Access

| Member | Returns | Description |
|---|---|---|
| `WithTransactionAsync(operation)` | `Task` | Run operation in a MongoDB multi-document transaction |
| `Collection` | `IMongoCollection<T>` | Underlying driver collection for advanced scenarios |

All methods accept an optional `CancellationToken ct` parameter.

---

### MongooseOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | `string` | required | MongoDB connection string |
| `DatabaseName` | `string` | required | Database to connect to |
| `AutoRegisterModels` | `bool` | `true` | Auto-scan assembly and register repositories |
| `FilterSoftDeleted` | `bool` | `true` | Exclude soft-deleted documents from standard queries |
| `RetryCount` | `int` | `3` | Max retries for transient errors (`0` to disable) |
| `RetryDelay` | `TimeSpan` | `200 ms` | Base delay between retries (doubles each attempt) |

When `AutoRegisterModels` is `false`, register models individually:

```csharp
builder.Services.AddMongooseModel<User>();
builder.Services.AddMongooseModel<Product>();
```

#### Using IOptions\<MongooseOptions\>

If you bind options from `appsettings.json` instead of the `AddMongoose` delegate, register `MongooseOptionsValidator` to catch missing or invalid values at startup:

```csharp
builder.Services.AddOptions<MongooseOptions>()
    .BindConfiguration("MongooseNet")
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<MongooseOptions>, MongooseOptionsValidator>();
```

---

### Attributes

| Attribute | Target | Description |
|---|---|---|
| `[CollectionName("name")]` | Class | Override the MongoDB collection name |
| `[MongoIndex]` | Property | Declare a single-field index |

`[CollectionName]` validates the name at construction time and rejects:
- Null or whitespace
- Names containing `$` or null bytes
- Names starting with `system.`
- Names whose UTF-8 encoding exceeds 120 bytes

`[MongoIndex]` parameters:

| Parameter | Type | Default | Description |
|---|---|---|---|
| `unique` | `bool` | `false` | Enforce uniqueness |
| `sparse` | `bool` | `false` | Omit documents missing this field |
| `name` | `string?` | `null` | Custom index name |
| `order` | `int` | `1` | `1` = ascending, `-1` = descending |

---

### Exceptions

| Exception | Thrown by | Description |
|---|---|---|
| `DocumentNotFoundException` | `GetByIdRequiredAsync` | Document with the given id was not found |
| `MongooseNetException` | all repository methods | Base exception — wraps MongoDB driver errors |

`DocumentNotFoundException` exposes `CollectionName` and `DocumentId` for structured error handling.

---

## Changelog

### 1.1.0
- **`CollectionNameAttribute`** now validates names against MongoDB rules at construction time (rejects `$`, null bytes, `system.` prefix, names > 120 UTF-8 bytes)
- **`MongooseOptions`** gains a `Validate()` method and a new `MongooseOptionsValidator` (`IValidateOptions<MongooseOptions>`) for validation when options are bound via `IOptions<>` / `appsettings.json`
- **`MongooseOptions`** table updated to include all properties (`FilterSoftDeleted`, `RetryCount`, `RetryDelay`)
- Exception messages from repository methods no longer include raw MongoDB driver topology details
- Quick Start updated to cover all features: projection, soft delete, restore, streaming, transactions, bulk writes, `FindOneAndUpdateAsync`, `CountAsync`, `ExistsAsync`, `InsertManyAsync`

### 1.0.0
- Initial release

---

## License

MIT © 2026 Ajin Varghese Chandy

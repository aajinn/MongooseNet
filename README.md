# MongooseNet

[![NuGet](https://img.shields.io/nuget/v/MongooseNet.svg)](https://www.nuget.org/packages/MongooseNet)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-blue)](https://dotnet.microsoft.com)

A lightweight **Mongoose-inspired ODM** for MongoDB in .NET 8/9/10.  
Fluent LINQ queries · Pagination · Pre-save hooks · Auto timestamps · Declarative indexes · One-line DI setup.

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
    [HttpGet]
    public Task<List<User>> GetAll()
        => users.GetAllAsync();

    [HttpGet("search")]
    public Task<List<User>> Search(string email)
        => users.FindAsync(x => x.Email == email);

    [HttpGet("page")]
    public Task<PagedResult<User>> GetPage(int page = 1, int pageSize = 20)
        => users.PageAsync(page: page, pageSize: pageSize, orderBy: x => x.CreatedAt, descending: true);

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

## Pagination

`PageAsync` runs the count and data queries **in parallel** and returns a `PagedResult<T>` with everything you need to build pagination UI.

```csharp
// Basic — page 1, 20 items
var result = await users.PageAsync();

// Filtered + sorted
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
| `PreSave()` | `virtual void` | Hook fired before any write — override for custom logic |

### IMongoRepository\<T\>

#### Queries

| Method | Returns | Description |
|---|---|---|
| `GetAllAsync()` | `List<T>` | All documents in the collection |
| `GetByIdAsync(id)` | `T?` | By id, returns `null` if not found |
| `GetByIdRequiredAsync(id)` | `T` | By id, throws `DocumentNotFoundException` if not found |
| `FindAsync(predicate)` | `List<T>` | LINQ filter → all matches |
| `FindOneAsync(predicate)` | `T?` | LINQ filter → first match or `null` |
| `PageAsync(predicate?, page, pageSize, orderBy?, descending)` | `PagedResult<T>` | Paginated + optionally filtered and sorted results |
| `CountAsync(predicate?)` | `long` | Count matching documents (all if predicate omitted) |
| `ExistsAsync(predicate)` | `bool` | `true` if any document matches |

#### Writes

| Method | Returns | Description |
|---|---|---|
| `InsertAsync(doc)` | `T` | Insert one, fires `PreSave` |
| `InsertManyAsync(docs)` | `Task` | Batch insert, fires `PreSave` on each |
| `SaveAsync(doc)` | `T` | Upsert by id, fires `PreSave` |
| `UpdateAsync(id, update)` | `bool` | Partial update via `UpdateDefinition<T>`, auto-stamps `UpdatedAt` |

#### Deletes

| Method | Returns | Description |
|---|---|---|
| `DeleteAsync(id)` | `bool` | Delete by id, returns `true` if deleted |
| `DeleteManyAsync(predicate)` | `long` | Delete all matches, returns count deleted |

#### Raw access

| Member | Type | Description |
|---|---|---|
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

`appsettings.json`:

```json
{
  "MongooseNet": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "myapp"
  }
}
```

---

### Attributes

| Attribute | Target | Description |
|---|---|---|
| `[CollectionName("name")]` | Class | Override the MongoDB collection name |
| `[MongoIndex]` | Property | Declare a single-field index |

`[CollectionName]` validates the name at compile/startup time and rejects:
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

### Soft Delete

MongooseNet supports soft deletes out of the box via `DeletedAt` on `BaseDocument`.

| Method | Returns | Description |
|---|---|---|
| `SoftDeleteAsync(id)` | `bool` | Stamps `DeletedAt`; document stays in MongoDB |
| `RestoreAsync(id)` | `bool` | Clears `DeletedAt`, making the document active again |
| `GetDeletedAsync()` | `List<T>` | Returns all soft-deleted documents |

When `FilterSoftDeleted` is `true` (default), all standard query methods automatically exclude soft-deleted documents.

---

### Streaming

For large collections, use `StreamAsync` to process documents one at a time via a server-side cursor instead of loading everything into memory:

```csharp
await foreach (var user in users.StreamAsync(x => x.IsActive))
{
    // process user
}
```

---

### Transactions

```csharp
await repo.WithTransactionAsync(async session =>
{
    await orders.InsertAsync(order);
    await inventory.UpdateAsync(item.Id, update);
});
```

Commits on success, rolls back automatically on any exception.

---

### Bulk Writes

```csharp
var requests = new List<WriteModel<User>>
{
    new InsertOneModel<User>(newUser),
    new UpdateOneModel<User>(filter, update),
    new DeleteOneModel<User>(deleteFilter),
};

await users.BulkWriteAsync(requests);
```

---

## Changelog

### 1.1.0
- **`CollectionNameAttribute`** now validates names against MongoDB rules at construction time (rejects `$`, null bytes, `system.` prefix, names > 120 UTF-8 bytes)
- **`MongooseOptions`** gains a `Validate()` method and a new `MongooseOptionsValidator` (`IValidateOptions<MongooseOptions>`) for validation when options are bound via `IOptions<>` / `appsettings.json`
- **`MongooseOptions`** table in docs updated to include all properties (`FilterSoftDeleted`, `RetryCount`, `RetryDelay`)
- Exception messages from repository methods no longer include raw MongoDB driver topology details

### 1.0.0
- Initial release

---

## License

MIT © 2026 Ajin Varghese Chandy

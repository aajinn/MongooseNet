using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongooseNet;
using MongooseNet.Example.Models;
using MongooseNet.Exceptions;

namespace MongooseNet.Example.Controllers;

/// <summary>
/// Demonstrates every MongooseNet feature against a User collection.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class UsersController(IMongoRepository<User> users) : ControllerBase
{
    // ── Basic queries ──────────────────────────────────────────────────────────

    /// <summary>Returns all active users (soft-deleted excluded automatically).</summary>
    [HttpGet]
    public Task<List<User>> GetAll(CancellationToken ct)
        => users.GetAllAsync(ct);

    /// <summary>Returns a user by id, or 404 if not found.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            var user = await users.GetByIdRequiredAsync(id, ct);
            return Ok(user);
        }
        catch (DocumentNotFoundException ex)
        {
            return NotFound(new { ex.Message, ex.CollectionName, ex.DocumentId });
        }
    }

    /// <summary>Returns all users matching the given email.</summary>
    [HttpGet("search")]
    public Task<List<User>> Search([FromQuery] string email, CancellationToken ct)
        => users.FindAsync(x => x.Email == email, ct);

    /// <summary>Returns the first user with the given role, or null.</summary>
    [HttpGet("first")]
    public async Task<IActionResult> FindFirst([FromQuery] string role, CancellationToken ct)
    {
        var user = await users.FindOneAsync(x => x.Role == role, ct);
        return user is null ? NotFound() : Ok(user);
    }

    /// <summary>Returns the count of active users.</summary>
    [HttpGet("count")]
    public Task<long> Count(CancellationToken ct)
        => users.CountAsync(x => x.IsActive, ct);

    /// <summary>Returns true if any user has the given email.</summary>
    [HttpGet("exists")]
    public Task<bool> Exists([FromQuery] string email, CancellationToken ct)
        => users.ExistsAsync(x => x.Email == email, ct);

    // ── Projection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight summary (id, name, email, role) — only those fields
    /// are fetched from MongoDB, reducing bandwidth.
    /// </summary>
    [HttpGet("summary")]
    public Task<List<UserSummary>> GetSummaries(CancellationToken ct)
    {
        var projection = Builders<User>.Projection
            .Expression(u => new UserSummary(u.Id, u.Name, u.Email, u.Role));

        return users.FindProjectedAsync(x => x.IsActive, projection, ct);
    }

    // ── Pagination ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated, sorted page of active users.
    /// Count and data queries run in parallel.
    /// </summary>
    [HttpGet("page")]
    public Task<PagedResult<User>> GetPage(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct     = default)
        => users.PageAsync(
            predicate:  x => x.IsActive,
            page:       page,
            pageSize:   pageSize,
            orderBy:    x => x.CreatedAt,
            descending: true,
            ct:         ct);

    // ── Streaming ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Streams all active users via a server-side cursor and returns their emails.
    /// Use StreamAsync for large collections to avoid loading everything into memory.
    /// </summary>
    [HttpGet("stream-emails")]
    public async Task<List<string>> StreamEmails(CancellationToken ct)
    {
        var emails = new List<string>();
        await foreach (var user in users.StreamAsync(x => x.IsActive, ct))
            emails.Add(user.Email);
        return emails;
    }

    // ── Writes ─────────────────────────────────────────────────────────────────

    /// <summary>Creates a single user. PreSave() hashes the password automatically.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        var user = new User
        {
            Name              = req.Name,
            Email             = req.Email,
            Role              = req.Role,
            PlainTextPassword = req.Password,
        };

        await users.InsertAsync(user, ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    /// <summary>Creates multiple users in a single batch.</summary>
    [HttpPost("batch")]
    public async Task<IActionResult> CreateMany([FromBody] List<CreateUserRequest> reqs, CancellationToken ct)
    {
        var docs = reqs.Select(r => new User
        {
            Name              = r.Name,
            Email             = r.Email,
            Role              = r.Role,
            PlainTextPassword = r.Password,
        }).ToList();

        await users.InsertManyAsync(docs, ct);
        return Ok(docs.Select(d => d.Id));
    }

    /// <summary>Partially updates a user's name, role, or active status.</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var updates = new List<UpdateDefinition<User>>();

        if (req.Name     is not null) updates.Add(Builders<User>.Update.Set(x => x.Name,     req.Name));
        if (req.Role     is not null) updates.Add(Builders<User>.Update.Set(x => x.Role,     req.Role));
        if (req.IsActive is not null) updates.Add(Builders<User>.Update.Set(x => x.IsActive, req.IsActive.Value));

        if (updates.Count == 0)
            return BadRequest("No fields to update.");

        var combined = Builders<User>.Update.Combine(updates);
        var updated  = await users.UpdateAsync(id, combined, ct); // auto-stamps UpdatedAt
        return updated ? Ok() : NotFound();
    }

    /// <summary>
    /// Atomically finds a user by email and sets IsActive = true in one round trip.
    /// Returns the updated document, or 404 if not found.
    /// </summary>
    [HttpPatch("activate-by-email")]
    public async Task<IActionResult> ActivateByEmail([FromQuery] string email, CancellationToken ct)
    {
        var update = Builders<User>.Update.Set(x => x.IsActive, true);
        var user   = await users.FindOneAndUpdateAsync(x => x.Email == email, update, returnAfter: true, ct: ct);
        return user is null ? NotFound() : Ok(user);
    }

    // ── Soft delete ────────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-deletes a user — stamps DeletedAt, document stays in MongoDB.
    /// The user will no longer appear in standard queries.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
        => await users.SoftDeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Restores a soft-deleted user by clearing DeletedAt.</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
        => await users.RestoreAsync(id, ct) ? Ok() : NotFound();

    /// <summary>Returns all soft-deleted users.</summary>
    [HttpGet("deleted")]
    public Task<List<User>> GetDeleted(CancellationToken ct)
        => users.GetDeletedAsync(ct);

    // ── Hard delete ────────────────────────────────────────────────────────────

    /// <summary>Permanently deletes a user. Cannot be undone.</summary>
    [HttpDelete("{id:guid}/hard")]
    public async Task<IActionResult> HardDelete(Guid id, CancellationToken ct)
        => await users.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Permanently deletes all inactive users. Returns the count deleted.</summary>
    [HttpDelete("inactive")]
    public Task<long> DeleteInactive(CancellationToken ct)
        => users.DeleteManyAsync(x => !x.IsActive, ct);

    // ── Bulk writes ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates a list of users in a single round trip using BulkWrite.
    /// </summary>
    [HttpPost("bulk-activate")]
    public async Task<IActionResult> BulkActivate([FromBody] BulkActivateRequest req, CancellationToken ct)
    {
        if (req.Ids.Count == 0)
            return BadRequest("At least one id is required.");

        var requests = req.Ids.Select(id =>
            (WriteModel<User>)new UpdateOneModel<User>(
                Builders<User>.Filter.Eq(x => x.Id, id),
                Builders<User>.Update.Set(x => x.IsActive, true)
                                     .Set(x => x.UpdatedAt, DateTime.UtcNow)
            )).ToList();

        var result = await users.BulkWriteAsync(requests, ct: ct);
        return Ok(new { result.ModifiedCount });
    }

    // ── Transactions ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates two users inside a single MongoDB multi-document transaction.
    /// Both are inserted atomically — if either fails, neither is persisted.
    /// </summary>
    [HttpPost("transactional-pair")]
    public async Task<IActionResult> CreatePair(
        [FromBody] List<CreateUserRequest> reqs,
        CancellationToken ct)
    {
        if (reqs.Count != 2)
            return BadRequest("Exactly two users required.");

        var created = new List<User>();

        await users.WithTransactionAsync(async _ =>
        {
            foreach (var req in reqs)
            {
                var user = new User
                {
                    Name              = req.Name,
                    Email             = req.Email,
                    Role              = req.Role,
                    PlainTextPassword = req.Password,
                };
                await users.InsertAsync(user, ct);
                created.Add(user);
            }
        }, ct);

        return Ok(created.Select(u => u.Id));
    }
}

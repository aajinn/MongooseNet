using Microsoft.AspNetCore.Mvc;
using MongooseNet;
using MongooseNet.Example.Models;

namespace MongooseNet.Example.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(MongoRepository<User> users) : ControllerBase
{
    // GET api/users
    [HttpGet]
    public Task<List<User>> GetAll() => users.GetAllAsync();

    // GET api/users/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await users.GetByIdAsync(id);
        return user is null ? NotFound() : Ok(user);
    }

    // GET api/users/search?email=test@test.com  ← Mongoose-style fluent query
    [HttpGet("search")]
    public Task<List<User>> Search([FromQuery] string email)
        => users.FindAsync(x => x.Email == email);

    // POST api/users
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var user = new User
        {
            Name  = req.Name,
            Email = req.Email,
            PlainTextPassword = req.Password  // PreSave() will hash this
        };

        await users.InsertAsync(user);  // PreSave() fires here
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    // DELETE api/users/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
        => await users.DeleteAsync(id) ? NoContent() : NotFound();
}

public record CreateUserRequest(string Name, string Email, string Password);

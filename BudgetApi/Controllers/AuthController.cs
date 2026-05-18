using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Auth;

namespace BudgetApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly BudgetContext _db;
    public AuthController(BudgetContext db) { _db = db; }

    public record AuthRequest(string Username, string Password);

    [HttpPost("register")]
    public async Task<IActionResult> Register(AuthRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Username and password required" });
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new { error = "Username taken" });

        var firstUser = !await _db.Users.AnyAsync();
        var user = new User
        {
            Username = req.Username,
            PasswordHash = PasswordHasher.Hash(req.Password),
            Role = firstUser ? "admin" : "user"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await SignInAsync(user);
        return Ok(new { user.Id, user.Username, user.Role });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(AuthRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid credentials" });
        await SignInAsync(user);
        return Ok(new { user.Id, user.Username, user.Role });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!(User.Identity?.IsAuthenticated ?? false)) return Unauthorized();
        return Ok(new
        {
            Id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0"),
            Username = User.Identity!.Name,
            Role = User.FindFirstValue(ClaimTypes.Role) ?? "user"
        });
    }

    private Task SignInAsync(User u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Username),
            new(ClaimTypes.Role, u.Role),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}

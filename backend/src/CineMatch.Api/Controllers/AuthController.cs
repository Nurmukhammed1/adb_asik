using CineMatch.Api.Auth;
using CineMatch.Api.Contracts;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace CineMatch.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IUserRepository users, IPasswordHasher hasher, JwtTokenService tokens) : ControllerBase
{
    /// <summary>Creates an account and returns a login token.</summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Username : request.DisplayName.Trim();
        var user = await users.CreateAsync(new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = hasher.Hash(request.Password),
            Role = Roles.User,
            Profile = new UserProfile { DisplayName = displayName },
            CreatedAt = DateTime.UtcNow,
        }, ct);

        return Created("/api/users/me", ToResponse(user));
    }

    /// <summary>Logs in with a username or email and a password.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.GetByLoginAsync(request.Username, ct);
        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid username or password.");

        return Ok(ToResponse(user));
    }

    private AuthResponse ToResponse(User user)
    {
        var (token, expiresAt) = tokens.Create(user);
        return new AuthResponse(token, expiresAt, UserDto.From(user));
    }
}

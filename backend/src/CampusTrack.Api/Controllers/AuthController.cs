using CampusTrack.Application.Identity;
using CampusTrack.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CampusTrack.Api.Controllers;

/// <summary>Sign-in, token refresh, and the password lifecycle.</summary>
[AllowAnonymous]
[EnableRateLimiting("auth")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService auth, ILogger<AuthController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    /// <summary>Exchanges credentials for an access token and a refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResult>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>Rotates a refresh token and issues a fresh access token.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResult>> Refresh(RefreshRequest request, CancellationToken ct)
        => Ok(await _auth.RefreshAsync(request, ct));

    /// <summary>Revokes the supplied refresh token.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Returns the signed-in user's profile, roles and effective permissions.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfile), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfile>> Me(CancellationToken ct)
        => Ok(await _auth.GetProfileAsync(RequireUserId(), ct));

    /// <summary>Changes the signed-in user's password and ends their other sessions.</summary>
    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(RequireUserId(), request, ct);
        return NoContent();
    }

    /// <summary>
    /// Starts a password reset. Always reports success, whether or not the address is
    /// registered - answering differently would turn this into an account-discovery tool.
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var token = await _auth.GeneratePasswordResetTokenAsync(request.Email, ct);

        var response = new Dictionary<string, object?>
        {
            ["message"] = "If that email address is registered, a reset link has been sent to it."
        };

        // Without SMTP there is no way to deliver the token, so development surfaces it
        // directly. Guarded by the environment so it can never leak from a real deployment.
        if (token is not null && HttpContext.RequestServices
                .GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            response["developmentToken"] = token;
            _logger.LogWarning("Password reset token returned in the response because this is a development build.");
        }

        return Ok(response);
    }

    /// <summary>Completes a password reset using the emailed token.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    /// <summary>Registers this device for push notifications.</summary>
    [Authorize]
    [HttpPost("device-token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RegisterDeviceToken(RegisterDeviceTokenRequest request, CancellationToken ct)
    {
        await _auth.RegisterDeviceTokenAsync(RequireUserId(), request, ct);
        return NoContent();
    }

    /// <summary>Stops push notifications to this device (called on sign-out).</summary>
    [Authorize]
    [HttpDelete("device-token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveDeviceToken([FromQuery] string token, CancellationToken ct)
    {
        await _auth.RemoveDeviceTokenAsync(RequireUserId(), token, ct);
        return NoContent();
    }
}

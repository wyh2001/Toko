using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OneOf.Types;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using static Toko.Controllers.RoomController;

namespace Toko.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(IConfiguration cfg) : ControllerBase
    {
        private readonly string _jwtKey = cfg["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key");

        [HttpGet("anon")]
        public IActionResult IssueAnonymous()
        {
            // if user is already authenticated, return their existing player info
            if (User?.Identity?.IsAuthenticated == true)
            {
                var existingPlayerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(existingPlayerId))
                {
                    return Ok(new ApiSuccess<object?>("Already authenticated", new
                    {
                        playerName = (string?)null, // Client should use their stored name
                        playerId = existingPlayerId
                    }));
                }
            }

            // generate playerId and display name
            var playerId = Guid.NewGuid().ToString("N");
            var display = $"Driver-{Random.Shared.Next(1000, 9999)}";

            // create JWT claims
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, playerId)
            };

            // issue JWT
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwt = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: creds);
            var token = new JwtSecurityTokenHandler().WriteToken(jwt);

            // write token to HttpOnly cookie
            Response.Cookies.Append("token", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = true,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return Ok(new ApiSuccess<object?>("Anonymous identity issued", new
            {
                playerName = display,
                playerId
            }));
        }
    }
}

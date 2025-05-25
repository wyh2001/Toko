using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OneOf.Types;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Toko.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly string _jwtKey;

        public AuthController(IConfiguration cfg)
        {
            // read JWT key from configuration
            _jwtKey = cfg["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key");
        }

        [HttpGet("anon")]
        public IActionResult IssueAnonymous()
        {
            // if user is already authenticated, return 204 No Content
            if (User?.Identity?.IsAuthenticated == true)
                return NoContent();

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
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return Ok(new
            {
                message = "Anonymous identity issued",
                data = new
                {
                    displayName = display,
                    playerId
                }
            });
        }
    }
}

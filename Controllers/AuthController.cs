using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Toko.Controllers
{
    /// <summary>
    /// 匿名身份签发端点（免注册）。
    /// GET /api/auth/anon  —— 浏览器首访时调用一次即可。
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly string _jwtKey;

        public AuthController(IConfiguration cfg)
        {
            // 从配置读取签名密钥（在 Program.cs 已经注入到配置）
            _jwtKey = cfg["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key");
        }

        /// <summary>
        /// 为未登录用户生成 playerId，并把 JWT 写入 HttpOnly Cookie。
        /// 昵称不写入 Token，改名时前端直接传新值即可。
        /// </summary>
        [HttpGet("anon")]
        public IActionResult IssueAnonymous()
        {
            // 如果请求已经带合法 token，就什么都不做
            if (User?.Identity?.IsAuthenticated == true)
                return NoContent();

            // 1) 生成匿名身份
            var playerId = Guid.NewGuid().ToString("N");
            var display = $"Driver-{Random.Shared.Next(1000, 9999)}"; // 仅返回给前端展示

            // 2) 仅在 token 写入 sub（playerId）等必需 claim
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, playerId)
            };

            // 3) 签发 JWT（30 天有效）
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwt = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: creds);
            var token = new JwtSecurityTokenHandler().WriteToken(jwt);

            // 4) 写 HttpOnly Cookie
            Response.Cookies.Append("token", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = true,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            // 5) 把昵称回给前端存本地（后续可随时改并在请求体传递）
            return Ok(new { displayName = display, playerId });
        }
    }
}

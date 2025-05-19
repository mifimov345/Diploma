using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class LoginController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;

        public LoginController(IUserService userService, IConfiguration configuration)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
            {
                return BadRequest("Username and password are required.");
            }

            var user = await _userService.AuthenticateAsync(model.Username, model.Password);

            if (user == null)
            {
                return Unauthorized("Invalid username or password.");
            }

            try
            {
                var groups = user.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
                var token = GenerateJwtToken(user, groups);
                var responseData = new
                {
                    Token = token,
                    Username = user.UserName,
                    Role = user.Role,
                    Groups = groups,
                    Id = user.Id
                };

                return Ok(responseData);
            }
            catch (Exception)
            {
                return StatusCode(500, "An internal error occurred during login processing.");
            }
        }

        private string GenerateJwtToken(User user, List<string> groups)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, user.Role)
            };

            if (groups != null)
            {
                foreach (var group in groups.Where(g => !string.IsNullOrWhiteSpace(g)))
                {
                    claims.Add(new Claim("group", group));
                }
            }

            var keyString = _configuration["Jwt:Key"];
            var issuer = _configuration["Jwt:Issuer"];
            var audience = _configuration["Jwt:Audience"];

            if (string.IsNullOrEmpty(keyString) || keyString.Length < 32 || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
            {
                throw new InvalidOperationException("JWT configuration (Key/Issuer/Audience) is missing or invalid.");
            }
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public class LoginModel
        {
            [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Username is required.")]
            public string Username { get; set; }

            [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Password is required.")]
            public string Password { get; set; }
        }
    }
}

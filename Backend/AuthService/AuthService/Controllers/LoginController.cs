using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class LoginController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LoginController> _logger;

        public LoginController(IUserService userService, IConfiguration configuration, ILogger<LoginController> logger)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            LogJwtConfiguration();
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public IActionResult Login([FromBody] LoginModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
            {
                _logger.LogWarning("Login attempt with missing username or password.");
                return BadRequest("Username and password are required.");
            }

            _logger.LogInformation("Login attempt for user: {Username}", model.Username);
            var user = _userService.Authenticate(model.Username, model.Password);

            if (user == null)
            {
                _logger.LogWarning("Login failed for user: {Username} - Invalid credentials or user not found.", model.Username);
                return Unauthorized("Invalid username or password."); // 401
            }

            try
            {
                var token = GenerateJwtToken(user);
                var responseData = new
                {
                    Token = token,
                    Username = user.Username,
                    Role = user.Role,
                    Groups = user.Groups ?? new List<string>(),
                    Id = user.Id
                };

                _logger.LogInformation("Login successful for user: {Username}, Role: {Role}", responseData.Username, responseData.Role);
                return Ok(responseData); // 200 OK
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "CRITICAL: Failed to generate JWT token for user {Username} after successful authentication. Check JWT configuration.", user.Username);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred during login processing.");
            }
        }


        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyString = _configuration["Jwt:Key"];
            var issuer = _configuration["Jwt:Issuer"];
            var audience = _configuration["Jwt:Audience"];

            if (string.IsNullOrEmpty(keyString) || keyString.Length < 32 || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
            {
                throw new InvalidOperationException("JWT configuration (Key/Issuer/Audience) is missing or invalid.");
            }
            var key = Encoding.ASCII.GetBytes(keyString);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
            };

            if (user.Groups != null)
            {
                foreach (var group in user.Groups.Where(g => !string.IsNullOrWhiteSpace(g))) // Добавляем только непустые группы
                {
                    claims.Add(new Claim("group", group));
                }
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private void LogJwtConfiguration()
        {
            try
            {
                var key = _configuration["Jwt:Key"];
                var issuer = _configuration["Jwt:Issuer"];
                var audience = _configuration["Jwt:Audience"];
                _logger.LogInformation("--- AuthService JWT Config Loaded (LoginController) ---");
                _logger.LogInformation("Jwt:Key = {KeyStatus}", string.IsNullOrEmpty(key) ? "MISSING" : (key.Length < 32 ? $"SHORT ({key.Length} chars)" : key.Substring(0, 5) + "..."));
                _logger.LogInformation("Jwt:Issuer = {Issuer}", issuer);
                _logger.LogInformation("Jwt:Audience = {Audience}", audience);
                _logger.LogInformation("------------------------------------------------------");
                if (string.IsNullOrEmpty(key) || key.Length < 32)
                {
                    _logger.LogCritical("CRITICAL: JWT Key is missing or too short in configuration! Auth will fail.");
                }
                if (string.IsNullOrEmpty(issuer)) _logger.LogCritical("CRITICAL: JWT Issuer is missing!");
                if (string.IsNullOrEmpty(audience)) _logger.LogCritical("CRITICAL: JWT Audience is missing!");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading JWT configuration in LoginController.");
            }
        }
    }

    public class LoginModel
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Username is required.")]
        public string Username { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; }
    }
}
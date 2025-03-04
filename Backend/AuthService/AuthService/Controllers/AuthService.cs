using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AuthService.Controllers
{
    /// <summary>
    /// Контроллер для аутентификации пользователей.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        /// <summary>
        /// Аутентифицирует пользователя и возвращает JWT-токен при успешном входе.
        /// </summary>
        /// <param name="model">Объект с данными для входа (имя пользователя и пароль).</param>
        /// <returns>
        /// При успешной аутентификации возвращает статус 200 OK с объектом, содержащим JWT-токен.
        /// Если данные некорректны, возвращает 401 Unauthorized.
        /// </returns>
        /// <remarks>
        /// Используйте данный эндпоинт, передав в теле запроса JSON-объект с полями <c>Username</c> и <c>Password</c>.
        /// Пример запроса:
        /// <code>
        /// {
        ///   "username": "admin",
        ///   "password": "password"
        /// }
        /// </code>
        /// </remarks>
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginModel model)
        {
            // Простейшая проверка: замените на проверку через базу данных или ORM в реальном проекте.
            if (model.Username == "admin" && model.Password == "password")
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes("c036fd2a60b5cb97b364182eff8123f6");
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, model.Username) }),
                    Expires = DateTime.UtcNow.AddHours(1),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                return Ok(new { Token = tokenHandler.WriteToken(token) });
            }
            return Unauthorized();
        }
    }

    /// <summary>
    /// Модель для передачи данных при входе в систему.
    /// </summary>
    public class LoginModel
    {
        /// <summary>
        /// Имя пользователя.
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// Пароль пользователя.
        /// </summary>
        public string Password { get; set; }
    }
}

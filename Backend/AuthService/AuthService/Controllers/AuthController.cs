using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;

        public AuthController(IUserService userService)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        [HttpPost("users")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var creatorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var creatorRole = User.FindFirstValue(ClaimTypes.Role);

            try
            {
                User newUser;
                var userToCreate = new User { UserName = model.Username };

                if (creatorRole == "SuperAdmin")
                {
                    if (model.Role == "Admin")
                    {
                        if (model.Groups == null || !model.Groups.Any(g => !string.IsNullOrWhiteSpace(g)))
                            return BadRequest("Initial group(s) are required when creating an Admin.");

                        newUser = await _userService.CreateAdminAsync(userToCreate, model.Password, model.Groups.Where(g => !string.IsNullOrWhiteSpace(g)).ToList(), creatorId);
                    }
                    else
                    {
                        newUser = await _userService.CreateUserBySuperAdminAsync(userToCreate, model.Password, model.Groups?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? new List<string>());
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(model.Role) && model.Role != "User")
                        return Forbid("Admins can only create users with the 'User' role.");
                    if (model.Groups == null || model.Groups.Count != 1 || string.IsNullOrWhiteSpace(model.Groups[0]))
                        return BadRequest("You must specify exactly one valid group for the new user.");

                    string targetGroup = model.Groups[0].Trim();
                    newUser = await _userService.CreateUserByAdminAsync(userToCreate, model.Password, creatorId, targetGroup);
                }

                var groups = await _userService.GetUserGroupsAsync(newUser.Id);
                var result = new
                {
                    newUser.Id,
                    Username = newUser.UserName,
                    newUser.Role,
                    Groups = groups.Select(g => g.Name).ToList(),
                    newUser.CreatedByAdminId
                };
                return CreatedAtAction(nameof(GetUserById), new { id = newUser.Id }, result);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (Exception ex) when (ex.Message.Contains("is already taken", StringComparison.OrdinalIgnoreCase))
            { return Conflict(new { message = ex.Message }); }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An internal server error occurred during user creation." });
            }
        }

        [HttpGet("users")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetUsers()
        {
            var requestingUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var requestingUserRole = User.FindFirstValue(ClaimTypes.Role);

            IEnumerable<User> users;
            if (requestingUserRole == "SuperAdmin")
            {
                users = await _userService.GetAllAsync();
            }
            else
            {
                var admin = await _userService.GetByIdAsync(requestingUserId);
                if (admin == null || admin.UserGroups == null || !admin.UserGroups.Any())
                {
                    users = Enumerable.Empty<User>();
                }
                else
                {
                    var groupNames = admin.UserGroups.Select(ug => ug.Group.Name).ToList();
                    users = await _userService.GetUsersInGroupsAsync(groupNames);
                }
            }

            var result = new List<object>();
            foreach (var u in users)
            {
                var groups = u.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
                result.Add(new
                {
                    u.Id,
                    Username = u.UserName,
                    u.Role,
                    Groups = groups,
                    u.CreatedByAdminId
                });
            }

            return Ok(result);
        }

        [HttpGet("users/{id:int}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetUserById(int id)
        {
            var requestingUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var requestingUserRole = User.FindFirstValue(ClaimTypes.Role);

            var user = await _userService.GetByIdAsync(id);
            if (user == null)
                return NotFound();

            if (requestingUserRole == "Admin")
            {
                var admin = await _userService.GetByIdAsync(requestingUserId);
                if (admin == null || admin.UserGroups == null)
                    return Forbid("Access denied due to inconsistent admin data.");

                bool isSelf = user.Id == requestingUserId;
                var adminGroupNames = admin.UserGroups.Select(ug => ug.Group.Name).ToList();
                var userGroupNames = user.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
                bool isInAdminGroup = userGroupNames.Any(g => adminGroupNames.Contains(g));

                if (!isSelf && !isInAdminGroup)
                    return Forbid("You do not have permission to view this user.");
            }

            var groups = user.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
            var result = new
            {
                user.Id,
                Username = user.UserName,
                user.Role,
                Groups = groups,
                user.CreatedByAdminId
            };
            return Ok(result);
        }

        [HttpPut("users/{id}/groups")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> UpdateUserGroups(int id, [FromBody] List<string> groupNames)
        {
            if (groupNames == null)
                return BadRequest("Group list cannot be null. Send an empty array [] to remove all groups.");

            var validGroupNames = groupNames.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();

            try
            {
                await _userService.UpdateUserGroupsAsync(id, validGroupNames);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An internal error occurred while updating user groups." });
            }
        }

        [HttpDelete("users/{id:int}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var deletedById = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var deletedByRole = User.FindFirstValue(ClaimTypes.Role);

            if (id == deletedById)
                return BadRequest("You cannot delete your own account.");

            try
            {
                await _userService.DeleteUserAsync(id, deletedById, deletedByRole);
                return NoContent();
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return Forbid(ex.Message); }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An internal error occurred while deleting the user." });
            }
        }

        [HttpPut("users/{id}/username")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> UpdateUsername(int id, [FromBody] UpdateUsernameModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);

            try
            {
                await _userService.UpdateUsernameAsync(id, model.NewUsername, currentUserId, currentUserRole);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception)
            {
                return StatusCode(500, "An internal error occurred.");
            }
        }

        [HttpPut("users/{id}/password")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> UpdatePassword(int id, [FromBody] UpdatePasswordModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);

            try
            {
                await _userService.UpdatePasswordAsync(id, model.NewPassword, currentUserId, currentUserRole);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception)
            {
                return StatusCode(500, "An internal error occurred.");
            }
        }

        [HttpPost("groups")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> CreateGroup([FromBody] GroupCreateModel model)
        {
            var creatorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var creatorRole = User.FindFirstValue(ClaimTypes.Role);

            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(model.GroupName))
                return BadRequest("GroupName cannot be empty or whitespace.");

            var groupNameToCreate = model.GroupName.Trim();
            if (groupNameToCreate.Equals("System", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Group name 'System' is reserved.");

            try
            {
                bool created = await _userService.CreateGroupAsync(groupNameToCreate, creatorId, creatorRole);
                if (created)
                    return Ok(new { message = $"Group '{groupNameToCreate}' created successfully." });
                else
                    return Conflict(new { message = $"Group '{groupNameToCreate}' already exists." });
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An error occurred while creating the group." });
            }
        }

        [HttpGet("groups")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetGroups()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            IEnumerable<string> groups;
            if (userRole == "SuperAdmin")
            {
                groups = await _userService.GetAllGroupNamesAsync();
            }
            else
            {
                groups = await _userService.GetGroupsForAdminAsync(userId);
            }
            return Ok(groups ?? new List<string>());
        }

        [HttpDelete("groups/{groupName}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> DeleteGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return BadRequest("Group name is required.");
            if (groupName.Equals("System", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Cannot delete the 'System' group.");

            try
            {
                await _userService.DeleteGroupAsync(groupName);
                return NoContent();
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = $"An error occurred while deleting group '{groupName}'. Check server logs." });
            }
        }
    }

    public class UpdateUsernameModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MinLength(3)]
        public string NewUsername { get; set; }
    }

    public class UpdatePasswordModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MinLength(6)]
        public string NewPassword { get; set; }
    }

    public class CreateUserModel
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Username is required.")]
        [System.ComponentModel.DataAnnotations.MinLength(3, ErrorMessage = "Username must be at least 3 characters long.")]
        public string Username { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Password is required.")]
        [System.ComponentModel.DataAnnotations.MinLength(6, ErrorMessage = "Password must be at least 6 characters long.")]
        public string Password { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Role is required.")]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^(Admin|User)$", ErrorMessage = "Role must be 'Admin' or 'User'.")]
        public string Role { get; set; }

        public List<string> Groups { get; set; }
    }

    public class GroupCreateModel
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "GroupName is required.")]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "Group name can only contain letters, numbers, underscore, hyphen, and period.")]
        [System.ComponentModel.DataAnnotations.MaxLength(50, ErrorMessage = "Group name cannot exceed 50 characters.")]
        public string GroupName { get; set; }
    }
}

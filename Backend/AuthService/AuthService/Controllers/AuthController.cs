using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;


namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IUserService userService, ILogger<AuthController> logger)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("users")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult CreateUser([FromBody] CreateUserModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                //_logger.LogWarning("CreateUser validation failed: {ValidationErrors}", string.Join("; ", errors));
                return BadRequest(ModelState);
            }

            var creatorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var creatorRole = User.FindFirstValue(ClaimTypes.Role);
            //_logger.LogInformation("User creation attempt by UserID: {CreatorId}, Role: {CreatorRole} for new user: {NewUsername}, Role: {NewRole}",
            //    creatorId, creatorRole, model.Username, model.Role);

            try
            {
                User newUser;
                var userToCreate = new User { Username = model.Username };
                if (creatorRole == Roles.SuperAdmin) // Если создает SuperAdmin
                {
                    if (model.Role == Roles.Admin)
                    {
                        if (model.Groups == null || !model.Groups.Any(g => !string.IsNullOrWhiteSpace(g)))
                        {
                            return BadRequest("Initial group(s) are required when creating an Admin.");
                        }
                        newUser = _userService.CreateAdmin(userToCreate, model.Password, model.Groups.Where(g => !string.IsNullOrWhiteSpace(g)).ToList(), creatorId);
                    }
                    else 
                    {
                        newUser = _userService.CreateUserBySuperAdmin(userToCreate, model.Password, model.Groups?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? new List<string>());
                    }
                }
                else
                {
                    if (model.Role != null && model.Role != Roles.User)
                    {
                        _logger.LogWarning("Admin ID {AdminId} attempted to create a non-User role.", creatorId);
                        return Forbid("Admins can only create users with the 'User' role.");
                    }
                    if (model.Groups == null || model.Groups.Count != 1 || string.IsNullOrWhiteSpace(model.Groups[0]))
                    {

                        _logger.LogWarning("Admin ID {AdminId} must specify exactly one valid group when creating a user.", creatorId);
                        return BadRequest("You must specify exactly one valid group for the new user.");
                }
                    
                    string targetGroup = model.Groups[0].Trim();
                    
                    newUser = _userService.CreateUserByAdmin(userToCreate, model.Password, creatorId, targetGroup);

                }

                var result = new { newUser.Id, newUser.Username, newUser.Role, newUser.Groups };
                return CreatedAtAction(nameof(GetUserById), new { id = newUser.Id }, result);
            }
            catch (ArgumentException ex)
            {
                //_logger.LogWarning(ex, "Argument error during user creation for {NewUsername}: {ErrorMessage}", model.Username, ex.Message);
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                //_logger.LogWarning(ex, "Business logic error during user creation for {NewUsername}: {ErrorMessage}", model.Username, ex.Message);
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex) when (ex.Message.Contains("is already taken", StringComparison.OrdinalIgnoreCase))
            {
                //_logger.LogWarning("Attempt to create user with duplicate username '{DuplicateUsername}'.", model.Username);
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Generic error during user creation for {NewUsername}", model.Username);
                return StatusCode(500, new { message = "An internal server error occurred during user creation." });
            }
        }

        [HttpGet("users")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult GetUsers()
        {
            var requestingUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var requestingUserRole = User.FindFirstValue(ClaimTypes.Role);
            //_logger.LogInformation("GetUsers request by UserID: {UserId}, Role: {UserRole}", requestingUserId, requestingUserRole);

            IEnumerable<User> users;

            if (requestingUserRole == Roles.SuperAdmin)
            {
                users = _userService.GetAll();
            }
            else
            {
                var admin = _userService.GetById(requestingUserId);
                if (admin == null || admin.Groups == null || !admin.Groups.Any())
                {
                    //_logger.LogWarning("Admin UserID: {AdminId} has no groups or not found, returning empty user list for GetUsers request.", requestingUserId);
                    users = Enumerable.Empty<User>();
                }
                else
                {
                    users = _userService.GetUsersInGroups(admin.Groups);
                }
            }

            var result = users.Select(u => new { u.Id, u.Username, u.Role, u.Groups, u.CreatedByAdminId }).ToList();
            //_logger.LogInformation("Returning {UserCount} users for request by UserID: {UserId}", result.Count, requestingUserId);
            return Ok(result);
        }

        [HttpGet("users/{id:int}")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult GetUserById(int id)
        {
            var requestingUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var requestingUserRole = User.FindFirstValue(ClaimTypes.Role);
           // _logger.LogInformation("GetUserById request for ID: {TargetUserId} by UserID: {RequesterId}, Role: {RequesterRole}", id, requestingUserId, requestingUserRole);

            var user = _userService.GetById(id);
            if (user == null)
            {
                //_logger.LogWarning("User with ID: {TargetUserId} not found for GetUserById request by UserID: {RequesterId}", id, requestingUserId);
                return NotFound();
            }

            if (requestingUserRole == Roles.Admin)
            {
                var admin = _userService.GetById(requestingUserId);
                if (admin == null || admin.Groups == null)
                {
                    //_logger.LogError("Inconsistent state: Admin ID {AdminId} requesting user data not found or has null groups.", requestingUserId);
                    return Forbid("Access denied due to inconsistent admin data.");
                }

                bool isSelf = user.Id == requestingUserId;
                bool isInAdminGroup = user.Groups != null && user.Groups.Any(ug => admin.Groups.Contains(ug));

                if (!isSelf && !isInAdminGroup)
                {
                    //_logger.LogWarning("Forbidden attempt by Admin ID: {AdminId} to view User ID: {TargetUserId} (not self or in managed group)", requestingUserId, id);
                    return Forbid("You do not have permission to view this user.");
                }
            }

            var result = new { user.Id, user.Username, user.Role, user.Groups, user.CreatedByAdminId };
            return Ok(result);
        }

        [HttpPut("users/{id:int}/groups")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult UpdateUserGroups(int id, [FromBody] List<string> groupNames)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            //_logger.LogInformation("UpdateUserGroups request for UserID: {TargetUserId} by SuperAdmin ID: {AdminId}", id, adminId);

            if (groupNames == null)
            {
                //_logger.LogWarning("UpdateUserGroups failed for UserID {TargetUserId}: Received null instead of a group list.", id);
                return BadRequest("Group list cannot be null. Send an empty array [] to remove all groups.");
            }

            var validGroupNames = groupNames.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();
            //_logger.LogDebug("Updating groups for UserID {TargetUserId} with: [{ValidGroups}]", id, string.Join(", ", validGroupNames));

            try
            {
                _userService.UpdateUserGroups(id, validGroupNames);
                //_logger.LogInformation("Successfully updated groups for UserID: {TargetUserId}", id);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                //_logger.LogWarning("UpdateUserGroups failed: UserID {TargetUserId} not found.", id);
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                //_logger.LogWarning(ex, "UpdateUserGroups failed for UserID {TargetUserId} due to argument error: {ErrorMessage}", id, ex.Message);
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                //_logger.LogWarning(ex, "UpdateUserGroups failed for UserID {TargetUserId} due to business logic error: {ErrorMessage}", id, ex.Message);
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error updating groups for UserID {TargetUserId}", id);
                return StatusCode(500, new { message = "An internal error occurred while updating user groups." });
            }
        }

        [HttpDelete("users/{id:int}")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult DeleteUser(int id)
        {
            var deletedById = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var deletedByRole = User.FindFirstValue(ClaimTypes.Role);
            //_logger.LogInformation("DeleteUser request for UserID: {TargetUserId} by UserID: {DeleterId}, Role: {DeleterRole}", id, deletedById, deletedByRole);

            if (id == deletedById)
            {
                //_logger.LogWarning("Attempt by UserID: {DeleterId} to delete own account.", deletedById);
                return BadRequest("You cannot delete your own account.");
            }

            try
            {
                _userService.DeleteUser(id, deletedById, deletedByRole);
                //_logger.LogInformation("Successfully deleted UserID: {TargetUserId} by UserID: {DeleterId}", id, deletedById);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                //_logger.LogWarning("DeleteUser failed: UserID {TargetUserId} not found for deletion by UserID: {DeleterId}.", id, deletedById);
                return NotFound(); 
            }
            catch (InvalidOperationException ex)
            {
                //_logger.LogWarning(ex, "DeleteUser forbidden (e.g., deleting SuperAdmin) for UserID {TargetUserId} by UserID: {DeleterId}.", id, deletedById);
                return Forbid(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                //_logger.LogWarning(ex, "DeleteUser unauthorized for UserID {TargetUserId} by UserID: {DeleterId}, Role: {DeleterRole}.", id, deletedById, deletedByRole);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error deleting UserID {TargetUserId} by UserID: {DeleterId}", id, deletedById);
                return StatusCode(500, new { message = "An internal error occurred while deleting the user." });
            }
        }



        [HttpPost("groups")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult CreateGroup([FromBody] GroupCreateModel model)
        {
            var creatorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var creatorRole = User.FindFirstValue(ClaimTypes.Role);            
            //_logger.LogInformation("CreateGroup requested: '{GroupName}' by SuperAdmin ID: {AdminId}", model?.GroupName, adminId);

            if (!ModelState.IsValid)
            {
                //_logger.LogWarning("CreateGroup failed validation for SuperAdmin ID: {AdminId}. Model: {@Model}", adminId, model);
                return BadRequest(ModelState);
            }
            if (string.IsNullOrWhiteSpace(model.GroupName))
            {
                //_logger.LogWarning("CreateGroup failed: GroupName is empty or whitespace for SuperAdmin ID: {AdminId}.", adminId);
                return BadRequest("GroupName cannot be empty or whitespace.");
            }

            var groupNameToCreate = model.GroupName.Trim();

            if (groupNameToCreate.Equals("System", StringComparison.OrdinalIgnoreCase)){
                //_logger.LogWarning("Attempt to create reserved group name 'System' by User ID: {CreatorId}", creatorId);
                return BadRequest("Group name 'System' is reserved.");
            }
            try
            {
                bool created = _userService.CreateGroup(groupNameToCreate, creatorId, creatorRole);
                if (created)
                {
                    //_logger.LogInformation("Group '{GroupName}' created successfully by User ID: {CreatorId} (Role: {CreatorRole}).", groupNameToCreate, 
                    return Ok(new { message = $"Group '{groupNameToCreate}' created successfully." });
                }
                else
                {
                    //_logger.LogWarning("Attempt to create existing group '{GroupName}' by User ID: {CreatorId}.", groupNameToCreate, creatorId);
                    return Conflict(new { message = $"Group '{groupNameToCreate}' already exists." });
                }
            }
            catch (ArgumentException ex)
            {
                //_logger.LogWarning(ex, "Argument error creating group '{GroupName}' by SuperAdmin ID: {AdminId}.", groupNameToCreate, adminId);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error creating group '{GroupName}' by SuperAdmin ID: {AdminId}.", groupNameToCreate, adminId);
                return StatusCode(500, new { message = "An error occurred while creating the group." });
            }
        }

        [HttpGet("groups")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult GetGroups()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            IEnumerable<string> groups;
            if (userRole == Roles.SuperAdmin)
            {
                groups = _userService.GetAllGroupNames();
            }
            else
            {
                groups = _userService.GetGroupsForAdmin(userId);
            }
            //_logger.LogInformation("GetGroups requested by SuperAdmin ID: {AdminId}", adminId);

            var groupsList = groups as List<string> ?? groups?.ToList();
            var groupsType = groups?.GetType().FullName ?? "null";
            //_logger.LogInformation("--- AuthService GetGroups: Returning Ok. Result Type: {GroupsType}, Count: {GroupsCount}", groupsType, groupsList?.Count ?? -1);
            if (groupsList != null && groupsList.Count > 0)
            {
                _logger.LogInformation("--- AuthService GetGroups: Groups sample: [{GroupsSample}]", string.Join(", ", groupsList));
            }

            return Ok(groups ?? new List<string>());
        }

        [HttpDelete("groups/{groupName}")]
        [Authorize(Roles = Roles.SuperAdmin)]
        public IActionResult DeleteGroup(string groupName)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            //_logger.LogInformation("DeleteGroup requested: '{GroupName}' by SuperAdmin ID: {AdminId}", groupName, adminId);

            if (string.IsNullOrWhiteSpace(groupName))
            {
                //_logger.LogWarning("DeleteGroup failed: GroupName is empty or whitespace for SuperAdmin ID: {AdminId}.", adminId);
                return BadRequest("Group name is required.");
            }

            if (groupName.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                //_logger.LogWarning("Attempt to delete system group 'System' by SuperAdmin ID: {AdminId}.", adminId);
                return BadRequest("Cannot delete the 'System' group.");
            }

            try
            {
                _userService.DeleteGroup(groupName);
                //_logger.LogInformation("Successfully deleted group '{GroupName}' by SuperAdmin ID: {AdminId}. Affected users had the group removed.", groupName, adminId);
                return NoContent();
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error deleting group '{GroupName}' by SuperAdmin ID: {AdminId}. Check potentially affected Admin users.", groupName, adminId);
                return StatusCode(500, new { message = $"An error occurred while deleting group '{groupName}'. Check server logs." });
            }
        }
        [HttpPut("users/{id}/username")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult UpdateUsername(int id, [FromBody] UpdateUsernameModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);

            try
            {
                _userService.UpdateUsername(id, model.NewUsername, currentUserId, currentUserRole);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error updating username for user ID {UserId}", id);
                return StatusCode(500, "An internal error occurred.");
            }
        }

        [HttpPut("users/{id}/password")]
        [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
        public IActionResult UpdatePassword(int id, [FromBody] UpdatePasswordModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);

            try
            {
                _userService.UpdatePassword(id, model.NewPassword, currentUserId, currentUserRole);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error updating password for user ID {UserId}", id);
                return StatusCode(500, "An internal error occurred.");
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
        [System.ComponentModel.DataAnnotations.RegularExpression($"^({Roles.Admin}|{Roles.User})$", ErrorMessage = "Role must be 'Admin' or 'User'.")]
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
using AuthService.Models;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System;

namespace AuthService.Services
{
    public class UserService : IUserService
    {
        private readonly AuthDbContext _context;

        public UserService(AuthDbContext context)
        {
            _context = context;
        }

        public async Task<User> AuthenticateAsync(string username, string password)
        {
            var user = await _context.Users
                .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                .SingleOrDefaultAsync(x => x.UserName == username);

            if (user == null || !VerifyPasswordHash(password, user.PasswordHash))
                return null;

            return user;
        }

        public async Task<User> GetByIdAsync(int id)
        {
            return await _context.Users
                .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<IEnumerable<User>> GetAllAsync()
        {
            return await _context.Users
                .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                .ToListAsync();
        }

        public async Task<User> GetByUsernameAsync(string username)
        {
            return await _context.Users
                .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                .FirstOrDefaultAsync(u => u.UserName == username);
        }

        public async Task<User> CreateAdminAsync(User user, string password, List<string> initialGroups, int creatorId)
        {
            if (string.IsNullOrWhiteSpace(user.UserName)) throw new ArgumentException("Username is required.");
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required.");
            if (await _context.Users.AnyAsync(u => u.UserName == user.UserName))
                throw new Exception($"Username \"{user.UserName}\" is already taken.");

            if (initialGroups == null || !initialGroups.Any())
                throw new ArgumentException("Admin must be assigned to at least one group upon creation.");

            var existingGroups = await _context.Groups.Select(g => g.Name).ToListAsync();
            if (!initialGroups.All(g => existingGroups.Contains(g)))
                throw new ArgumentException("One or more specified groups do not exist.");

            user.PasswordHash = HashPassword(password);
            user.Role = "Admin";
            user.CreatedByAdminId = creatorId;

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            foreach (var groupName in initialGroups.Distinct())
            {
                var group = await _context.Groups.FirstOrDefaultAsync(g => g.Name == groupName);
                if (group != null)
                    _context.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
            }
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<User> CreateUserByAdminAsync(User user, string password, int adminCreatorId, string targetGroup)
        {
            if (string.IsNullOrWhiteSpace(user.UserName)) throw new ArgumentException("Username is required.");
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required.");
            if (await _context.Users.AnyAsync(u => u.UserName == user.UserName))
                throw new Exception($"Username \"{user.UserName}\" is already taken.");

            var admin = await _context.Users.Include(u => u.UserGroups).ThenInclude(ug => ug.Group).FirstOrDefaultAsync(u => u.Id == adminCreatorId && u.Role == "Admin");
            if (admin == null) throw new InvalidOperationException("Invalid admin creator specified.");

            var adminGroups = admin.UserGroups.Select(ug => ug.Group.Name).ToList();
            if (!adminGroups.Contains(targetGroup))
                throw new Exception($"Admin '{admin.UserName}' has no groups assigned and cannot create users.");

            user.PasswordHash = HashPassword(password);
            user.Role = "User";
            user.CreatedByAdminId = adminCreatorId;

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var group = await _context.Groups.FirstOrDefaultAsync(g => g.Name == targetGroup);
            if (group != null)
                _context.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });

            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<User> CreateUserBySuperAdminAsync(User user, string password, List<string> initialGroups)
        {
            if (string.IsNullOrWhiteSpace(user.UserName)) throw new ArgumentException("Username is required.");
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required.");
            if (await _context.Users.AnyAsync(u => u.UserName == user.UserName))
                throw new Exception($"Username \"{user.UserName}\" is already taken.");

            if (initialGroups != null && initialGroups.Any())
            {
                var groupNamesDb = await _context.Groups.Select(g => g.Name).ToListAsync();
                if (!initialGroups.All(g => groupNamesDb.Contains(g)))
                    throw new ArgumentException("One or more specified groups do not exist.");
            }

            user.PasswordHash = HashPassword(password);
            user.Role = "User";
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            if (initialGroups != null)
            {
                foreach (var groupName in initialGroups.Distinct())
                {
                    var group = await _context.Groups.FirstOrDefaultAsync(g => g.Name == groupName);
                    if (group != null)
                        _context.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
                }
                await _context.SaveChangesAsync();
            }
            return user;
        }

        public async Task<IEnumerable<string>> GetAllGroupNamesAsync()
        {
            return await _context.Groups.Select(g => g.Name).ToListAsync();
        }

        public async Task<IEnumerable<string>> GetGroupsForAdminAsync(int adminId)
        {
            var user = await _context.Users.Include(u => u.UserGroups).ThenInclude(ug => ug.Group).FirstOrDefaultAsync(u => u.Id == adminId && u.Role == "Admin");
            if (user == null) return Enumerable.Empty<string>();
            return user.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
        }

        public async Task<bool> CreateGroupAsync(string groupName, int creatorId, string creatorRole)
        {
            if (string.IsNullOrWhiteSpace(groupName)) throw new ArgumentException("Group name cannot be empty.");
            if (await _context.Groups.AnyAsync(g => g.Name == groupName)) return false;

            var group = new Group { Name = groupName };
            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            var creator = await _context.Users.FirstOrDefaultAsync(u => u.Id == creatorId);
            if (creatorRole == "Admin" && creator != null)
            {
                if (!await _context.UserGroups.AnyAsync(ug => ug.UserId == creator.Id && ug.GroupId == group.Id))
                    _context.UserGroups.Add(new UserGroup { UserId = creator.Id, GroupId = group.Id });
                await _context.SaveChangesAsync();
            }
            return true;
        }

        public async Task DeleteGroupAsync(string groupName)
        {
            var group = await _context.Groups.Include(g => g.UserGroups).FirstOrDefaultAsync(g => g.Name == groupName);
            if (group == null) return;
            if (group.Name == "System") throw new InvalidOperationException("Cannot delete the 'System' group.");

            _context.UserGroups.RemoveRange(group.UserGroups);
            _context.Groups.Remove(group);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserGroupsAsync(int userId, List<string> groupNames)
        {
            var user = await _context.Users.Include(u => u.UserGroups).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found.");

            var allGroups = await _context.Groups.Select(g => g.Name).ToListAsync();
            if (groupNames != null && !groupNames.All(g => allGroups.Contains(g)))
                throw new ArgumentException("One or more specified groups do not exist.");

            if (user.Role == "Admin" && (groupNames == null || !groupNames.Any()))
                throw new InvalidOperationException("Cannot remove the last group from an Admin user.");

            var currentGroupLinks = _context.UserGroups.Where(ug => ug.UserId == userId);
            _context.UserGroups.RemoveRange(currentGroupLinks);
            await _context.SaveChangesAsync();

            if (groupNames != null)
            {
                foreach (var groupName in groupNames.Distinct())
                {
                    var group = await _context.Groups.FirstOrDefaultAsync(g => g.Name == groupName);
                    if (group != null)
                        _context.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteUserAsync(int userId, int deletedById, string deletedByRole)
        {
            var userToDelete = await _context.Users.Include(u => u.UserGroups).FirstOrDefaultAsync(u => u.Id == userId);
            if (userToDelete == null) throw new KeyNotFoundException();

            if (userToDelete.Role == "SuperAdmin")
                throw new InvalidOperationException("Cannot delete the SuperAdmin account.");

            if (deletedByRole == "SuperAdmin")
            {
                _context.Users.Remove(userToDelete);
            }
            else if (deletedByRole == "Admin")
            {
                var admin = await _context.Users.Include(u => u.UserGroups).ThenInclude(ug => ug.Group).FirstOrDefaultAsync(u => u.Id == deletedById);
                if (admin == null) throw new UnauthorizedAccessException("Admin performing deletion not found.");

                if (userToDelete.Role != "User")
                    throw new UnauthorizedAccessException("Admins can only delete Users.");

                var adminGroupNames = admin.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
                var userGroupNames = userToDelete.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
                if (!userGroupNames.Any(g => adminGroupNames.Contains(g)))
                    throw new UnauthorizedAccessException("Admin cannot delete users outside of their groups.");

                _context.Users.Remove(userToDelete);
            }
            else
            {
                throw new UnauthorizedAccessException("Insufficient permissions to delete user.");
            }
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<User>> GetUsersInGroupsAsync(List<string> groupNames)
        {
            return await _context.Users
                .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                .Where(u => u.UserGroups.Any(ug => groupNames.Contains(ug.Group.Name)))
                .ToListAsync();
        }

        public async Task<List<Group>> GetUserGroupsAsync(int userId)
        {
            return await _context.Groups
                .Where(g => g.UserGroups.Any(ug => ug.UserId == userId))
                .ToListAsync();
        }

        public async Task UpdateUsernameAsync(int userIdToUpdate, string newUsername, int currentUserId, string currentUserRole)
        {
            if (string.IsNullOrWhiteSpace(newUsername)) throw new ArgumentException("New username cannot be empty.");

            var userToUpdate = await _context.Users.FirstOrDefaultAsync(u => u.Id == userIdToUpdate);
            if (userToUpdate == null)
                throw new KeyNotFoundException($"User with ID {userIdToUpdate} not found.");

            if (await _context.Users.AnyAsync(u => u.Id != userIdToUpdate && u.UserName == newUsername))
                throw new InvalidOperationException($"Username '{newUsername}' is already taken.");

            await CheckUpdatePermissions(userToUpdate, currentUserId, currentUserRole, "update username");

            userToUpdate.UserName = newUsername;
            await _context.SaveChangesAsync();
        }

        public async Task UpdatePasswordAsync(int userIdToUpdate, string newPassword, int currentUserId, string currentUserRole)
        {
            if (string.IsNullOrWhiteSpace(newPassword)) throw new ArgumentException("New password cannot be empty.");

            var userToUpdate = await _context.Users.FirstOrDefaultAsync(u => u.Id == userIdToUpdate);
            if (userToUpdate == null)
                throw new KeyNotFoundException($"User with ID {userIdToUpdate} not found.");

            await CheckUpdatePermissions(userToUpdate, currentUserId, currentUserRole, "update password");

            userToUpdate.PasswordHash = HashPassword(newPassword);
            await _context.SaveChangesAsync();
        }

        private async Task CheckUpdatePermissions(User userToModify, int currentUserId, string currentUserRole, string action)
        {
            if (userToModify.Id == currentUserId)
                throw new InvalidOperationException($"Users cannot {action} for themselves through this method.");

            if (userToModify.Role == "SuperAdmin")
                throw new UnauthorizedAccessException($"Cannot {action} for a SuperAdmin.");

            if (currentUserRole == "SuperAdmin")
            {
                if (userToModify.Role == "Admin" || userToModify.Role == "User")
                    return;
            }
            else if (currentUserRole == "Admin")
            {
                if (userToModify.Role == "User")
                {
                    var admin = await _context.Users.Include(u => u.UserGroups).ThenInclude(ug => ug.Group).FirstOrDefaultAsync(u => u.Id == currentUserId);
                    var adminGroupNames = admin.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
                    var userGroupNames = userToModify.UserGroups?.Select(ug => ug.Group.Name).ToList() ?? new List<string>();
                    if (userGroupNames.Any(g => adminGroupNames.Contains(g)))
                        return;
                    else
                        throw new UnauthorizedAccessException($"Admin can only {action} for users within their groups.");
                }
            }
            throw new UnauthorizedAccessException($"Insufficient permissions to {action} for user ID {userToModify.Id}.");
        }

        private static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashed = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashed);
            }
        }

        private static bool VerifyPasswordHash(string password, string storedHash)
        {
            var hash = HashPassword(password);
            return hash == storedHash;
        }
    }
}

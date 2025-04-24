using AuthService.Models;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System;
using Microsoft.Extensions.Logging;

namespace AuthService.Services
{
    public static class Roles
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string User = "User";
    }

    public interface IUserService
    {
        User Authenticate(string username, string password);
        User GetById(int id);
        IEnumerable<User> GetAll();
        IEnumerable<User> GetUsersInGroups(List<string> groupNames);
        User CreateAdmin(User user, string password, List<string> initialGroups, int creatorId);
        User CreateUserByAdmin(User user, string password, int adminCreatorId, string targetGroup);
        User CreateUserBySuperAdmin(User user, string password, List<string> initialGroups);
        User GetByUsername(string username);
        void UpdateUserGroups(int userId, List<string> groupNames);
        void DeleteUser(int userId, int deletedById, string deletedByRole);
        void UpdateUsername(int userIdToUpdate, string newUsername, int currentUserId, string currentUserRole);
        void UpdatePassword(int userIdToUpdate, string newPassword, int currentUserId, string currentUserRole);

        IEnumerable<string> GetAllGroupNames();
        IEnumerable<string> GetGroupsForAdmin(int adminId);
        bool CreateGroup(string groupName);
        bool CreateGroup(string groupName, int creatorId, string creatorRole);
        void DeleteGroup(string groupName);
    }

    public class UserService : IUserService
    {
        private readonly List<User> _users = new List<User>();
        private readonly List<string> _groups = new List<string>();
        private int _nextId = 1;

        public UserService()//ILogger<UserService> logger)
        {
            // _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Console.WriteLine("--- Initializing UserService (No Logger) ---"); // Используем Console для отладки

            try
            {
                var superAdminPasswordHash = HashPassword("superpassword");
                // Console.WriteLine("--- Hashed superadmin password successfully: {Hash}", superAdminPasswordHash);

                var superAdmin = new User
                {
                    Id = _nextId++,
                    Username = "superadmin",
                    Role = Roles.SuperAdmin,
                    PasswordHash = superAdminPasswordHash,
                    Groups = new List<string> { "System" }
                };
                Console.WriteLine("--- Adding superadmin (ID: {0}, User: {1}) to list...", superAdmin.Id, superAdmin.Username);
                _users.Add(superAdmin);
                Console.WriteLine("--- Superadmin added. Current count: {0}", _users.Count);

                var admin1PasswordHash = HashPassword("adminpass");
                // Console.WriteLine("--- Hashed admin1 password successfully: {Hash}", admin1PasswordHash);
                var admin1 = new User
                {
                    Id = _nextId++,
                    Username = "admin1",
                    Role = Roles.Admin,
                    PasswordHash = admin1PasswordHash,
                    Groups = new List<string> { "GroupA" },
                    CreatedByAdminId = 1 // Исправлено на 1, т.к. superAdmin.Id недоступен до добавления
                };
                Console.WriteLine("--- Adding admin1 (ID: {0}, User: {1}) to list...", admin1.Id, admin1.Username);
                _users.Add(admin1);
                Console.WriteLine("--- Admin1 added. Current count: {0}", _users.Count);


                var user1PasswordHash = HashPassword("userpass");
                // Console.WriteLine("--- Hashed user1 password successfully: {Hash}", user1PasswordHash);
                var user1 = new User
                {
                    Id = _nextId++,
                    Username = "user1",
                    Role = Roles.User,
                    PasswordHash = user1PasswordHash,
                    Groups = new List<string> { "GroupA" },
                    CreatedByAdminId = 2 // Исправлено на 2, т.к. admin1.Id недоступен до добавления
                };
                Console.WriteLine("--- Adding user1 (ID: {0}, User: {1}) to list...", user1.Id, user1.Username);
                _users.Add(user1);
                Console.WriteLine("--- User1 added. Current count: {0}", _users.Count);

                // Добавляем группы
                _groups.Add("System");
                _groups.Add("GroupA");
                _groups.Add("GroupB");
                Console.WriteLine("--- Groups added. Groups count: {0}", _groups.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"!!! EXCEPTION during UserService initialization: {ex.Message} !!!");
                throw;
            }

            Console.WriteLine("UserService initialized successfully (No Logger). Final users count: {0}", _users.Count);
        }
        public User Authenticate(string username, string password)
        {
            Console.WriteLine($"--- UserService Authenticate (No Logger): Attempting login for username='{username}'");

            if (_users != null) { Console.WriteLine($"--- Current users in list ({_users.Count}): {string.Join(", ", _users.Select(u => u.Username))}"); }
            else { Console.WriteLine("--- _users list is NULL!"); return null; }

            var user = _users.SingleOrDefault(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

            if (user == null) { Console.WriteLine($"Authentication failed (No Logger): User '{username}' not found in the list."); return null; }

            Console.WriteLine($"--- User '{username}' FOUND (No Logger). Verifying password...");
            bool passwordValid = VerifyPasswordHash(password, user.PasswordHash);

            if (!passwordValid) { Console.WriteLine($"Authentication failed (No Logger): Invalid password for user '{username}'."); return null; }

            Console.WriteLine($"Authentication successful (No Logger) for user '{username}'.");
            return user;
        }

        public User CreateAdmin(User user, string password, List<string> initialGroups, int creatorId)
        {
            ValidateUserCreation(user, password);

            if (initialGroups == null || !initialGroups.Any())
                throw new ArgumentException("Admin must be assigned to at least one group upon creation.", nameof(initialGroups));

            if (!initialGroups.All(g => _groups.Contains(g)))
                throw new ArgumentException("One or more specified groups do not exist.", nameof(initialGroups));

            user.Id = _nextId++;
            user.Role = Roles.Admin;
            user.PasswordHash = HashPassword(password);
            user.Groups = initialGroups.Distinct().ToList();
            user.CreatedByAdminId = creatorId;

            _users.Add(user);
            return user;
        }

        public User CreateUserByAdmin(User user, string password, int adminCreatorId, string targetGroup) // Принимаем targetGroup
        {
            ValidateUserCreation(user, password);

            var admin = GetById(adminCreatorId);
            if (admin == null || admin.Role != Roles.Admin)
                throw new InvalidOperationException("Invalid admin creator specified.");
            if (admin.Groups == null || !admin.Groups.Contains(targetGroup))
            {
                _logger?.LogWarning("Admin ID {AdminId} attempted to assign user to group '{TargetGroup}' which they are not a member of.", adminCreatorId, targetGroup);
                throw new Exception($"Admin '{admin.Username}' has no groups assigned and cannot create users.");
            }
            user.Id = _nextId++;
            user.Role = Roles.User;
            user.PasswordHash = HashPassword(password);
            user.Groups = new List<string> { targetGroup };
            user.CreatedByAdminId = adminCreatorId;

            _users.Add(user);
            _logger?.LogInformation("User '{Username}' (ID: {UserId}) created by Admin ID {AdminId} and assigned to group '{TargetGroup}'.", user.Username, user.Id, adminCreatorId, targetGroup);
            return user;
        }

        public User CreateUserBySuperAdmin(User user, string password, List<string> initialGroups)
        {
            ValidateUserCreation(user, password);

            if (initialGroups != null && !initialGroups.All(g => _groups.Contains(g)))
                throw new ArgumentException("One or more specified groups do not exist.", nameof(initialGroups));

            user.Id = _nextId++;
            user.Role = Roles.User;
            user.PasswordHash = HashPassword(password);
            user.Groups = initialGroups?.Distinct().ToList() ?? new List<string>();
            _users.Add(user);
            return user;
        }

        public User GetById(int id) => _users.FirstOrDefault(x => x.Id == id);
        public User GetByUsername(string username) => _users.FirstOrDefault(x => x.Username == username);
        public IEnumerable<User> GetAll() => _users.ToList();
        public IEnumerable<string> GetAllGroupNames() => _groups.ToList();
        public IEnumerable<string> GetGroupsForAdmin(int adminId)
        {
            var admin = GetById(adminId);
            if (admin?.Role == Roles.Admin)
            {
                return admin.Groups?.ToList() ?? Enumerable.Empty<string>();
            }
            return Enumerable.Empty<string>();
        }

        public bool CreateGroup(string groupName, int creatorId, string creatorRole)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name cannot be empty.", nameof(groupName));
            if (_groups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                return false;
            _groups.Add(groupName);
            if (creatorRole == Roles.Admin)
            {
                var adminUser = GetById(creatorId);
                if (adminUser != null)
                {
                    if (adminUser.Groups == null) adminUser.Groups = new List<string>();
                    if (!adminUser.Groups.Contains(groupName))
                    {
                        adminUser.Groups.Add(groupName);
                        //_logger.LogInformation("Admin User ID {AdminId} automatically added to newly created group '{GroupName}'.", creatorId, groupName);
                    }
                }
                else
                {
                    //_logger.LogWarning("Could not find Admin User ID {AdminId} to add to new group '{GroupName}'.", creatorId, groupName);
                }
            }
            return true;
        }
        public IEnumerable<User> GetUsersInGroups(List<string> groupNames)
        {
            if (groupNames == null || !groupNames.Any())
                return Enumerable.Empty<User>();

            return _users
                .Where(u => u.Groups != null && u.Groups.Any(ug => groupNames.Contains(ug)))
                .ToList();
        }

        public void UpdateUserGroups(int userId, List<string> groupNames)
        {
            var user = GetById(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found.");

            if (groupNames != null && !groupNames.All(g => _groups.Contains(g)))
                throw new ArgumentException("One or more specified groups do not exist.");

            if (user.Role == Roles.Admin && (groupNames == null || !groupNames.Any()))
            {
                throw new InvalidOperationException("Cannot remove the last group from an Admin user.");
            }

            user.Groups = groupNames?.Distinct().ToList() ?? new List<string>();
        }

        public void DeleteUser(int userId, int deletedById, string deletedByRole)
        {
            var userToDelete = GetById(userId);
            if (userToDelete == null) return;

            if (userToDelete.Role == Roles.SuperAdmin)
                throw new InvalidOperationException("Cannot delete the SuperAdmin account.");

            if (deletedByRole == Roles.SuperAdmin)
            {
                _users.Remove(userToDelete);
            }
            else if (deletedByRole == Roles.Admin)
            {
                var admin = GetById(deletedById);
                if (admin == null) throw new UnauthorizedAccessException("Admin performing deletion not found.");

                if (userToDelete.Role != Roles.User)
                    throw new UnauthorizedAccessException("Admins can only delete Users.");

                bool isInAdminGroup = userToDelete.Groups.Any(ug => admin.Groups.Contains(ug));
                if (!isInAdminGroup)
                    throw new UnauthorizedAccessException("Admin cannot delete users outside of their groups.");

                _users.Remove(userToDelete);
            }
            else
            {
                throw new UnauthorizedAccessException("Insufficient permissions to delete user.");
            }
        }

        public bool CreateGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name cannot be empty.", nameof(groupName));
            if (_groups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                return false;

            _groups.Add(groupName);
            return true;
        }

        public void DeleteGroup(string groupName)
        {
            if (!_groups.Contains(groupName)) return;

            _groups.Remove(groupName);

            foreach (var user in _users)
            {
                if (user.Groups.Contains(groupName))
                {
                    user.Groups.Remove(groupName);
                    if (user.Role == Roles.Admin && !user.Groups.Any())
                    {
                        Console.WriteLine($"Warning: Admin '{user.Username}' (ID: {user.Id}) was left without groups after deleting group '{groupName}'. This should be handled!");
                    }
                }
            }
        }


        private void ValidateUserCreation(User user, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password is required.", nameof(password));
            if (string.IsNullOrWhiteSpace(user.Username))
                throw new ArgumentException("Username is required.", nameof(user.Username));
            if (_users.Any(x => x.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)))
                throw new Exception($"Username \"{user.Username}\" is already taken.");
        }

        private static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return null;
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return BitConverter.ToString(hashedBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static bool VerifyPasswordHash(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;
            return HashPassword(password) == storedHash;
        }

        public void UpdateUsername(int userIdToUpdate, string newUsername, int currentUserId, string currentUserRole)
        {
            if (string.IsNullOrWhiteSpace(newUsername))
                throw new ArgumentException("New username cannot be empty.", nameof(newUsername));

            var userToUpdate = GetById(userIdToUpdate);
            if (userToUpdate == null)
                throw new KeyNotFoundException($"User with ID {userIdToUpdate} not found.");

            if (_users.Any(u => u.Id != userIdToUpdate && u.Username.Equals(newUsername, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Username '{newUsername}' is already taken.");

            CheckUpdatePermissions(userToUpdate, currentUserId, currentUserRole, "update username");

            userToUpdate.Username = newUsername;
            //Console.WriteLine($"--- UserService: Updated username for User ID {userIdToUpdate} to '{newUsername}' by User ID {currentUserId}");
        }

        public void UpdatePassword(int userIdToUpdate, string newPassword, int currentUserId, string currentUserRole)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("New password cannot be empty.", nameof(newPassword));

            var userToUpdate = GetById(userIdToUpdate);
            if (userToUpdate == null)
                throw new KeyNotFoundException($"User with ID {userIdToUpdate} not found.");

            CheckUpdatePermissions(userToUpdate, currentUserId, currentUserRole, "update password");

            userToUpdate.PasswordHash = HashPassword(newPassword);
            //Console.WriteLine($"--- UserService: Updated password for User ID {userIdToUpdate} by User ID {currentUserId}");
        }
        private readonly ILogger<UserService> _logger;

        private void CheckUpdatePermissions(User userToModify, int currentUserId, string currentUserRole, string action)
        {
            if (userToModify.Id == currentUserId)
                throw new InvalidOperationException($"Users cannot {action} for themselves through this method.");

            if (userToModify.Role == Roles.SuperAdmin)
                throw new UnauthorizedAccessException($"Cannot {action} for a SuperAdmin.");

            if (currentUserRole == Roles.SuperAdmin)
            {
                if (userToModify.Role == Roles.Admin || userToModify.Role == Roles.User)
                    return;
            }
            else if (currentUserRole == Roles.Admin)
            {
                if (userToModify.Role == Roles.User)
                {
                    var admin = GetById(currentUserId);
                    bool isInAdminGroup = userToModify.Groups.Any(ug => admin?.Groups?.Contains(ug) ?? false);
                    if (isInAdminGroup)
                        return;
                    else
                        throw new UnauthorizedAccessException($"Admin can only {action} for users within their groups.");
                }
            }

            throw new UnauthorizedAccessException($"Insufficient permissions to {action} for user ID {userToModify.Id}.");
        }
    }
}
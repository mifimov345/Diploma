using AuthService.Models;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System;

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
        User CreateUserByAdmin(User user, string password, int adminCreatorId);
        User CreateUserBySuperAdmin(User user, string password, List<string> initialGroups);
        User GetByUsername(string username);
        void UpdateUserGroups(int userId, List<string> groupNames);
        void DeleteUser(int userId, int deletedById, string deletedByRole);

        IEnumerable<string> GetAllGroupNames();
        bool CreateGroup(string groupName);
        void DeleteGroup(string groupName);
    }

    public class UserService : IUserService
    {
        private readonly List<User> _users = new List<User>();
        private readonly List<string> _groups = new List<string>();
        private int _nextId = 1;

        public UserService()
        {
            var superAdmin = new User
            {
                Id = _nextId++,
                Username = "superadmin",
                Role = Roles.SuperAdmin,
                PasswordHash = HashPassword("superpassword"),
                Groups = new List<string> { "System" }
            };
            _users.Add(superAdmin);
            _groups.Add("System");

            var admin1 = new User
            {
                Id = _nextId++,
                Username = "admin1",
                Role = Roles.Admin,
                PasswordHash = HashPassword("adminpass"),
                Groups = new List<string> { "GroupA" },
                CreatedByAdminId = superAdmin.Id
            };
            _users.Add(admin1);
            _groups.Add("GroupA");

            var user1 = new User
            {
                Id = _nextId++,
                Username = "user1",
                Role = Roles.User,
                PasswordHash = HashPassword("userpass"),
                Groups = new List<string> { "GroupA" },
                CreatedByAdminId = admin1.Id
            };
            _users.Add(user1);

            _groups.Add("GroupB");
        }
        public User Authenticate(string username, string password)
        {
            Console.WriteLine($"--- UserService Authenticate: Attempting login for username='{username}'");
            var user = _users.SingleOrDefault(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

            if (user == null)
            {
                Console.WriteLine($"--- UserService Authenticate: User '{username}' not found.");
                return null;
            }

            Console.WriteLine($"--- UserService Authenticate: User '{username}' found. ID={user.Id}, Role={user.Role}. Verifying password...");

            bool passwordValid = VerifyPasswordHash(password, user.PasswordHash);

            if (!passwordValid)
            {
                Console.WriteLine($"--- UserService Authenticate: Password verification FAILED for user '{username}'.");
                return null;
            }

            Console.WriteLine($"--- UserService Authenticate: Password verification SUCCESSFUL for user '{username}'.");
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

        public User CreateUserByAdmin(User user, string password, int adminCreatorId)
        {
            ValidateUserCreation(user, password);

            var admin = GetById(adminCreatorId);
            if (admin == null || admin.Role != Roles.Admin)
                throw new Exception("Invalid admin creator specified.");

            if (admin.Groups == null || !admin.Groups.Any())
                throw new Exception($"Admin '{admin.Username}' has no groups assigned and cannot create users.");

            user.Id = _nextId++;
            user.Role = Roles.User;
            user.PasswordHash = HashPassword(password);
            user.Groups = new List<string>(admin.Groups);
            user.CreatedByAdminId = adminCreatorId;

            _users.Add(user);
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


        public IEnumerable<string> GetAllGroupNames() => _groups.ToList();

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
    }
}
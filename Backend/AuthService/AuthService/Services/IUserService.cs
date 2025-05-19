using AuthService.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AuthService.Services
{
    public interface IUserService
    {
        Task<User> AuthenticateAsync(string username, string password);
        Task<User> GetByIdAsync(int id);
        Task<IEnumerable<User>> GetAllAsync();
        Task<User> CreateUserByAdminAsync(User user, string password, int adminCreatorId, string targetGroup);
        Task<User> CreateAdminAsync(User user, string password, List<string> initialGroups, int creatorId);
        Task<User> CreateUserBySuperAdminAsync(User user, string password, List<string> initialGroups);
        Task<User> GetByUsernameAsync(string username);
        Task UpdateUserGroupsAsync(int userId, List<string> groupNames);
        Task DeleteUserAsync(int userId, int deletedById, string deletedByRole);
        Task UpdateUsernameAsync(int userIdToUpdate, string newUsername, int currentUserId, string currentUserRole);
        Task UpdatePasswordAsync(int userIdToUpdate, string newPassword, int currentUserId, string currentUserRole);

        Task<IEnumerable<string>> GetAllGroupNamesAsync();
        Task<IEnumerable<string>> GetGroupsForAdminAsync(int adminId);
        Task<bool> CreateGroupAsync(string groupName, int creatorId, string creatorRole);
        Task DeleteGroupAsync(string groupName);

        Task<IEnumerable<User>> GetUsersInGroupsAsync(List<string> groupNames);
        Task<List<Group>> GetUserGroupsAsync(int userId);
    }
}

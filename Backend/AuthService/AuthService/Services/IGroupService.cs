using AuthService.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AuthService.Services
{
    public interface IGroupService
    {
        Task<IEnumerable<Group>> GetAllGroupsAsync();
        Task<Group> GetGroupByIdAsync(int id);
        Task<Group> CreateGroupAsync(Group group, int creatorId);
        Task<bool> DeleteGroupAsync(int id);
        Task<bool> AddUserToGroupAsync(int userId, int groupId);
        Task<bool> RemoveUserFromGroupAsync(int userId, int groupId);
    }
}

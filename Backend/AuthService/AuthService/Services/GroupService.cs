using AuthService.Models;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AuthService.Services
{
    public class GroupService : IGroupService
    {
        private readonly AuthDbContext _context;

        public GroupService(AuthDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Group>> GetAllGroupsAsync()
        {
            return await _context.Groups
                .Include(g => g.UserGroups)
                .ThenInclude(ug => ug.User)
                .ToListAsync();
        }

        public async Task<Group> GetGroupByIdAsync(int id)
        {
            return await _context.Groups
                .Include(g => g.UserGroups)
                .ThenInclude(ug => ug.User)
                .FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task<Group> CreateGroupAsync(Group group, int creatorId)
        {
            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            var creator = await _context.Users.FindAsync(creatorId);
            if (creator != null)
            {
                _context.UserGroups.Add(new UserGroup { UserId = creatorId, GroupId = group.Id });

                if (creator.Role == "Admin")
                {
                    var superAdmins = _context.Users.Where(u => u.Role == "SuperAdmin");
                    foreach (var sa in superAdmins)
                        _context.UserGroups.Add(new UserGroup { UserId = sa.Id, GroupId = group.Id });
                }

                if (creator.Role == "SuperAdmin")
                {
                }
            }

            await _context.SaveChangesAsync();
            return group;
        }


        public async Task<bool> DeleteGroupAsync(int id)
        {
            var group = await _context.Groups.FindAsync(id);
            if (group == null) return false;
            if (group.Name == "System") return false;

            _context.Groups.Remove(group);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> AddUserToGroupAsync(int userId, int groupId)
        {
            var exists = await _context.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId);
            if (exists) return false;
            _context.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RemoveUserFromGroupAsync(int userId, int groupId)
        {
            var link = await _context.UserGroups.FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GroupId == groupId);
            if (link == null) return false;
            _context.UserGroups.Remove(link);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}

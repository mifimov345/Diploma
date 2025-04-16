using System.Collections.Generic;

namespace AuthService.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }
        public List<string> Groups { get; set; } = new List<string>();
        public int? CreatedByAdminId { get; set; }
    }

    public class Group
    {
        public string Name { get; set; }
    }
}

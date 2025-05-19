using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AuthService.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string UserName { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        [Required]
        public string Role { get; set; } // SuperAdmin | Admin | User
        [Required]
        public int? CreatedByAdminId { get; set; }
        public ICollection<UserGroup> UserGroups { get; set; }
    }
}

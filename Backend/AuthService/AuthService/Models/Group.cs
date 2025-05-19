using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AuthService.Models
{
    public class Group
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        public string Description { get; set; }

        public ICollection<UserGroup> UserGroups { get; set; }
    }
}

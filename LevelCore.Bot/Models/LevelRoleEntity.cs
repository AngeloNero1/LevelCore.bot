using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LevelCore.Bot.Models
{
    public class LevelRoleEntity
    {
        public int Id { get; set; }

        public ulong GuildId { get; set; }
        public int Level { get; set; }
        public ulong RoleId { get; set; }
    }
}

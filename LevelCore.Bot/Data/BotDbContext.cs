using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LevelCore.Bot.Models;
using Microsoft.EntityFrameworkCore;

namespace LevelCore.Bot.Data
{
    public class BotDbContext : DbContext
    {
        public DbSet<UserStatsEntity> UserStats { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=levelcore.db");
        }
        public DbSet<GuildSettingsEntity> GuildSettings { get; set; }

        public DbSet<LevelRoleEntity> LevelRoles { get; set; }
    }
}
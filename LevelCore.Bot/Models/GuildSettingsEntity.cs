using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LevelCore.Bot.Models
{
    public class GuildSettingsEntity
    {
        public int Id { get; set; }

        public ulong GuildId { get; set; }

        public ulong? LevelUpChannelId { get; set; }

        public int VoiceXpPerMinute { get; set; } = 10;

        public int MessageXpMin { get; set; } = 5;
        public int MessageXpMax { get; set; } = 15;
        public int MessageCooldownSeconds { get; set; } = 60;

        public int DailyBonusMin { get; set; } = 100;
        public int DailyBonusMax { get; set; } = 500;
    }
}

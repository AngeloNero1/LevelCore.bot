namespace LevelCore.Bot.Models
{
    public class UserStatsEntity
    {
        public int Id { get; set; }

        public ulong UserId { get; set; }
        public ulong GuildId { get; set; }

        public int BonusXp { get; set; }
        public int TotalXp { get; set; }
        public int MessageXp { get; set; }
        public int VoiceXp { get; set; }
        public int VoiceMinutes { get; set; }

        public DateTime LastMessageXp { get; set; }
        public DateTime? LastDailyVoiceBonus { get; set; }
    }
}
using Discord;
using Discord.WebSocket;
using LevelCore.Bot;
using LevelCore.Bot.Data;
using LevelCore.Bot.Models;
using System.Linq;
class Program
{
    private Dictionary<ulong, UserStats> _userStats = new();
    private Dictionary<ulong, DateTime> _messageCooldowns = new();
    private Random _random = new();
    private Dictionary<ulong, DateTime> _activeVoiceSessions = new();
    private BotDbContext _db = new();

    private int CalculateLevel(int totalXp)
    {
        int level = 1;

        while (totalXp >= GetRequiredXpForLevel(level))
        {
            totalXp -= GetRequiredXpForLevel(level);
            level++;
        }

        return level;
    }
    private async Task SendLevelUpMessage(SocketGuild guild, SocketUser user, int oldLevel, int newLevel)
    {
        var settings = _db.GuildSettings
            .FirstOrDefault(x => x.GuildId == guild.Id);

        if (settings?.LevelUpChannelId == null)
            return;

        var channel = guild.GetTextChannel(settings.LevelUpChannelId.Value);

        if (channel == null)
            return;

        await channel.SendMessageAsync(
            $"🎉 {user.Mention} level atladı! **Level {oldLevel} → {newLevel}**");
    }
    private async Task GiveLevelRoles(SocketGuild guild, SocketUser user, int newLevel)
    {
        if (user is not SocketGuildUser guildUser)
            return;

        var rolesToGive = _db.LevelRoles
            .Where(x => x.GuildId == guild.Id && x.Level <= newLevel)
            .ToList();

        foreach (var levelRole in rolesToGive)
        {
            var role = guild.GetRole(levelRole.RoleId);

            if (role != null && !guildUser.Roles.Any(x => x.Id == role.Id))
            {
                await guildUser.AddRoleAsync(role);
            }
        }
    }

    private int GetTotalXpForLevel(int targetLevel)
    {
        int totalXp = 0;

        for (int level = 1; level < targetLevel; level++)
        {
            totalXp += GetRequiredXpForLevel(level);
        }

        return totalXp;
    }

    private int GetRequiredXpForLevel(int level)
    {
        return level * level * 100;
    }

    private UserStatsEntity GetOrCreateUserStats(ulong userId, ulong guildId)
    {
        var stats = _db.UserStats
            .FirstOrDefault(x => x.UserId == userId && x.GuildId == guildId);

        if (stats == null)
        {
            stats = new UserStatsEntity
            {
                UserId = userId,
                GuildId = guildId,
                TotalXp = 0,
                MessageXp = 0,
                VoiceXp = 0,
                VoiceMinutes = 0,
                LastMessageXp = DateTime.MinValue
            };

            _db.UserStats.Add(stats);
            _db.SaveChanges();
        }

        return stats;
    }

    private (UserStatsEntity Stats, bool LeveledUp, int OldLevel, int NewLevel)
    AddXp(ulong userId, ulong guildId, int amount, bool isMessage)
    {
        var stats = GetOrCreateUserStats(userId, guildId);

        int oldLevel = CalculateLevel(stats.TotalXp);

        stats.TotalXp += amount;

        if (isMessage)
            stats.MessageXp += amount;
        else
            stats.VoiceXp += amount;

        int newLevel = CalculateLevel(stats.TotalXp);

        _db.SaveChanges();

        bool leveledUp = newLevel > oldLevel;

        return (stats, leveledUp, oldLevel, newLevel);
    }

    private async Task MessageReceived(SocketMessage message)
    {
        if (message.Author.IsBot)
            return;

        if (message.Content == "!help")
        {
            await message.Channel.SendMessageAsync(
                "📖 **LevelCore Komutları**\n\n" +

                "👤 **Kullanıcı Komutları**\n" +
                "`!stats` → Kendi istatistiklerini gösterir.\n" +
                "`!stats @user` → Etiketlenen kullanıcının istatistiklerini gösterir.\n" +
                "`!leaderboard` → Toplam XP sıralamasını gösterir.\n" +
                "`!leaderboard chat` → Chat XP sıralamasını gösterir.\n" +
                "`!leaderboard voice` → Voice XP sıralamasını gösterir.\n\n" +

                "👑 **Yönetici Komutları**\n" +
                "`!setlevelchannel` → Komutun yazıldığı kanalı level-up kanalı yapar.\n" +
                "`!addxp @user miktar` → Kullanıcıya bonus XP ekler.\n" +
                "`!setlevel @user level` → Kullanıcının levelini ayarlar.\n" +
                "`!resetlevel @user` → Kullanıcının XP/level bilgisini sıfırlar.\n" +
                "`!setlevelrole level @rol` → Belirli levele rol bağlar."
            );

            return;
        }

        if (message.Content.StartsWith("!setlevelrole"))
        {
            if (message.Author is not SocketGuildUser guildUser ||
                !guildUser.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("❌ Bu komutu kullanmak için yönetici olmalısın.");
                return;
            }

            string[] parts = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3 || !int.TryParse(parts[1], out int rolelevel))
            {
                await message.Channel.SendMessageAsync("❌ Kullanım: `!setlevelrole 5 @Rol`");
                return;
            }

            if (message.MentionedRoles.Count == 0)
            {
                await message.Channel.SendMessageAsync("❌ Bir rol etiketlemelisin.");
                return;
            }

            var guildChannel = (SocketGuildChannel)message.Channel;
            ulong roleGuildId = guildChannel.Guild.Id;
            var role = message.MentionedRoles.First();

            var levelRole = _db.LevelRoles
                .FirstOrDefault(x => x.GuildId == roleGuildId && x.Level == rolelevel);

            if (levelRole == null)
            {
                levelRole = new LevelRoleEntity
                {
                    GuildId = roleGuildId,
                    Level = rolelevel,
                    RoleId = role.Id
                };

                _db.LevelRoles.Add(levelRole);
            }
            else
            {
                levelRole.RoleId = role.Id;
            }

            _db.SaveChanges();

            await message.Channel.SendMessageAsync(
                $"✅ Level **{rolelevel}** için rol ayarlandı: {role.Mention}");

            return;
        }

        if (message.Content == "!setlevelchannel")
        {
            if (message.Author is not SocketGuildUser guildUser ||
                !guildUser.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync(
                    "❌ Bu komutu kullanmak için yönetici olmalısın.");

                return;
            }

            

            var guildChannel = (SocketGuildChannel)message.Channel;

            var settings = _db.GuildSettings
                .FirstOrDefault(x => x.GuildId == guildChannel.Guild.Id);

            if (settings == null)
            {
                settings = new GuildSettingsEntity
                {
                    GuildId = guildChannel.Guild.Id
                };

                _db.GuildSettings.Add(settings);
            }

            settings.LevelUpChannelId = guildChannel.Id;

            _db.SaveChanges();

            await message.Channel.SendMessageAsync(
                "✅ Bu kanal artık level-up kanalı olarak ayarlandı.");

            return;
        }

        if (message.Content.StartsWith("!addxp") ||
    message.Content.StartsWith("!setlevel") ||
    message.Content.StartsWith("!resetlevel"))
        {
            if (message.Author is not SocketGuildUser guildUser ||
                !guildUser.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("❌ Bu komutu kullanmak için yönetici olmalısın.");
                return;
            }

            var guildChannel = (SocketGuildChannel)message.Channel;
            ulong adminguildId = guildChannel.Guild.Id;

            string[] parts = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync("❌ Bir kullanıcı etiketlemelisin.");
                return;
            }

            var targetUser = message.MentionedUsers.First();
            var AdminTargetstats = GetOrCreateUserStats(targetUser.Id, adminguildId);

            if (parts[0] == "!addxp")
            {
                if (parts.Length < 3 || !int.TryParse(parts[2], out int amount))
                {
                    await message.Channel.SendMessageAsync("❌ Kullanım: `!addxp @user 500`");
                    return;
                }

                AdminTargetstats.TotalXp += amount;
                AdminTargetstats.BonusXp += amount;
                _db.SaveChanges();

                await message.Channel.SendMessageAsync(
                    $"✅ {targetUser.Mention} kullanıcısına **{amount} XP** eklendi.");
                return;
            }

            if (parts[0] == "!setlevel")
            {
                
                if (parts.Length < 3 || !int.TryParse(parts[2], out int targetLevel) || targetLevel < 1)
                {
                    await message.Channel.SendMessageAsync("❌ Kullanım: `!setlevel @user 5`");
                    return;
                }


                int oldLevel = CalculateLevel(AdminTargetstats.TotalXp);

                int newTotalXp = GetTotalXpForLevel(targetLevel);

                int difference = newTotalXp - AdminTargetstats.TotalXp;

                AdminTargetstats.TotalXp = newTotalXp;
                AdminTargetstats.BonusXp = Math.Max(0, AdminTargetstats.BonusXp + difference);

                _db.SaveChanges();

                await GiveLevelRoles(
                    guildChannel.Guild,
                    targetUser,
                    targetLevel);
                await SendLevelUpMessage(
                    guildChannel.Guild,
                    targetUser,
                    oldLevel,
                    targetLevel);
                await message.Channel.SendMessageAsync(
                    $"✅ {targetUser.Mention} kullanıcısı **Level {targetLevel}** yapıldı.");
                return;
            }

            if (parts[0] == "!resetlevel")
            {
                AdminTargetstats.BonusXp = 0;
                AdminTargetstats.TotalXp = 0;
                AdminTargetstats.MessageXp = 0;
                AdminTargetstats.VoiceXp = 0;
                AdminTargetstats.VoiceMinutes = 0;
                AdminTargetstats.LastDailyVoiceBonus = null;

                _db.SaveChanges();

                await message.Channel.SendMessageAsync(
                    $"✅ {targetUser.Mention} kullanıcısının level/XP bilgileri sıfırlandı.");
                return;
            }
        }

        if (message.Content.StartsWith("!stats"))
        {
            ulong statsGuildId = ((SocketGuildChannel)message.Channel).Guild.Id;

            SocketUser targetUser;

            if (message.MentionedUsers.Any())
                targetUser = message.MentionedUsers.First();
            else
                targetUser = message.Author;

            var userStats = GetOrCreateUserStats(targetUser.Id, statsGuildId);

            int userLevel = CalculateLevel(userStats.TotalXp);

            string voiceTime = $"{userStats.VoiceMinutes / 60} saat {userStats.VoiceMinutes % 60} dakika";

            await message.Channel.SendMessageAsync(
                $"📊 {targetUser.Mention} İstatistikleri\n\n" +
                $"⭐ Level: **{userLevel}**\n" +
                $"✨ Toplam XP: **{userStats.TotalXp}**\n\n" +
                $"💬 Mesaj XP: **{userStats.MessageXp}**\n" +
                $"🎤 Ses XP: **{userStats.VoiceXp}**\n" +
                $"🎁 Bonus XP: **{userStats.BonusXp}**\n" +
                $"🕒 Ses Süresi: **{voiceTime}**"
            );

            return;
        }
        if (message.Content.StartsWith("!leaderboard"))
        {
            ulong leaderboardGuildId = ((SocketGuildChannel)message.Channel).Guild.Id;

            string[] parts = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string type = parts.Length > 1 ? parts[1].ToLower() : "total";

            IQueryable<UserStatsEntity> query = _db.UserStats
                .Where(x => x.GuildId == leaderboardGuildId);

            string title;
            Func<UserStatsEntity, int> scoreSelector;

            if (type == "chat" || type == "text")
            {
                title = "💬 **Chat XP Liderlik Tablosu**";
                scoreSelector = x => x.MessageXp;
            }
            else if (type == "voice" || type == "ses")
            {
                title = "🎤 **Voice XP Liderlik Tablosu**";
                scoreSelector = x => x.VoiceXp;
            }
            else
            {
                title = "🏆 **Toplam XP Liderlik Tablosu**";
                scoreSelector = x => x.TotalXp;
            }

            var leaderboard = query
                .ToList()
                .OrderByDescending(scoreSelector)
                .Take(10)
                .ToList();

            if (leaderboard.Count == 0)
            {
                await message.Channel.SendMessageAsync("📭 Henüz leaderboard verisi yok.");
                return;
            }

            string leaderboardText = $"{title}\n\n";

            for (int i = 0; i < leaderboard.Count; i++)
            {
                var userStats = leaderboard[i];

                var user = await _client.GetUserAsync(userStats.UserId);

                string medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    2 => "🥉",
                    _ => $"{i + 1}."
                };

                int score = scoreSelector(userStats);

                leaderboardText +=
                    $"{medal} {(user != null ? user.Mention : $"<@{userStats.UserId}>")} — **{score} XP**\n";
            }

            await message.Channel.SendMessageAsync(leaderboardText);

            return;
        }

        ulong userId = message.Author.Id;


        if (_messageCooldowns.ContainsKey(userId))
        {
            TimeSpan diff = DateTime.Now - _messageCooldowns[userId];

            if (diff.TotalSeconds < 60)
            {
                Console.WriteLine($"{message.Author.Username} cooldown'da.");
                return;
            }
        }
        

        int earnedXp = _random.Next(5, 16);

        _messageCooldowns[userId] = DateTime.Now;

        ulong guildId = ((SocketGuildChannel)message.Channel).Guild.Id;
        var result = AddXp(userId, guildId, earnedXp, true);
        var stats = result.Stats;

        if (result.LeveledUp)
        {
            var guild = ((SocketGuildChannel)message.Channel).Guild;

            await SendLevelUpMessage(
                guild,
                message.Author,
                result.OldLevel,
                result.NewLevel);

            await GiveLevelRoles(
                guild,
                message.Author,
                result.NewLevel);
        }

        int level = stats.TotalXp / 1000 + 1;

        Console.WriteLine(
            $"[{message.Author.Username}] {message.Content} | " +
            $"+{earnedXp} XP | " +
            $"Toplam XP: {stats.TotalXp} | " +
            $"Level: {level}");

        return;
    }

    private async Task UserVoiceStateUpdated(SocketUser user,
    SocketVoiceState before,
    SocketVoiceState after)
    {
        if (user.IsBot)
            return;

        ulong userId = user.Id;

        // Sese giriş
        if (before.VoiceChannel == null && after.VoiceChannel != null)
        {
            _activeVoiceSessions[userId] = DateTime.Now;

            Console.WriteLine($"{user.Username} {after.VoiceChannel.Name} kanalına katıldı.");
        }

        // Sesten çıkış
        else if (before.VoiceChannel != null && after.VoiceChannel == null)
        {
            if (_activeVoiceSessions.ContainsKey(userId))
            {
                DateTime joinTime = _activeVoiceSessions[userId];

                int minutes =
                    (int)(DateTime.Now - joinTime).TotalMinutes;

                int earnedXp = minutes * 10;

                ulong guildId = before.VoiceChannel.Guild.Id;
                

                var result = AddXp(userId, guildId, earnedXp, false);
                var stats = result.Stats;
                stats.VoiceMinutes += minutes;
                _db.SaveChanges();
                if (minutes >= 5)
                {
                    DateTime today = DateTime.Today;

                    bool alreadyGotBonus =
                        stats.LastDailyVoiceBonus.HasValue &&
                        stats.LastDailyVoiceBonus.Value.Date == today;

                    if (!alreadyGotBonus)
                    {
                        int bonusXp = _random.Next(100, 501);

                        stats.TotalXp += bonusXp;
                        stats.BonusXp += bonusXp;
                        stats.LastDailyVoiceBonus = DateTime.Now;

                        _db.SaveChanges();

                        Console.WriteLine(
                            $"{user.Username} günlük ses bonusu kazandı! +{bonusXp} XP");
                    }
                }

                if (result.LeveledUp)
                {
                    var guild = before.VoiceChannel.Guild;

                    await SendLevelUpMessage(
                        guild,
                        user,
                        result.OldLevel,
                        result.NewLevel);

                    await GiveLevelRoles(
                        guild,
                        user,
                        result.NewLevel);
                }
                 
                _activeVoiceSessions.Remove(userId);
            }
        }

        return;
    }

    private DiscordSocketClient _client;

    static async Task Main(string[] args)
    {
        await new Program().MainAsync();
    }

    public async Task MainAsync()
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents =
        GatewayIntents.Guilds |
        GatewayIntents.GuildMessages |
        GatewayIntents.MessageContent |
        GatewayIntents.GuildVoiceStates
        });

        _client.Log += Log;
        _client.Ready += Ready;
        _client.MessageReceived += MessageReceived;
        _client.UserVoiceStateUpdated += UserVoiceStateUpdated;

        string token = "Token_here";

        _db.Database.EnsureCreated();

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private Task Ready()
    {
        Console.WriteLine($"Bot hazır! {_client.CurrentUser.Username}");
        return Task.CompletedTask;
    }

    private Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
}
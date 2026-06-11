using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LevelCore.Bot
{
    public class UserStats
    {
        public int TotalXp { get; set; }
        public int MessageXp { get; set; }
        public int VoiceXp { get; set; }
        public int VoiceMinutes { get; set; }

        public int Level => TotalXp / 1000 + 1;
    }
}

using System;

namespace Stribog
{
    public class UserSetting
    {
        public long ChatId { get; set; }
        public string? City { get; set; }
        public bool DailyWeatherBroadcast { get; set; } = false;
        public string? BroadcastCity { get; set; }
        public string? BroadcastTime { get; set; }
        public string? TimeZoneId { get; set; }
        
        // НОВЕ ПОЛЕ: Зберігаємо час останньої розсилки
        public DateTime? LastBroadcastSentUtc { get; set; }
    }
}
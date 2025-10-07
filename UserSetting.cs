using System;

namespace Stribog
{
    public class UserSetting
    {
        public string City { get; set; }
        public TimeSpan NotificationTime { get; set; }
        public int UtcOffsetSeconds { get; set; }
        // *** НОВЕ ПОЛЕ: Зберігаємо дату останнього сповіщення ***
        public DateTime LastNotificationDate { get; set; } = DateTime.MinValue;
    }
}
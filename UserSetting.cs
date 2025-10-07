using System;

namespace Stribog
{
    public class UserSetting
    {
        public string City { get; set; }
        public TimeSpan NotificationTime { get; set; }
        // Нове поле для зберігання часового поясу (зміщення від UTC в секундах)
        public int UtcOffsetSeconds { get; set; }
    }
}
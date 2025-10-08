using System;

namespace Stribog
{
    /// <summary>
    /// Модель налаштувань користувача для розсилки прогнозу погоди.
    /// </summary>
    public class UserSetting
    {
        /// <summary>
        /// Місто користувача.
        /// </summary>
        public string? City { get; set; }

        /// <summary>
        /// Час доби, коли користувач хоче отримувати сповіщення.
        /// </summary>
        public TimeSpan NotificationTime { get; set; }

        /// <summary>
        /// Зсув від UTC у секундах (часовий пояс користувача).
        /// </summary>
        public int UtcOffsetSeconds { get; set; }

        /// <summary>
        /// Дата останнього відправленого сповіщення (у локальному часі користувача).
        /// </summary>
        public DateTime LastNotificationDate { get; set; } = DateTime.MinValue;
    }
}
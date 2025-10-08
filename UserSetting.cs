// PetProjects/UserSetting.cs
namespace Stribog;

public class UserSetting
{
    public long ChatId { get; set; }
    public string? City { get; set; }

    // Розсилки:
    public bool DailyWeatherBroadcast { get; set; } // чи увімкнена розсилка
    public string? BroadcastCity { get; set; }      // місто для розсилки
    public string? BroadcastTime { get; set; }      // час розсилки у форматі HH:mm (UTC/ локальний трактуємо як UTC або локальний залежно від сервера)

    // Нова опція: часовa зона користувача
    public string? TimeZoneId { get; set; }         // TZ міста (наприклад, "Europe/Kiev" або "Europe/Warsaw" залежно від платформи)
}
// PetProjects/UserSetting.cs
namespace Stribog;

public class UserSetting
{
    public long ChatId { get; set; }
    public string? City { get; set; }

    // Нові поля для збереження в БД
    public bool DailyWeatherBroadcast { get; set; } = false;
    public string? BroadcastCity { get; set; }
    public string? BroadcastTime { get; set; }
    public string? TimeZoneId { get; set; }
}
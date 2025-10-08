// PetProjects/UserSettingsService.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Stribog;

public class UserSettingsService
{
    private readonly string _filePath;

    public UserSettingsService()
    {
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
        _filePath = Path.Combine(dataDirectory, "usersettings.json");
    }

    public UserSetting GetUserSettings(long chatId)
    {
        if (!File.Exists(_filePath)) return new UserSetting { ChatId = chatId };
        var json = File.ReadAllText(_filePath);
        var settings = JsonConvert.DeserializeObject<List<UserSetting>>(json) ?? new List<UserSetting>();
        return settings.FirstOrDefault(s => s.ChatId == chatId) ?? new UserSetting { ChatId = chatId };
    }

    public void SaveUserSettings(UserSetting settingToSave)
    {
        var settings = new List<UserSetting>();
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            settings = JsonConvert.DeserializeObject<List<UserSetting>>(json) ?? new List<UserSetting>();
        }
        var existingSetting = settings.FirstOrDefault(s => s.ChatId == settingToSave.ChatId);
        if (existingSetting != null)
        {
            existingSetting.City = settingToSave.City;
        }
        else
        {
            settings.Add(settingToSave);
        }
        var newJson = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(_filePath, newJson);
    }
}
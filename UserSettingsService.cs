using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Stribog;

public class UserSettingsService
{
    private readonly string _filePath;
    private Dictionary<long, UserSetting> _userSettings;

    public UserSettingsService(string filePath)
    {
        _filePath = filePath;
        _userSettings = LoadUserSettingsFromFile();
    }

    private Dictionary<long, UserSetting> LoadUserSettingsFromFile()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<long, UserSetting>();
        }
        var json = File.ReadAllText(_filePath);
        return JsonConvert.DeserializeObject<Dictionary<long, UserSetting>>(json) ?? new Dictionary<long, UserSetting>();
    }

    public Task SetUserSettingAsync(long chatId, UserSetting setting)
    {
        _userSettings[chatId] = setting;
        return SaveUserSettingsToFileAsync();
    }

    public Task<UserSetting?> GetUserSettingsAsync(long chatId)
    {
        _userSettings.TryGetValue(chatId, out var setting);
        return Task.FromResult(setting);
    }
    
    // ДОДАНО: Відсутній метод для отримання ID всіх користувачів
    public Task<IEnumerable<long>> GetAllUserIdsAsync()
    {
        return Task.FromResult(_userSettings.Keys.AsEnumerable());
    }

    private Task SaveUserSettingsToFileAsync()
    {
        var json = JsonConvert.SerializeObject(_userSettings, Formatting.Indented);
        return File.WriteAllTextAsync(_filePath, json);
    }
}
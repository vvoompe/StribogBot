using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;

namespace Stribog
{
    public class UserSettingsService
    {
        private readonly string _filePath;
        private Dictionary<long, string> _userSettings;

        public UserSettingsService(string filePath)
        {
            _filePath = filePath;
            _userSettings = LoadSettings();
        }

        private Dictionary<long, string> LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<long, string>();
            }
            var json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<Dictionary<long, string>>(json) ?? new Dictionary<long, string>();
        }

        private async Task SaveSettingsAsync()
        {
            var json = JsonConvert.SerializeObject(_userSettings, Formatting.Indented);
            await File.WriteAllTextAsync(_filePath, json);
        }

        public async Task SetDefaultCityAsync(long chatId, string city)
        {
            _userSettings[chatId] = city;
            await SaveSettingsAsync();
        }

        public Task<string> GetDefaultCityAsync(long chatId)
        {
            _userSettings.TryGetValue(chatId, out var city);
            return Task.FromResult(city);
        }

        public async Task SendWeatherNotifications(ITelegramBotClient botClient, WeatherService weatherService)
        {
            System.Console.WriteLine("Розпочато надсилання щоденних оновлень...");
            foreach (var setting in _userSettings)
            {
                var weatherReport = await weatherService.GetCurrentWeatherAsync(setting.Value);
                await botClient.SendTextMessageAsync(setting.Key, weatherReport);
            }
            System.Console.WriteLine("Надсилання завершено.");
        }
    }
}
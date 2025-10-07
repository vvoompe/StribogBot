using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;

namespace Stribog
{
    public class UserSettingsService
    {
        private readonly string _filePath;
        private Dictionary<long, UserSetting> _userSettings;
        // Список користувачів, яким вже сьогодні надсилали сповіщення
        private List<long> _sentToday = new List<long>();

        public UserSettingsService(string filePath)
        {
            _filePath = filePath;
            _userSettings = LoadSettings();
        }

        private Dictionary<long, UserSetting> LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<long, UserSetting>();
            }
            var json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<Dictionary<long, UserSetting>>(json) ?? new Dictionary<long, UserSetting>();
        }

        private async Task SaveSettingsAsync()
        {
            var json = JsonConvert.SerializeObject(_userSettings, Formatting.Indented);
            await File.WriteAllTextAsync(_filePath, json);
        }

        public async Task SetDefaultCityAsync(long chatId, string city)
        {
            if (!_userSettings.ContainsKey(chatId))
            {
                _userSettings[chatId] = new UserSetting();
            }
            _userSettings[chatId].City = city;
            await SaveSettingsAsync();
        }
        
        public async Task SetUserNotificationTimeAsync(long chatId, TimeSpan time)
        {
            if (_userSettings.ContainsKey(chatId))
            {
                _userSettings[chatId].NotificationTime = time;
                await SaveSettingsAsync();
            }
        }

        public Task<UserSetting> GetUserSettingsAsync(long chatId)
        {
            _userSettings.TryGetValue(chatId, out var settings);
            return Task.FromResult(settings);
        }

        // Логіка щохвилинної перевірки та надсилання сповіщень
        public async Task CheckAndSendNotifications(ITelegramBotClient botClient, WeatherService weatherService)
        {
            var now = DateTime.Now;
            // Скидаємо список опівночі
            if (now.TimeOfDay.Hours == 0 && now.TimeOfDay.Minutes == 0)
            {
                _sentToday.Clear();
            }

            // Знаходимо користувачів, яким час надіслати сповіщення
            var usersToSend = _userSettings
                .Where(s => now.Hour == s.Value.NotificationTime.Hours &&
                            now.Minute == s.Value.NotificationTime.Minutes &&
                            !_sentToday.Contains(s.Key))
                .ToList();

            if (usersToSend.Any())
            {
                Console.WriteLine($"Надсилаємо сповіщення для {usersToSend.Count} користувачів...");
                foreach (var user in usersToSend)
                {
                    var weatherReport = await weatherService.GetCurrentWeatherAsync(user.Value.City);
                    await botClient.SendTextMessageAsync(user.Key, weatherReport, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
                    _sentToday.Add(user.Key); // Позначаємо, що сьогодні вже надіслали
                }
            }
        }
    }
}
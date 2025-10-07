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
        private List<long> _sentToday = new List<long>();

        public UserSettingsService(string filePath)
        {
            _filePath = filePath;
            _userSettings = LoadSettings();
        }

        private Dictionary<long, UserSetting> LoadSettings()
        {
            try
            {
                if (!File.Exists(_filePath)) return new Dictionary<long, UserSetting>();
                var json = File.ReadAllText(_filePath);
                return JsonConvert.DeserializeObject<Dictionary<long, UserSetting>>(json) ?? new Dictionary<long, UserSetting>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка завантаження налаштувань: {ex.Message}");
                return new Dictionary<long, UserSetting>();
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_userSettings, Formatting.Indented);
                // Перевіряємо, чи існує директорія
                var directory = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка збереження налаштувань: {ex.Message}");
            }
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
            if (_userSettings.ContainsKey(chatId) && _userSettings[chatId].City != null)
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

        public async Task CheckAndSendNotifications(ITelegramBotClient botClient, WeatherService weatherService)
        {
            var now = DateTime.Now;
            if (now.Hour == 0 && now.Minute <= 1) _sentToday.Clear();

            var usersToSend = _userSettings
                .Where(s => s.Value.City != null &&
                            now.Hour == s.Value.NotificationTime.Hours &&
                            now.Minute == s.Value.NotificationTime.Minutes &&
                            !_sentToday.Contains(s.Key))
                .ToList();

            foreach (var user in usersToSend)
            {
                var weatherReport = await weatherService.GetCurrentWeatherAsync(user.Value.City);
                if (!weatherReport.StartsWith("Помилка"))
                {
                    await botClient.SendTextMessageAsync(user.Key, weatherReport, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
                    _sentToday.Add(user.Key);
                }
            }
        }

        public async Task<int> BroadcastMessageAsync(ITelegramBotClient botClient, string message)
        {
            var userIds = _userSettings.Keys.ToList();
            int successfulSends = 0;
            foreach (var userId in userIds)
            {
                try
                {
                    await botClient.SendTextMessageAsync(userId, message, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
                    successfulSends++;
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Не вдалося надіслати повідомлення користувачу {userId}: {ex.Message}");
                }
            }
            return successfulSends;
        }
    }
}
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
        private Dictionary<long, bool> _sentToday = new Dictionary<long, bool>();

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
                Console.WriteLine($"[ERROR] Помилка завантаження налаштувань: {ex.Message}");
                return new Dictionary<long, UserSetting>();
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_userSettings, Formatting.Indented);
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await File.WriteAllTextAsync(_filePath, json);
                 Console.WriteLine($"[INFO] Налаштування збережено для {_userSettings.Count} користувачів.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Помилка збереження налаштувань: {ex.Message}");
            }
        }

        public async Task SetUserSettingAsync(long chatId, UserSetting setting)
        {
            _userSettings[chatId] = setting;
            await SaveSettingsAsync();
        }

        public Task<UserSetting> GetUserSettingsAsync(long chatId)
        {
            _userSettings.TryGetValue(chatId, out var settings);
            return Task.FromResult(settings);
        }

        public async Task CheckAndSendNotifications(ITelegramBotClient botClient, WeatherService weatherService)
        {
            var nowUtc = DateTime.UtcNow;
            
            // Опівночі скидаємо статус для всіх
            if (nowUtc.Hour == 0 && nowUtc.Minute == 0) _sentToday.Clear();

            var usersToSend = _userSettings.Where(s => s.Value.City != null).ToList();

            foreach (var userEntry in usersToSend)
            {
                var userId = userEntry.Key;
                var userSetting = userEntry.Value;
                
                // Перевіряємо, чи ми вже надсилали сьогодні
                if (_sentToday.ContainsKey(userId) && _sentToday[userId]) continue;

                var userLocalTime = nowUtc.AddSeconds(userSetting.UtcOffsetSeconds);

                if (userLocalTime.Hour == userSetting.NotificationTime.Hours &&
                    userLocalTime.Minute == userSetting.NotificationTime.Minutes)
                {
                    try
                    {
                        var weatherReport = await weatherService.GetCurrentWeatherAsync(userSetting.City);
                        if (!weatherReport.StartsWith("Помилка"))
                        {
                            await botClient.SendTextMessageAsync(userId, weatherReport, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
                            _sentToday[userId] = true;
                            Console.WriteLine($"[SUCCESS] Сповіщення надіслано користувачу {userId} о {userLocalTime:HH:mm} його місцевого часу.");
                        }
                    }
                    catch (Exception ex)
                    {
                         Console.WriteLine($"[ERROR] Не вдалося надіслати сповіщення користувачу {userId}: {ex.Message}");
                    }
                }
            }
        }
        
        public async Task<int> BroadcastMessageAsync(ITelegramBotClient botClient, string message)
        {
            var userIds = _userSettings.Keys.ToList();
            int successfulSends = 0;
            Console.WriteLine($"[ADMIN] Починаю розсилку для {userIds.Count} користувачів.");
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
                    Console.WriteLine($"[ERROR] Не вдалося надіслати повідомлення користувачу {userId} під час розсилки: {ex.Message}");
                }
            }
            return successfulSends;
        }
    }
}
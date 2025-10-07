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
        // Змінено на словник, щоб ефективно відстежувати статус для кожного
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
                if (!File.Exists(_filePath))
                {
                    Console.WriteLine("[INFO] Файл налаштувань не знайдено, буде створено новий.");
                    return new Dictionary<long, UserSetting>();
                }
                var json = File.ReadAllText(_filePath);
                return JsonConvert.DeserializeObject<Dictionary<long, UserSetting>>(json) ?? new Dictionary<long, UserSetting>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] КРИТИЧНА ПОМИЛКА завантаження налаштувань: {ex.Message}");
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
                 Console.WriteLine($"[INFO] Налаштування збережено. Всього користувачів: {_userSettings.Count}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] КРИТИЧНА ПОМИЛКА збереження налаштувань: {ex.Message}");
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

        // *** ПОВНІСТЮ ПЕРЕПИСАНА ЛОГІКА РОЗСИЛКИ ДЛЯ НАДІЙНОСТІ ***
        public async Task CheckAndSendNotifications(ITelegramBotClient botClient, WeatherService weatherService)
        {
            var nowUtc = DateTime.UtcNow;

            // Раз на добу (опівночі за UTC) очищуємо список тих, кому вже надіслали
            if (nowUtc.Hour == 0 && nowUtc.Minute == 0)
            {
                _sentToday.Clear();
                Console.WriteLine($"[INFO] Нова доба. Список сповіщень очищено.");
            }

            // Перебираємо всіх користувачів з налаштуваннями
            foreach (var userEntry in _userSettings)
            {
                var userId = userEntry.Key;
                var userSetting = userEntry.Value;

                // Пропускаємо, якщо у користувача неповні дані або вже отримав розсилку
                if (userSetting.City == null || _sentToday.ContainsKey(userId))
                {
                    continue;
                }

                // Вираховуємо поточний час для конкретного користувача
                var userLocalTime = nowUtc.AddSeconds(userSetting.UtcOffsetSeconds);
                
                // *** ОСНОВНА ПЕРЕВІРКА ***
                if (userLocalTime.Hour == userSetting.NotificationTime.Hours &&
                    userLocalTime.Minute == userSetting.NotificationTime.Minutes)
                {
                    Console.WriteLine($"[INFO] Час для розсилки користувачу {userId}! Його місцевий час: {userLocalTime:HH:mm}. Надсилаємо погоду для м. {userSetting.City}.");
                    
                    try
                    {
                        var weatherReport = await weatherService.GetCurrentWeatherAsync(userSetting.City);
                        if (!weatherReport.StartsWith("Помилка"))
                        {
                            await botClient.SendTextMessageAsync(userId, weatherReport, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
                            _sentToday[userId] = true; // Позначаємо, що сьогодні вже надіслали
                            Console.WriteLine($"[SUCCESS] Сповіщення для {userId} надіслано успішно.");
                        }
                        else
                        {
                            Console.WriteLine($"[WARNING] Не вдалося отримати звіт про погоду для {userId}.");
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
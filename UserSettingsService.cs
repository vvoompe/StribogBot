using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Stribog
{
    public class UserSettingsService
    {
        private readonly string _filePath;
        private Dictionary<long, UserSetting> _userSettings;
        // Словник для зберігання дати останнього вдалого сповіщення
        private Dictionary<long, DateTime> _lastNotificationDates = new Dictionary<long, DateTime>();

        public UserSettingsService(string filePath)
        {
            _filePath = filePath;
            _userSettings = LoadSettings();
            Console.WriteLine($"[INFO] Завантажено налаштування для {_userSettings.Count} користувачів.");
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

        // *** ПОВНІСТЮ ПЕРЕПИСАНА ЛОГІКА РОЗСИЛКИ ДЛЯ МАКСИМАЛЬНОЇ НАДІЙНОСТІ ***
        public async Task CheckAndSendNotifications(ITelegramBotClient botClient, WeatherService weatherService)
        {
            var nowUtc = DateTime.UtcNow;

            foreach (var userEntry in _userSettings)
            {
                var userId = userEntry.Key;
                var userSetting = userEntry.Value;

                // Пропускаємо користувачів без налаштованого міста або часу
                if (userSetting.City == null || userSetting.NotificationTime == TimeSpan.Zero)
                {
                    continue;
                }

                var userLocalTime = nowUtc.AddSeconds(userSetting.UtcOffsetSeconds);
                _lastNotificationDates.TryGetValue(userId, out var lastSentDate);
                
                // --- НОВА НАДІЙНА ПЕРЕВІРКА ---
                // 1. Час вже настав АБО пройшов?
                // 2. Дата останнього сповіщення - це не сьогоднішня дата?
                if (userLocalTime.TimeOfDay >= userSetting.NotificationTime && lastSentDate.Date < userLocalTime.Date)
                {
                    Console.WriteLine($"[INFO] Знайдено користувача для розсилки! ID: {userId}. Місцевий час: {userLocalTime:HH:mm}. Запланований час: {userSetting.NotificationTime:hh\\:mm}.");
                    
                    try
                    {
                        var weatherReport = await weatherService.GetCurrentWeatherAsync(userSetting.City);
                        if (!weatherReport.StartsWith("Помилка"))
                        {
                            await botClient.SendTextMessageAsync(userId, weatherReport, parseMode: ParseMode.MarkdownV2);
                            _lastNotificationDates[userId] = userLocalTime; // Позначаємо, що сьогодні надіслали
                            Console.WriteLine($"[SUCCESS] Сповіщення для {userId} надіслано успішно.");
                        }
                        else
                        {
                            Console.WriteLine($"[WARNING] Не вдалося отримати звіт про погоду для {userId}, сповіщення не надіслано.");
                        }
                    }
                    catch (Exception ex)
                    {
                         Console.WriteLine($"[ERROR] Не вдалося надіслати сповіщення користувачеві {userId}: {ex.Message}");
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
                    await botClient.SendTextMessageAsync(userId, message, parseMode: ParseMode.MarkdownV2);
                    successfulSends++;
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Не вдалося надіслати повідомлення користувачеві {userId} під час розсилки: {ex.Message}");
                }
            }
            return successfulSends;
        }
    }
}
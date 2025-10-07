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

        // *** ФІНАЛЬНА, НАДІЙНА ЛОГІКА РОЗСИЛКИ ***
        public async Task CheckAndSendNotifications(ITelegramBotClient botClient, WeatherService weatherService)
        {
            var nowUtc = DateTime.UtcNow;
            
            // Робимо копію, щоб уникнути проблем при зміні колекції
            var usersToCheck = _userSettings.ToList();

            foreach (var userEntry in usersToCheck)
            {
                var userId = userEntry.Key;
                var userSetting = userEntry.Value;

                if (userSetting.City == null || userSetting.NotificationTime == TimeSpan.Zero)
                {
                    continue;
                }

                var userLocalTime = nowUtc.AddSeconds(userSetting.UtcOffsetSeconds);
                
                // 1. Час сповіщення вже настав?
                // 2. Сьогоднішня дата більша за дату останнього сповіщення?
                if (userLocalTime.TimeOfDay >= userSetting.NotificationTime && userSetting.LastNotificationDate.Date < userLocalTime.Date)
                {
                    Console.WriteLine($"[INFO] Знайдено користувача для розсилки! ID: {userId}. Місцевий час: {userLocalTime:HH:mm}.");
                    
                    try
                    {
                        var weatherReport = await weatherService.GetCurrentWeatherAsync(userSetting.City);
                        if (!weatherReport.StartsWith("Помилка"))
                        {
                            await botClient.SendTextMessageAsync(userId, weatherReport, parseMode: ParseMode.MarkdownV2);
                            
                            // *** ЗБЕРІГАЄМО ДАТУ В ПОСТІЙНУ ПАМ'ЯТЬ ***
                            userSetting.LastNotificationDate = userLocalTime;
                            await SaveSettingsAsync();
                            
                            Console.WriteLine($"[SUCCESS] Сповіщення для {userId} надіслано успішно. Дата збережена.");
                        }
                        else
                        {
                            Console.WriteLine($"[WARNING] Не вдалося отримати звіт про погоду для {userId}.");
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
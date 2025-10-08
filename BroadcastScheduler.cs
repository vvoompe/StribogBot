using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using TimeZoneConverter;

namespace Stribog
{
    public class BroadcastScheduler
    {
        private readonly ITelegramBotClient _botClient;
        private readonly UserSettingsService _settingsService;
        private readonly WeatherService _weatherService;
        private readonly Dictionary<long, DateTime> _lastBroadcastSent = new();

        public BroadcastScheduler(ITelegramBotClient botClient, UserSettingsService settingsService)
        {
            _botClient = botClient;
            _settingsService = settingsService;
            _weatherService = new WeatherService();
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var allSettings = _settingsService.GetAllSettings()
                        .Where(s => s.DailyWeatherBroadcast && !string.IsNullOrEmpty(s.BroadcastTime));

                    foreach (var user in allSettings)
                    {
                        if (!TimeSpan.TryParse(user.BroadcastTime, out var localBroadcastTime))
                        {
                            continue;
                        }

                        // Отримуємо часовий пояс користувача, за замовчуванням - UTC
                        TimeZoneInfo userTimeZone;
                        try
                        {
                            // Використовуємо TimeZoneConverter для сумісності між Windows та IANA ID
                            userTimeZone = TZConvert.GetTimeZoneInfo(user.TimeZoneId ?? "UTC");
                        }
                        catch
                        {
                            userTimeZone = TimeZoneInfo.Utc; // Якщо вказано невірний ID, повертаємось до UTC
                        }

                        // Конвертуємо поточний час UTC в локальний час користувача
                        var nowInUserTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, userTimeZone);
                        
                        // Перевіряємо, чи настав час для розсилки В ЛОКАЛЬНОМУ ЧАСІ користувача
                        if (nowInUserTz.TimeOfDay >= localBroadcastTime)
                        {
                            // Перевіряємо, чи ми вже надсилали розсилку цьому користувачу СЬОГОДНІ за його локальним часом
                            if (!_lastBroadcastSent.TryGetValue(user.ChatId, out var lastSent) || lastSent.Date < nowInUserTz.Date)
                            {
                                var cityToUse = !string.IsNullOrEmpty(user.BroadcastCity) ? user.BroadcastCity : user.City;
                                if (string.IsNullOrEmpty(cityToUse)) continue;

                                string weatherInfo = await _weatherService.GetWeatherAsync(cityToUse);
                                await _botClient.SendTextMessageAsync(
                                    chatId: user.ChatId,
                                    text: $"*🔔 Ваша щоденна розсилка погоди:*\n\n{weatherInfo}",
                                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                                    cancellationToken: cancellationToken);
                                
                                // Оновлюємо час останньої розсилки, зберігаючи час UTC
                                _lastBroadcastSent[user.ChatId] = DateTime.UtcNow;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SCHEDULER ERROR] {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
        }
    }
}
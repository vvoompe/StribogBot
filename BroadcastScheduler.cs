using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Stribog;

namespace Stribog
{
    public class BroadcastScheduler
    {
        private readonly ITelegramBotClient _botClient;
        private readonly UserSettingsService _settingsService;
        private readonly WeatherService _weatherService;
        // Словник для відстеження вже надісланих розсилок, щоб уникнути дублів
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
                    var allSettings = _settingsService.GetAllSettings();
                    var now = DateTime.UtcNow; // Працюємо в UTC для універсальності

                    foreach (var user in allSettings)
                    {
                        // Пропускаємо, якщо розсилка вимкнена або не вказано час
                        if (!user.DailyWeatherBroadcast || string.IsNullOrEmpty(user.BroadcastTime) || !TimeSpan.TryParse(user.BroadcastTime, out var broadcastTime))
                        {
                            continue;
                        }

                        // TODO: В майбутньому тут можна додати логіку для перетворення часу з урахуванням TimeZoneId користувача
                        // Зараз час перевіряється за UTC
                        var todayBroadcastTimeUtc = now.Date + broadcastTime;

                        // Перевіряємо, чи настав час для розсилки
                        if (now >= todayBroadcastTimeUtc)
                        {
                            // Перевіряємо, чи ми вже надсилали розсилку цьому користувачу сьогодні
                            if (!_lastBroadcastSent.TryGetValue(user.ChatId, out var lastSent) || lastSent.Date < now.Date)
                            {
                                var cityToUse = !string.IsNullOrEmpty(user.BroadcastCity) ? user.BroadcastCity : user.City;
                                if (string.IsNullOrEmpty(cityToUse)) continue;

                                string weatherInfo = await _weatherService.GetWeatherAsync(cityToUse);
                                await _botClient.SendTextMessageAsync(
                                    chatId: user.ChatId,
                                    text: $"*🔔 Ваша щоденна розсилка погоди:*\n\n{weatherInfo}",
                                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                                    cancellationToken: cancellationToken);

                                // Оновлюємо час останньої розсилки для цього користувача
                                _lastBroadcastSent[user.ChatId] = now;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Логування помилки, щоб бачити проблеми в роботі планувальника
                    Console.WriteLine($"[SCHEDULER ERROR] Помилка в планувальнику розсилок: {ex.Message}");
                }

                // Чекаємо одну хвилину до наступної перевірки
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
        }
    }
}
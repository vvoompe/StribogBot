using System;
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

                    foreach (var user in allSettings)
                    {
                        if (!user.DailyWeatherBroadcast || string.IsNullOrEmpty(user.BroadcastTime) || !TimeSpan.TryParse(user.BroadcastTime, out var localBroadcastTime))
                        {
                            continue;
                        }

                        TimeZoneInfo userTimeZone;
                        try
                        {
                            userTimeZone = TZConvert.GetTimeZoneInfo(user.TimeZoneId ?? "UTC");
                        }
                        catch
                        {
                            userTimeZone = TimeZoneInfo.Utc;
                        }

                        var utcNow = DateTime.UtcNow;
                        var nowInUserTz = TimeZoneInfo.ConvertTimeFromUtc(utcNow, userTimeZone);

                        if (nowInUserTz.TimeOfDay >= localBroadcastTime)
                        {
                            // ВИПРАВЛЕНО: Перевіряємо час останньої розсилки з бази даних
                            var lastSentInUserTz = user.LastBroadcastSentUtc.HasValue
                                ? TimeZoneInfo.ConvertTimeFromUtc(user.LastBroadcastSentUtc.Value, userTimeZone)
                                : (DateTime?)null;

                            if (lastSentInUserTz == null || lastSentInUserTz.Value.Date < nowInUserTz.Date)
                            {
                                Console.WriteLine($"[SCHEDULER] Sending broadcast to user {user.ChatId}.");
                                
                                var cityToUse = !string.IsNullOrEmpty(user.BroadcastCity) ? user.BroadcastCity : user.City;
                                if (string.IsNullOrEmpty(cityToUse)) continue;

                                string weatherInfo = await _weatherService.GetWeatherAsync(cityToUse);
                                await _botClient.SendTextMessageAsync(
                                    chatId: user.ChatId,
                                    text: $"*🔔 Ваша щоденна розсилка погоди:*\n\n{weatherInfo}",
                                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                                    cancellationToken: cancellationToken);

                                // Оновлюємо час останньої розсилки в базі даних
                                user.LastBroadcastSentUtc = utcNow;
                                _settingsService.SaveUserSettings(user);
                                
                                Console.WriteLine($"[SCHEDULER] Broadcast sent successfully to {user.ChatId}.");
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
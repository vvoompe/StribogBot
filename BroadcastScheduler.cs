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

                        // --- ЛОГУВАННЯ: Початок перевірки для користувача ---
                        Console.WriteLine($"[SCHEDULER] Checking user {user.ChatId} with broadcast time {user.BroadcastTime}");

                        TimeZoneInfo userTimeZone;
                        try
                        {
                            userTimeZone = TZConvert.GetTimeZoneInfo(user.TimeZoneId ?? "UTC");
                        }
                        catch
                        {
                            userTimeZone = TimeZoneInfo.Utc;
                            Console.WriteLine($"[SCHEDULER] User {user.ChatId} has invalid TimeZoneId. Defaulting to UTC.");
                        }

                        var utcNow = DateTime.UtcNow;
                        var nowInUserTz = TimeZoneInfo.ConvertTimeFromUtc(utcNow, userTimeZone);
                        
                        // --- ЛОГУВАННЯ: Час ---
                        Console.WriteLine($"[SCHEDULER] UTC time: {utcNow:HH:mm:ss}. User's local time ({userTimeZone.Id}): {nowInUserTz:HH:mm:ss}");

                        if (nowInUserTz.TimeOfDay >= localBroadcastTime)
                        {
                            if (!_lastBroadcastSent.TryGetValue(user.ChatId, out var lastSent) || lastSent.Date < nowInUserTz.Date)
                            {
                                // --- ЛОГУВАННЯ: Надсилання розсилки ---
                                Console.WriteLine($"[SCHEDULER] Sending broadcast to user {user.ChatId}. Reason: Time matched and not sent today.");
                                
                                var cityToUse = !string.IsNullOrEmpty(user.BroadcastCity) ? user.BroadcastCity : user.City;
                                if (string.IsNullOrEmpty(cityToUse))
                                {
                                    Console.WriteLine($"[SCHEDULER] Skipping user {user.ChatId}: city not set.");
                                    continue;
                                }

                                string weatherInfo = await _weatherService.GetWeatherAsync(cityToUse);
                                await _botClient.SendTextMessageAsync(
                                    chatId: user.ChatId,
                                    text: $"*🔔 Ваша щоденна розсилка погоди:*\n\n{weatherInfo}",
                                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                                    cancellationToken: cancellationToken);
                                
                                _lastBroadcastSent[user.ChatId] = utcNow;
                                Console.WriteLine($"[SCHEDULER] Broadcast sent successfully to {user.ChatId}.");
                            }
                            else
                            {
                                // --- ЛОГУВАННЯ: Пропуск (вже надіслано) ---
                                Console.WriteLine($"[SCHEDULER] Skipping user {user.ChatId}. Reason: Already sent today.");
                            }
                        }
                        else
                        {
                            // --- ЛОГУВАННЯ: Пропуск (ще не час) ---
                            Console.WriteLine($"[SCHEDULER] Skipping user {user.ChatId}. Reason: It's not time yet ({nowInUserTz.TimeOfDay} < {localBroadcastTime}).");
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
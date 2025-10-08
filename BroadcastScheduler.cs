﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Stribog;

namespace Stribog;

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
        var lastSent = new Dictionary<long, DateTime>(); // чат_id -> last sent UTC timestamp
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var allSettings = _settingsService.GetAllSettings();
                var nowUtc = DateTime.UtcNow;

                foreach (var s in allSettings)
                {
                    if (!s.DailyWeatherBroadcast) continue;
                    var cityToUse = string.IsNullOrEmpty(s.BroadcastCity) ? s.City : s.BroadcastCity;
                    if (string.IsNullOrEmpty(cityToUse)) continue;

                    // Parse broadcast time (HH:mm). Враховуємо TZ міста
                    if (!TimeSpan.TryParse(s.BroadcastTime ?? "", out var targetTime))
                    {
                        continue;
                    }

                    // Визначимо TZ для користувача
                    TimeZoneInfo tz;
                    try
                    {
                        tz = string.IsNullOrEmpty(s.TimeZoneId) ? TimeZoneInfo.Utc
                            : TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId);
                    }
                    catch
                    {
                        tz = TimeZoneInfo.Utc;
                    }

                    // Тепер знайдемо локальний сьогоднішній день користувача і конвертуємо до UTC
                    var nowInZone = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
                    var localDate = new DateTime(nowInZone.Year, nowInZone.Month, nowInZone.Day, targetTime.Hours, targetTime.Minutes, 0, DateTimeKind.Unspecified);
                    var todayTargetUtc = TimeZoneInfo.ConvertTimeToUtc(localDate, tz);

                    // Готові умови для розсилки: приблизно коли зараз UTC в окні розсилки (наприклад +-2 хвилини)
                    if (nowUtc >= todayTargetUtc && nowUtc < todayTargetUtc.AddMinutes(2))
                    {
                        // Переконаємось, що не надсилаємо занадто часто
                        if (lastSent.TryGetValue(s.ChatId, out var last) && (nowUtc - last).TotalHours < 1) continue;

                        string weatherInfo;
                        try
                        {
                            weatherInfo = await _weatherService.GetWeatherAsync(cityToUse);
                        }
                        catch
                        {
                            // Пропустити помилку для цього чату
                            continue;
                        }

                        await _botClient.SendTextMessageAsync(
                            chatId: s.ChatId,
                            text: weatherInfo,
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                            cancellationToken: cancellationToken);

                        lastSent[s.ChatId] = nowUtc;
                    }
                }
            }
            catch
            {
                // Пропустити помилку і продовжити цикл
            }

            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }
}

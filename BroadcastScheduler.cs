using System;
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
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var allSettings = _settingsService.GetAllSettings();
                foreach (var s in allSettings)
                {
                    if (!s.DailyWeatherBroadcast) continue;
                    var cityToUse = string.IsNullOrEmpty(s.BroadcastCity) ? s.City : s.BroadcastCity;
                    if (string.IsNullOrEmpty(cityToUse)) continue;

                    // TODO: Реалізація з TZ — поки демонструємо базову версію без TZ
                    string weatherInfo = await _weatherService.GetWeatherAsync(cityToUse);
                    await _botClient.SendTextMessageAsync(
                        chatId: s.ChatId,
                        text: weatherInfo,
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
            }
            catch
            {
                // лог або тишина
            }

            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }
}
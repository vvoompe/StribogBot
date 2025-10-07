using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Stribog
{
    class Program
    {
        private static readonly string BotToken = "8351856913:AAFpwghkCYm_Y_Q7b97vQbDUsTMp6UxtpW8";
        private static readonly string WeatherApiKey = "7c39b15a9902c7fa7d10849aeb538a45";

        static async Task Main(string[] args)
        {
            var botClient = new TelegramBotClient(BotToken);
            var weatherService = new WeatherService(WeatherApiKey);
            var userSettingsService = new UserSettingsService("/data/users.json");
            var updateHandlers = new UpdateHandlers(botClient, weatherService, userSettingsService);

            using var cts = new CancellationTokenSource();

            botClient.StartReceiving(
                updateHandler: updateHandlers.HandleUpdateAsync,
                pollingErrorHandler: (bc, ex, ct) => Task.CompletedTask, // помилки обробляються всередині
                receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Бот {me.Username} запущений.");

            // *** ЗМІНА: Таймер спрацьовує кожну хвилину ***
            var timer = new Timer(
                async _ => await userSettingsService.CheckAndSendNotifications(botClient, weatherService),
                null,
                TimeSpan.Zero,       // Запустити одразу
                TimeSpan.FromMinutes(1) // Повторювати кожну хвилину
            );

            await Task.Delay(-1);
        }
    }
}
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
        // --- ВАШІ ДАНІ ---
        private static readonly string BotToken = "8351856913:AAFpwghkCYm_Y_Q7b97vQbDUsTMp6UxtpW8"; // токен бота
        private static readonly string WeatherApiKey = "7c39b15a9902c7fa7d10849aeb538a45"; //  ключ OpenWeatherMap

        static async Task Main(string[] args)
        {
            var botClient = new TelegramBotClient(BotToken);
            var weatherService = new WeatherService(WeatherApiKey);
            var userSettingsService = new UserSettingsService("users.json");

            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            var updateHandlers = new UpdateHandlers(botClient, weatherService, userSettingsService);

            botClient.StartReceiving(
                updateHandler: updateHandlers.HandleUpdateAsync,
                pollingErrorHandler: updateHandlers.HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Бот {me.Username} запущений.");

            // Запускаємо щоденну розсилку (наприклад, о 8:00 ранку)
            TimeSpan notificationTime = new TimeSpan(8, 0, 0);
            var timer = new Timer(
                async _ => await userSettingsService.SendWeatherNotifications(botClient, weatherService),
                null,
                GetInitialDelay(notificationTime), // Час до першого запуску
                TimeSpan.FromHours(24) // Повторювати кожні 24 години
            );

            await Task.Delay(-1);
        }
        
        // Допоміжний метод для розрахунку часу до першого запуску таймера
        private static TimeSpan GetInitialDelay(TimeSpan targetTime)
        {
            var now = DateTime.Now.TimeOfDay;
            var delay = targetTime - now;
            if (delay < TimeSpan.Zero)
            {
                delay += TimeSpan.FromHours(24);
            }
            return delay;
        }
    }
}
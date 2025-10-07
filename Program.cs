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
        // ВАЖЛИВО: Переконайтесь, що ви додали ці змінні у налаштуваннях Railway
        // Я залишив ваші токени, але обернув їх у зчитування зі змінних середовища
        private static readonly string BotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "6939949938:AAHT-d_zt19W4yYt-xT2V6DQQn-l1bJkfrA";
        private static readonly string WeatherApiKey = Environment.GetEnvironmentVariable("WEATHER_API_KEY") ?? "439FF154955755495941affa13454489";
        private static readonly string AdminId = Environment.GetEnvironmentVariable("ADMIN_ID") ?? "962460578";

        static async Task Main(string[] args)
        {
            var botClient = new TelegramBotClient(BotToken);
            var weatherService = new WeatherService(WeatherApiKey);
            // Використовуємо постійне сховище на Railway
            var userSettingsService = new UserSettingsService("/data/users.json");
            var updateHandlers = new UpdateHandlers(botClient, weatherService, userSettingsService, AdminId);

            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };

            botClient.StartReceiving(
                updateHandler: updateHandlers.HandleUpdateAsync,
                pollingErrorHandler: (bc, ex, ct) => { Console.WriteLine($"Помилка опитування: {ex.Message}"); return Task.CompletedTask; },
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Бот {me.Username} запущений.");

            var timer = new Timer(
                async _ => await userSettingsService.CheckAndSendNotifications(botClient, weatherService),
                null,
                TimeSpan.FromSeconds(10), // Перший запуск через 10 секунд
                TimeSpan.FromMinutes(1)   // Повторювати кожну хвилину
            );

            await Task.Delay(-1);
        }
    }
}
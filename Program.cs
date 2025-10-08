using Stribog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? throw new InvalidOperationException("BOT_TOKEN is not set.");
var weatherApiKey = Environment.GetEnvironmentVariable("WEATHER_API_KEY") ?? throw new InvalidOperationException("WEATHER_API_KEY is not set.");
var adminId = Environment.GetEnvironmentVariable("ADMIN_ID") ?? throw new InvalidOperationException("ADMIN_ID is not set.");

var botClient = new TelegramBotClient(botToken);
var weatherService = new WeatherService(weatherApiKey);
var userSettingsService = new UserSettingsService("users.json");
var updateHandlers = new UpdateHandlers(weatherService, userSettingsService, adminId);

using var cts = new CancellationTokenSource();

var receiverOptions = new ReceiverOptions
{
    AllowedUpdates = { }
};

// ВИПРАВЛЕНО: Повертаємо правильні імена параметрів
botClient.StartReceiving(
    updateHandler: updateHandlers.HandleUpdateAsync,
    pollingErrorHandler: updateHandlers.HandlePollingErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: cts.Token
);

var me = await botClient.GetMeAsync();
Console.WriteLine($"Бот @{me.Username} запущений.");
Console.ReadLine();

cts.Cancel();
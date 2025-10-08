using Stribog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

var botToken = Environment.GetEnvironmentVariable("8351856913:AAFpwghkCYm_Y_Q7b97vQbDUsTMp6UxtpW8") ?? throw new InvalidOperationException("BOT_TOKEN is not set.");
var weatherApiKey = Environment.GetEnvironmentVariable("7c39b15a9902c7fa7d10849aeb538a45") ?? throw new InvalidOperationException("WEATHER_API_KEY is not set.");
var adminId = Environment.GetEnvironmentVariable("962460578") ?? throw new InvalidOperationException("ADMIN_ID is not set.");

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
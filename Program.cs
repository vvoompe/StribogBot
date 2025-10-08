using Stribog;
using Telegram.Bot;

// --- 1. Отримання налаштувань зі змінних оточення ---
var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
var adminChatId = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID");
var weatherApiKey = Environment.GetEnvironmentVariable("OPENWEATHERMAP_API_KEY");

// --- 2. Перевірка, чи всі необхідні змінні встановлені ---
if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(weatherApiKey))
{
    Console.WriteLine("[FATAL ERROR] Не всі змінні оточення встановлено!");
    Console.WriteLine("Перевірте наявність: TELEGRAM_BOT_TOKEN, OPENWEATHERMAP_API_KEY");
    return;
}

var botClient = new TelegramBotClient(botToken);
using var cts = new CancellationTokenSource();

// --- 3. Запуск бота ---
botClient.StartReceiving(
    updateHandler: UpdateHandlers.HandleUpdateAsync,
    pollingErrorHandler: UpdateHandlers.HandlePollingErrorAsync,
    cancellationToken: cts.Token
);

var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
Console.WriteLine($"Бот {me.Username} успішно запущений.");

// --- 4. Сповіщення адміна про запуск (якщо вказано ID) ---
if (!string.IsNullOrEmpty(adminChatId))
{
    try
    {
        await botClient.SendTextMessageAsync(
            chatId: adminChatId,
            text: $"✅ Бот *{me.Username}* успішно запущений!\nЧас: {DateTime.Now:g}",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            cancellationToken: cts.Token);
        Console.WriteLine($"Сповіщення про запуск надіслано адміну.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Не вдалося надіслати сповіщення адміну: {ex.Message}");
    }
}

await Task.Delay(-1, cts.Token);
using Stribog;
using Telegram.Bot;
using System.Threading;

// --- 1) Отримання налаштувань зі змінних оточення ---
var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
var adminChatId = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID");
var weatherApiKey = Environment.GetEnvironmentVariable("OPENWEATHERMAP_API_KEY");
using (var conn = new Npgsql.NpgsqlConnection(
           Environment.GetEnvironmentVariable("DATABASE_URL")))
{
    conn.Open();
    var sql = File.ReadAllText("Migrations/01_create_usersettings.sql");
    using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
    cmd.ExecuteNonQuery();
}


// --- 2) Перевірка змінних ---
if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(weatherApiKey))
{
    Console.WriteLine("[FATAL ERROR] Не всі змінні оточення встановлено!");
    Console.WriteLine("Перевірте наявність: TELEGRAM_BOT_TOKEN, OPENWEATHERMAP_API_KEY");
    return;
}

var botClient = new TelegramBotClient(botToken);
using var cts = new CancellationTokenSource();

// --- 3) Запуск бота ---
botClient.StartReceiving(
    updateHandler: UpdateHandlers.HandleUpdateAsync,
    pollingErrorHandler: UpdateHandlers.HandlePollingErrorAsync,
    cancellationToken: cts.Token
);

var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
Console.WriteLine($"Бот {me.Username} успішно запущений.");

// --- 4. Сповіщення адміністратору (якщо задано) ---
if (!string.IsNullOrEmpty(adminChatId))
{
    try
    {
        await botClient.SendTextMessageAsync(
            chatId: adminChatId,
            text: $"✅ Бот *{me.Username}* успішно запущений!\nЧас: {DateTime.Now:g}",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            cancellationToken: cts.Token);
        Console.WriteLine($"Сповіщення адміну надіслано.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Не вдалося надіслати сповіщення адміну: {ex.Message}");
    }
}

// --- 5) Запуск розсилок як фонове завдання (Scheduler) ---
var scheduler = new Stribog.BroadcastScheduler(botClient, new UserSettingsService());
Task.Run(async () => await scheduler.RunAsync(cts.Token));

// --- 6) Очікування завершення ---
await Task.Delay(-1, cts.Token);
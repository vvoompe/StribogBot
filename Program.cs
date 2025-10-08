using Stribog;
using Telegram.Bot;
using System.Threading;

try
{
    // --- 1) Отримання налаштувань зі змінних оточення ---
    var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
    var adminChatId = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID");
    var weatherApiKey = Environment.GetEnvironmentVariable("OPENWEATHERMAP_API_KEY");
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

    // --- 2) Перевірка змінних ---
    if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(weatherApiKey) || string.IsNullOrEmpty(databaseUrl))
    {
        Console.WriteLine("[FATAL ERROR] Не всі змінні оточення встановлено!");
        Console.WriteLine($"TELEGRAM_BOT_TOKEN: {(string.IsNullOrEmpty(botToken) ? "❌" : "✅")}");
        Console.WriteLine($"OPENWEATHERMAP_API_KEY: {(string.IsNullOrEmpty(weatherApiKey) ? "❌" : "✅")}");
        Console.WriteLine($"DATABASE_URL: {(string.IsNullOrEmpty(databaseUrl) ? "❌" : "✅")}");
        return;
    }

    Console.WriteLine("✅ Всі змінні оточення встановлено");

    // --- 3) Ініціалізація БД ---
    Console.WriteLine("🔄 Виконання міграцій БД...");
    using (var conn = new Npgsql.NpgsqlConnection(databaseUrl))
    {
        conn.Open();
        var sql = File.ReadAllText("Migrations/01_create_usersettings.sql");
        using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }
    Console.WriteLine("✅ Міграції виконано успішно");

    var botClient = new TelegramBotClient(botToken);
    using var cts = new CancellationTokenSource();

    // --- 4) Запуск бота ---
    botClient.StartReceiving(
        updateHandler: UpdateHandlers.HandleUpdateAsync,
        pollingErrorHandler: UpdateHandlers.HandlePollingErrorAsync,
        cancellationToken: cts.Token
    );

    var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
    Console.WriteLine($"✅ Бот @{me.Username} успішно запущений!");

    // --- 5) Сповіщення адміністратору ---
    if (!string.IsNullOrEmpty(adminChatId))
    {
        try
        {
            await botClient.SendTextMessageAsync(
                chatId: adminChatId,
                text: $"✅ Бот *@{me.Username}* успішно запущений на Railway!\nЧас: {DateTime.Now:g}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: cts.Token);
            Console.WriteLine("✅ Сповіщення адміну надіслано");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Не вдалося надіслати сповіщення адміну: {ex.Message}");
        }
    }

    // --- 6) Запуск розсилок ---
    var scheduler = new BroadcastScheduler(botClient, new UserSettingsService());
    _ = Task.Run(async () => await scheduler.RunAsync(cts.Token));
    Console.WriteLine("✅ Scheduler запущено");

    // --- 7) Очікування завершення ---
    Console.WriteLine("🤖 Бот працює. Натисніть Ctrl+C для зупинки...");
    await Task.Delay(-1, cts.Token);
}
catch (Exception ex)
{
    Console.WriteLine($"❌ FATAL ERROR: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    throw;
}
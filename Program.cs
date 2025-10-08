using Stribog;
using Telegram.Bot;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("🚀 Запуск Telegram Weather Bot...");

            var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            var adminChatId = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID");
            var weatherApiKey = Environment.GetEnvironmentVariable("OPENWEATHERMAP_API_KEY");
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

            Console.WriteLine("\n📋 Перевірка змінних оточення:");
            Console.WriteLine($"TELEGRAM_BOT_TOKEN: {(string.IsNullOrEmpty(botToken) ? "❌ Відсутній" : "✅ Встановлено")}");
            Console.WriteLine($"OPENWEATHERMAP_API_KEY: {(string.IsNullOrEmpty(weatherApiKey) ? "❌ Відсутній" : "✅ Встановлено")}");
            Console.WriteLine($"DATABASE_URL: {(string.IsNullOrEmpty(databaseUrl) ? "❌ Відсутній" : "✅ Встановлено")}");
            Console.WriteLine($"ADMIN_CHAT_ID: {(string.IsNullOrEmpty(adminChatId) ? "⚠️ Не встановлено (опціонально)" : "✅ Встановлено")}");

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(weatherApiKey) || string.IsNullOrEmpty(databaseUrl))
            {
                Console.WriteLine("\n❌ FATAL ERROR: Одна або декілька обов'язкових змінних оточення не встановлено.");
                return;
            }

            Console.WriteLine("\n✅ Всі обов'язкові змінні оточення встановлено");

            Console.WriteLine("\n🔄 Виконання міграцій БД...");
            try
            {
                var connectionString = UserSettingsService.BuildConnectionString(databaseUrl);
                using (var conn = new Npgsql.NpgsqlConnection(connectionString))
                {
                    Console.WriteLine("   Підключення до бази даних...");
                    await conn.OpenAsync();
                    Console.WriteLine("   ✅ З'єднання встановлено");

                    var sqlFile = Path.Combine(AppContext.BaseDirectory, "Migrations", "01_create_usersettings.sql");
                    if (!File.Exists(sqlFile))
                    {
                        Console.WriteLine($"   ⚠️ Файл міграції не знайдено: {sqlFile}");
                        Console.WriteLine("   Спроба створити таблицю вручну...");
                        
                        var sql = @"
                            CREATE TABLE IF NOT EXISTS usersettings (
                                chatid BIGINT PRIMARY KEY,
                                city VARCHAR(255),
                                dailyweatherbroadcast BOOLEAN DEFAULT FALSE,
                                broadcastcity VARCHAR(255),
                                broadcasttime VARCHAR(10),
                                timezoneid VARCHAR(100),
                                createdat TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                updatedat TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                            );
                            
                            CREATE INDEX IF NOT EXISTS idx_daily_broadcast 
                            ON usersettings(dailyweatherbroadcast) 
                            WHERE dailyweatherbroadcast = TRUE;
                        ";
                        
                        using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        Console.WriteLine($"   Читання міграції з: {sqlFile}");
                        var sql = await File.ReadAllTextAsync(sqlFile);
                        using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                Console.WriteLine("✅ Міграції виконано успішно\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Помилка при ініціалізації БД: {ex.Message}");
                Console.WriteLine("\nПеревірте:");
                Console.WriteLine("1. Формат DATABASE_URL (має бути: postgresql://user:password@host:port/database)");
                Console.WriteLine("2. Доступність бази даних");
                Console.WriteLine("3. Правильність credentials");
                throw;
            }

            var botClient = new TelegramBotClient(botToken);
            using var cts = new CancellationTokenSource();

            Console.WriteLine("🤖 Запуск Telegram бота...");
            botClient.StartReceiving(
                updateHandler: UpdateHandlers.HandleUpdateAsync,
                pollingErrorHandler: UpdateHandlers.HandlePollingErrorAsync,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
            Console.WriteLine($"✅ Бот @{me.Username} успішно запущений!");
            
            if (!string.IsNullOrEmpty(adminChatId))
            {
                try
                {
                    await botClient.SendTextMessageAsync(
                        chatId: adminChatId,
                        text: $"✅ Бот *@{me.Username}* успішно запущений на Railway!",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                        cancellationToken: cts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Не вдалося надіслати сповіщення адміну: {ex.Message}");
                }
            }

            Console.WriteLine("\n📯 Запуск планувальника розсилок...");
            var scheduler = new BroadcastScheduler(botClient, new UserSettingsService());
            _ = Task.Run(() => scheduler.RunAsync(cts.Token), cts.Token);
            Console.WriteLine("✅ Планувальник розсилок запущено");

            Console.WriteLine("\n🎉 БОТ ПОВНІСТЮ ЗАПУЩЕНИЙ ТА ГОТОВИЙ ДО РОБОТИ!");

            await Task.Delay(-1, cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ CRITICAL ERROR: {ex.GetType().Name} - {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner Exception: {ex.InnerException.Message}");
            }
        }
    }
}
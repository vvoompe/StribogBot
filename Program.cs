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

            // --- 1) Отримання налаштувань зі змінних оточення ---
            var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            var adminChatId = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID");
            var weatherApiKey = Environment.GetEnvironmentVariable("OPENWEATHERMAP_API_KEY");
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

            // --- 2) Перевірка змінних ---
            Console.WriteLine("\n📋 Перевірка змінних оточення:");
            Console.WriteLine($"TELEGRAM_BOT_TOKEN: {(string.IsNullOrEmpty(botToken) ? "❌ Відсутній" : "✅ Встановлено")}");
            Console.WriteLine($"OPENWEATHERMAP_API_KEY: {(string.IsNullOrEmpty(weatherApiKey) ? "❌ Відсутній" : "✅ Встановлено")}");
            Console.WriteLine($"DATABASE_URL: {(string.IsNullOrEmpty(databaseUrl) ? "❌ Відсутній" : "✅ Встановлено")}");
            Console.WriteLine($"ADMIN_CHAT_ID: {(string.IsNullOrEmpty(adminChatId) ? "⚠️ Не встановлено (опціонально)" : "✅ Встановлено")}");

            if (string.IsNullOrEmpty(botToken))
            {
                Console.WriteLine("\n❌ FATAL ERROR: TELEGRAM_BOT_TOKEN не встановлено!");
                Console.WriteLine("Отримайте токен у @BotFather та встановіть змінну оточення.");
                return;
            }

            if (string.IsNullOrEmpty(weatherApiKey))
            {
                Console.WriteLine("\n❌ FATAL ERROR: OPENWEATHERMAP_API_KEY не встановлено!");
                Console.WriteLine("Зареєструйтесь на https://openweathermap.org/api та отримайте API ключ.");
                return;
            }

            if (string.IsNullOrEmpty(databaseUrl))
            {
                Console.WriteLine("\n❌ FATAL ERROR: DATABASE_URL не встановлено!");
                Console.WriteLine("Налаштуйте PostgreSQL базу даних та встановіть DATABASE_URL.");
                return;
            }

            Console.WriteLine("\n✅ Всі обов'язкові змінні оточення встановлено");

            // --- 3) Ініціалізація БД ---
            Console.WriteLine("\n🔄 Виконання міграцій БД...");
            try
            {
                // Перевірка формату DATABASE_URL
                if (!databaseUrl.StartsWith("postgres://") && !databaseUrl.StartsWith("postgresql://"))
                {
                    Console.WriteLine($"⚠️ DATABASE_URL має нестандартний формат: {databaseUrl.Substring(0, Math.Min(20, databaseUrl.Length))}...");
                }

                using (var conn = new Npgsql.NpgsqlConnection(databaseUrl))
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

            // --- 4) Створення та запуск бота ---
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
            Console.WriteLine($"   ID: {me.Id}");
            Console.WriteLine($"   Ім'я: {me.FirstName}");

            // --- 5) Сповіщення адміністратору ---
            if (!string.IsNullOrEmpty(adminChatId))
            {
                try
                {
                    Console.WriteLine($"\n📤 Надсилання сповіщення адміністратору (Chat ID: {adminChatId})...");
                    await botClient.SendTextMessageAsync(
                        chatId: adminChatId,
                        text: $"✅ Бот *@{me.Username}* успішно запущений на Railway!\n\n" +
                              $"🕐 Час запуску: {DateTime.Now:dd.MM.yyyy HH:mm:ss}\n" +
                              $"🌍 Сервер: Railway",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                        cancellationToken: cts.Token);
                    Console.WriteLine("✅ Сповіщення адміну надіслано");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Не вдалося надіслати сповіщення адміну: {ex.Message}");
                    Console.WriteLine("   Перевірте правильність ADMIN_CHAT_ID");
                }
            }

            // --- 6) Запуск розсилок ---
            Console.WriteLine("\n📯 Запуск планувальника розсилок...");
            var scheduler = new BroadcastScheduler(botClient, new UserSettingsService());
            _ = Task.Run(async () => 
            {
                try
                {
                    await scheduler.RunAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Помилка в планувальнику: {ex.Message}");
                }
            });
            Console.WriteLine("✅ Планувальник розсилок запущено");

            // --- 7) Обробка завершення ---
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("\n\n⏹️ Отримано сигнал зупинки...");
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("🎉 БОТ ПОВНІСТЮ ЗАПУЩЕНИЙ ТА ГОТОВИЙ ДО РОБОТИ!");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine("\n💡 Для зупинки натисніть Ctrl+C\n");

            // --- 8) Очікування завершення ---
            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("✅ Бот коректно зупинено");
            }
        }
        catch (Npgsql.NpgsqlException ex)
        {
            Console.WriteLine($"\n❌ ПОМИЛКА ПІДКЛЮЧЕННЯ ДО БД:");
            Console.WriteLine($"   {ex.Message}");
            Console.WriteLine("\n🔍 Можливі причини:");
            Console.WriteLine("   1. Неправильний формат DATABASE_URL");
            Console.WriteLine("   2. База даних недоступна");
            Console.WriteLine("   3. Неправильні credentials");
            Console.WriteLine("\n💡 Приклад правильного формату:");
            Console.WriteLine("   postgresql://user:password@host:5432/database");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ CRITICAL ERROR: {ex.GetType().Name}");
            Console.WriteLine($"   Повідомлення: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"\n   Внутрішня помилка: {ex.InnerException.Message}");
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups; // НОВИЙ using для клавіатур

namespace WeatherBot
{
    class Program
    {
        private static readonly string BotToken = "8351856913:AAFpwghkCYm_Y_Q7b97vQbDUsTMp6UxtpW8";
        private static readonly string OpenWeatherApiKey = "7c39b15a9902c7fa7d10849aeb538a45";
        private static readonly HttpClient _httpClient = new HttpClient();

        static async Task Main(string[] args)
        {
            var botClient = new TelegramBotClient(BotToken);
            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions
            {
                // Тепер ми хочемо отримувати і повідомлення, і натискання кнопок
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Бот {me.Username} запущений і чекає на повідомлення.");

            await Task.Delay(-1);
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // === НОВА ЛОГІКА: ОБРОБКА НАТИСКАННЯ КНОПОК ===
            if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQuery(botClient, update.CallbackQuery, cancellationToken);
                return;
            }

            // === СТАРА ЛОГІКА: ОБРОБКА ТЕКСТОВИХ ПОВІДОМЛЕНЬ ===
            if (update.Message is not { Text: { } messageText })
                return;

            var chatId = update.Message.Chat.Id;
            Console.WriteLine($"Отримано повідомлення в чаті {chatId}: '{messageText}'");

            string responseMessage;
            InlineKeyboardMarkup keyboard = null; // Змінна для нашої клавіатури

            if (messageText.ToLower() == "/start")
            {
                responseMessage = "Вітаю! Я Стрибогів Подих. Надішли мені назву міста, щоб дізнатись погоду.";
            }
            else
            {
                // Отримуємо поточну погоду, як і раніше
                responseMessage = await GetWeatherAsync(messageText, WeatherType.Current);

                // Якщо погоду знайдено, створюємо кнопки
                if (!responseMessage.StartsWith("Місто") && !responseMessage.StartsWith("Помилка"))
                {
                    keyboard = new InlineKeyboardMarkup(new[]
                    {
                        // Створюємо кнопки з "callback data". Цей текст побачить тільки бот.
                        new []
                        {
                            InlineKeyboardButton.WithCallbackData(text: "Погода на завтра", callbackData: $"forecast_{messageText}"),
                            InlineKeyboardButton.WithCallbackData(text: "Оновити зараз", callbackData: $"current_{messageText}"),
                        },
                    });
                }
            }

            // Надсилаємо повідомлення разом з клавіатурою
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: responseMessage,
                replyMarkup: keyboard, // <--- Ось тут ми прикріплюємо кнопки
                cancellationToken: cancellationToken);
        }

        // === НОВИЙ МЕТОД для обробки натискання кнопок ===
        private static async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            string[] dataParts = callbackQuery.Data.Split('_'); // Розділяємо callback data, напр., "forecast_Kyiv"
            string action = dataParts[0];
            string city = dataParts[1];

            Console.WriteLine($"Отримано запит з кнопки: дія '{action}', місто '{city}'");

            string responseMessage = "Невідома команда.";

            if (action == "forecast")
            {
                responseMessage = await GetWeatherAsync(city, WeatherType.Forecast);
            }
            else if (action == "current")
            {
                responseMessage = await GetWeatherAsync(city, WeatherType.Current);
            }

            // Надсилаємо нове повідомлення у відповідь на натискання кнопки
            await botClient.SendTextMessageAsync(
                chatId: callbackQuery.Message.Chat.Id,
                text: responseMessage,
                cancellationToken: cancellationToken);

            // (Опціонально) Повідомляємо Telegram, що ми обробили запит, щоб зник годинник на кнопці
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Помилка Telegram API:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };
            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }

        // === ОНОВЛЕНИЙ МЕТОД для отримання погоди ===
        // Додамо enum для вибору, яку погоду ми хочемо: поточну чи прогноз
        private enum WeatherType { Current, Forecast }

        private static async Task<string> GetWeatherAsync(string cityName, WeatherType type)
        {
            if (string.IsNullOrWhiteSpace(cityName)) return "Будь ласка, вкажіть назву міста.";

            string url = "";
            if (type == WeatherType.Current)
            {
                url = $"https://api.openweathermap.org/data/2.5/weather?q={cityName}&units=metric&lang=ua&appid={OpenWeatherApiKey}";
            }
            else // WeatherType.Forecast
            {
                url = $"https://api.openweathermap.org/data/2.5/forecast?q={cityName}&units=metric&lang=ua&appid={OpenWeatherApiKey}";
            }

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                if (type == WeatherType.Current)
                {
                    var weatherData = JsonConvert.DeserializeObject<OpenWeatherResponse>(responseBody);
                    return $"Погода у місті {weatherData.CityName}, {weatherData.Sys.Country}:\n" +
                           $"🌡️ Температура: {weatherData.Main.Temperature:F1}°C (відчувається як {weatherData.Main.FeelsLike:F1}°C)\n" +
                           $"📝 Опис: {weatherData.Weather[0].DetailedDescription}\n" +
                           $"💧 Вологість: {weatherData.Main.Humidity}%\n" +
                           $"💨 Швидкість вітру: {weatherData.Wind.Speed:F1} м/с";
                }
                else // WeatherType.Forecast
                {
                    var forecastData = JsonConvert.DeserializeObject<ForecastResponse>(responseBody);
                    // Шукаємо прогноз на завтрашній полудень (12:00)
                    var tomorrowForecast = forecastData.Forecasts
                        .FirstOrDefault(f => DateTime.Parse(f.ForecastTimeText).Date == DateTime.Now.Date.AddDays(1) && DateTime.Parse(f.ForecastTimeText).Hour == 12);

                    if (tomorrowForecast == null)
                    {
                        return "Не вдалося знайти прогноз на завтрашній полудень.";
                    }

                    return $"Прогноз на завтра (12:00) для міста {cityName}:\n" +
                           $"🌡️ Температура: {tomorrowForecast.Main.Temperature:F1}°C (відчувається як {tomorrowForecast.Main.FeelsLike:F1}°C)\n" +
                           $"📝 Опис: {tomorrowForecast.Weather[0].DetailedDescription}\n" +
                           $"💧 Вологість: {tomorrowForecast.Main.Humidity}%\n" +
                           $"💨 Швидкість вітру: {tomorrowForecast.Wind.Speed:F1} м/с";
                }
            }
            catch (HttpRequestException e)
            {
                if (e.Message.Contains("404")) return $"Місто '{cityName}' не знайдено.";
                return $"Помилка при запиті погоди: {e.Message}";
            }
            catch (Exception e)
            {
                return $"Виникла неочікувана помилка: {e.Message}";
            }
        }
    }
}
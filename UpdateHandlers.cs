using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Stribog
{
    public class UpdateHandlers
    {
        private readonly ITelegramBotClient _botClient;
        private readonly WeatherService _weatherService;
        private readonly UserSettingsService _userSettingsService;

        // Словник для відстеження стану розмови з кожним користувачем
        private static readonly Dictionary<long, string> UserStates = new Dictionary<long, string>();

        public UpdateHandlers(ITelegramBotClient botClient, WeatherService weatherService, UserSettingsService userSettingsService)
        {
            _botClient = botClient;
            _weatherService = weatherService;
            _userSettingsService = userSettingsService;
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var handler = update.Type switch
            {
                UpdateType.Message => HandleMessageAsync(botClient, update.Message, cancellationToken),
                UpdateType.CallbackQuery => HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken),
                _ => Task.CompletedTask
            };

            await handler;
        }

        private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;

            // Перевіряємо, чи користувач знаходиться в процесі налаштування
            if (UserStates.TryGetValue(chatId, out var state))
            {
                await HandleStatefulMessageAsync(botClient, message, state, cancellationToken);
                return;
            }

            // Стандартна обробка
            if (message.Location != null)
            {
                await ProcessLocationMessage(botClient, message, cancellationToken);
            }
            else if (message.Text is { } messageText)
            {
                await ProcessTextMessage(botClient, message, cancellationToken);
            }
        }
        
        private async Task HandleStatefulMessageAsync(ITelegramBotClient botClient, Message message, string state, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var text = message.Text;

            if (state == "awaiting_city")
            {
                if (await _weatherService.CityExistsAsync(text))
                {
                    await _userSettingsService.SetDefaultCityAsync(chatId, text);
                    UserStates[chatId] = "awaiting_time";
                    await botClient.SendTextMessageAsync(chatId, $"Добре, місто збережено як *{text}*.\n\nТепер надішліть час, о котрій ви хочете отримувати щоденний прогноз. Наприклад, `08:00` або `7:30`.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, $"Не можу знайти місто '{text}'. Спробуйте ще раз.", cancellationToken: cancellationToken);
                }
            }
            else if (state == "awaiting_time")
            {
                if (TimeSpan.TryParse(text, out var time))
                {
                    await _userSettingsService.SetUserNotificationTimeAsync(chatId, time);
                    UserStates.Remove(chatId); // Завершуємо розмову
                    await botClient.SendTextMessageAsync(chatId, $"Чудово! Щоденний прогноз буде надходити о *{time:hh\\:mm}*.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Неправильний формат часу. Будь ласка, надішліть час у форматі `ГГ:ХХ`, наприклад `08:30`.", cancellationToken: cancellationToken);
                }
            }
        }
        
        private async Task ProcessTextMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var messageText = message.Text;
            var command = messageText.Split(' ').First().ToLower();

            var commandAction = command switch
            {
                "/start" => HandleStartAsync(botClient, message, cancellationToken),
                "/forecast" => HandleForecastCommandAsync(botClient, message, cancellationToken),
                "/setdefault" => HandleSetDefaultCommandAsync(botClient, message, cancellationToken),
                _ => SendWeatherMenuAsync(botClient, chatId, messageText, cancellationToken)
            };
            await commandAction;
        }

        private async Task ProcessLocationMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var city = await _weatherService.GetCityNameByCoordsAsync(message.Location.Latitude, message.Location.Longitude);
            if (city != null)
            {
                await SendWeatherMenuAsync(botClient, message.Chat.Id, city, cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Не вдалося визначити місто.", cancellationToken: cancellationToken);
            }
        }
        
        private async Task SendWeatherMenuAsync(ITelegramBotClient botClient, long chatId, string city, CancellationToken cancellationToken)
        {
             if (!await _weatherService.CityExistsAsync(city))
             {
                 await botClient.SendTextMessageAsync(chatId, $"Не можу знайти місто '{city}'. Перевірте назву.", cancellationToken: cancellationToken);
                 return;
             }
            
             var inlineKeyboard = new InlineKeyboardMarkup(new[]
             {
                 new []
                 {
                     InlineKeyboardButton.WithCallbackData("🌡️ Поточна погода", $"current_{city}"),
                     InlineKeyboardButton.WithCallbackData("🗓️ Прогноз на 5 днів", $"forecast_{city}"),
                 }
             });

             await botClient.SendTextMessageAsync(chatId, $"Ви обрали: *{city}*.\nЩо саме вас цікавить?", 
                 parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var messageId = callbackQuery.Message.MessageId;
            var dataParts = callbackQuery.Data.Split(new[] { '_' }, 2);
            var action = dataParts[0];
            var city = dataParts[1];
            
            string resultText = "Помилка отримання даних...";

            if (action == "current")
            {
                resultText = await _weatherService.GetCurrentWeatherAsync(city);
            }
            else if (action == "forecast")
            {
                resultText = await _weatherService.GetForecastAsync(city);
            }

            await botClient.EditMessageTextAsync(chatId, messageId, resultText, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        // --- Обробники команд ---
        private async Task HandleStartAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var keyboard = new ReplyKeyboardMarkup(new[] { new[] { KeyboardButton.WithRequestLocation("📍 Надіслати моє місцезнаходження") } }) { ResizeKeyboard = true };
            await botClient.SendTextMessageAsync(message.Chat.Id, "Вітаю! Надішліть мені назву міста або геолокацію.", replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        private async Task HandleForecastCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var userSettings = await _userSettingsService.GetUserSettingsAsync(message.Chat.Id);
            if (userSettings?.City == null)
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Спочатку встановіть місто за замовчуванням командою /setdefault.", cancellationToken: cancellationToken);
                return;
            }
            var forecastReport = await _weatherService.GetForecastAsync(userSettings.City);
            await botClient.SendTextMessageAsync(message.Chat.Id, forecastReport, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
        }

        private async Task HandleSetDefaultCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            UserStates[message.Chat.Id] = "awaiting_city"; // Починаємо розмову
            await botClient.SendTextMessageAsync(message.Chat.Id, "Добре. Надішліть назву міста, яке хочете зберегти для щоденних сповіщень.", cancellationToken: cancellationToken);
        }
    }
}
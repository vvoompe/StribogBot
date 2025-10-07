using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
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
        private readonly string _adminId;
        private static readonly Dictionary<long, string> UserStates = new Dictionary<long, string>();

        public UpdateHandlers(ITelegramBotClient botClient, WeatherService weatherService, UserSettingsService userSettingsService, string adminId)
        {
            _botClient = botClient;
            _weatherService = weatherService;
            _userSettingsService = userSettingsService;
            _adminId = adminId;
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                var handler = update.Type switch
                {
                    UpdateType.Message => HandleMessageAsync(botClient, update.Message, cancellationToken),
                    UpdateType.CallbackQuery => HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken),
                    _ => Task.CompletedTask
                };
                await handler;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка обробки оновлення: {ex}");
            }
        }

        private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            if (UserStates.TryGetValue(chatId, out var state))
            {
                await HandleStatefulMessageAsync(botClient, message, state, cancellationToken);
                return;
            }

            if (message.Location != null) await ProcessLocationMessage(botClient, message, cancellationToken);
            else if (message.Text is { } messageText) await ProcessTextMessage(botClient, message, cancellationToken);
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
                    await botClient.SendTextMessageAsync(chatId, $"Добре, місто збережено як *{text}*.\n\nТепер надішліть час для щоденного прогнозу (наприклад, `08:00` або `7:30`).", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else await botClient.SendTextMessageAsync(chatId, $"Не можу знайти місто '{text}'. Спробуйте ще раз.", cancellationToken: cancellationToken);
            }
            else if (state == "awaiting_time")
            {
                if (TimeSpan.TryParse(text, out var time))
                {
                    await _userSettingsService.SetUserNotificationTimeAsync(chatId, time);
                    UserStates.Remove(chatId);
                    await botClient.SendTextMessageAsync(chatId, $"Чудово! Щоденний прогноз буде надходити о *{time:hh\\:mm}*.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else await botClient.SendTextMessageAsync(chatId, "Неправильний формат часу. Надішліть у форматі `ГГ:ХХ`, наприклад `08:30`.", cancellationToken: cancellationToken);
            }
        }

        private async Task ProcessTextMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var command = message.Text.Split(' ').First().ToLower();
            var commandAction = command switch
            {
                "/start" => HandleStartAsync(botClient, message, cancellationToken),
                "/forecast" => HandleForecastCommandAsync(botClient, message, cancellationToken),
                "/setdefault" => HandleSetDefaultCommandAsync(botClient, message, cancellationToken),
                "/broadcast" => HandleBroadcastCommandAsync(botClient, message, cancellationToken),
                _ => ShowCurrentWeatherWithMenu(botClient, message.Chat.Id, message.Text, cancellationToken)
            };
            await commandAction;
        }

        private async Task ProcessLocationMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var city = await _weatherService.GetCityNameByCoordsAsync(message.Location.Latitude, message.Location.Longitude);
            if (city != null) await ShowCurrentWeatherWithMenu(botClient, message.Chat.Id, city, cancellationToken);
            else await botClient.SendTextMessageAsync(message.Chat.Id, "Не вдалося визначити місто.", cancellationToken: cancellationToken);
        }

        private async Task ShowCurrentWeatherWithMenu(ITelegramBotClient botClient, long chatId, string city, CancellationToken cancellationToken)
        {
            string report = await _weatherService.GetCurrentWeatherAsync(city);
            if (report.StartsWith("Не вдалося") || report.StartsWith("Помилка"))
            {
                await botClient.SendTextMessageAsync(chatId, report, cancellationToken: cancellationToken);
                return;
            }

            var inlineKeyboard = new InlineKeyboardMarkup(new[] {
                new[] {
                    InlineKeyboardButton.WithCallbackData("Погода до вечора", $"evening_{city}"),
                    InlineKeyboardButton.WithCallbackData("Прогноз на 5 днів", $"forecast_{city}"),
                }
            });
            await botClient.SendTextMessageAsync(chatId, report, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var dataParts = callbackQuery.Data.Split(new[] { '_' }, 2);
            string resultText;

            if (dataParts[0] == "evening") resultText = await _weatherService.GetEveningForecastAsync(dataParts[1]);
            else if (dataParts[0] == "forecast") resultText = await _weatherService.GetForecastAsync(dataParts[1]);
            else return;

            await botClient.SendTextMessageAsync(chatId, resultText, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        private async Task HandleStartAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var keyboard = new ReplyKeyboardMarkup(new[] { KeyboardButton.WithRequestLocation("📍 Надіслати моє місцезнаходження") }) { ResizeKeyboard = true };
            await botClient.SendTextMessageAsync(message.Chat.Id, "Вітаю! Надішліть мені назву міста або геолокацію.", replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        private async Task HandleForecastCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var settings = await _userSettingsService.GetUserSettingsAsync(message.Chat.Id);
            if (settings?.City == null)
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Спочатку встановіть місто за замовчуванням командою /setdefault.", cancellationToken: cancellationToken);
                return;
            }
            var report = await _weatherService.GetForecastAsync(settings.City);
            await botClient.SendTextMessageAsync(message.Chat.Id, report, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
        }

        private async Task HandleSetDefaultCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            UserStates[message.Chat.Id] = "awaiting_city";
            await botClient.SendTextMessageAsync(message.Chat.Id, "Добре. Надішліть назву міста для щоденних сповіщень.", cancellationToken: cancellationToken);
        }

        private async Task HandleBroadcastCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_adminId) || message.Chat.Id.ToString() != _adminId) return;
            var parts = message.Text.Split(new[] { ' ' }, 2);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Введіть текст для розсилки після команди.", cancellationToken: cancellationToken);
                return;
            }
            int count = await _userSettingsService.BroadcastMessageAsync(botClient, parts[1]);
            await botClient.SendTextMessageAsync(message.Chat.Id, $"✅ Розсилку надіслано {count} користувачам.", cancellationToken: cancellationToken);
        }
    }
}
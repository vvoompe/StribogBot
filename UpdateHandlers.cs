using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TimeZoneConverter;

namespace Stribog
{
    public static class UpdateHandlers
    {
        private static readonly UserSettingsService _userSettingsService = new UserSettingsService();
        private static readonly WeatherService _weatherService = new WeatherService();
        private static readonly Dictionary<long, UserState> UserStates = new Dictionary<long, UserState>();

        private enum UserState { None, AwaitingCity, AwaitingBroadcastCity, AwaitingBroadcastTime, AwaitingTimeZone }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.CallbackQuery != null)
            {
                await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
                return;
            }

            if (update.Message?.Text == null) return;
            var message = update.Message;
            var chatId = message.Chat.Id;

            if (UserStates.TryGetValue(chatId, out var userState) && userState != UserState.None)
            {
                await HandleStatefulInput(botClient, message, userState, cancellationToken);
                return;
            }

            var action = message.Text switch
            {
                "/start" => HandleStartCommand(botClient, message, cancellationToken),
                "/help" => HandleHelpCommand(botClient, message, cancellationToken),
                "⛅️ Погода" => ShowWeatherMenu(botClient, message.Chat.Id, cancellationToken),
                "⚙️ Налаштування" => ShowSettingsMenu(botClient, message.Chat.Id, cancellationToken),
                _ => HandleUnknownCommand(botClient, message, cancellationToken)
            };
            await action;
        }

        private static async Task HandleStatefulInput(ITelegramBotClient botClient, Message message, UserState userState, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            UserStates.Remove(chatId);

            switch (userState)
            {
                case UserState.AwaitingCity:
                    await HandleCityInput(botClient, message, "main", cancellationToken);
                    break;
                case UserState.AwaitingBroadcastCity:
                    await HandleCityInput(botClient, message, "broadcast", cancellationToken);
                    break;
                case UserState.AwaitingBroadcastTime:
                    await HandleBroadcastTimeInput(botClient, message, cancellationToken);
                    break;
                case UserState.AwaitingTimeZone:
                    await HandleTimeZoneInput(botClient, message, cancellationToken);
                    break;
            }
        }
        
        #region Handlers
        private static Task HandleStartCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            return botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "👋 Ласкаво просимо! Я ваш персональний погодний асистент.\n\n" +
                      "Використовуйте кнопки нижче, щоб дізнатись погоду або налаштувати щоденну розсилку.",
                replyMarkup: GetMainMenu(),
                cancellationToken: cancellationToken);
        }

        private static Task HandleHelpCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            return botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "❓ *Допомога*\n\n" +
                      "⛅️ *Погода* - отримати поточний прогноз погоди.\n" +
                      "⚙️ *Налаштування* - встановити ваше місто за замовчуванням та налаштувати щоденні сповіщення.",
                parseMode: ParseMode.Markdown,
                replyMarkup: GetMainMenu(),
                cancellationToken: cancellationToken);
        }

        private static async Task HandleCityInput(ITelegramBotClient botClient, Message message, string context, CancellationToken cancellationToken)
        {
            var city = message.Text;
            var chatId = message.Chat.Id;
            try
            {
                // ВИПРАВЛЕНО: Використання правильної назви параметра chatAction
                await botClient.SendChatActionAsync(chatId: chatId, chatAction: ChatAction.Typing, cancellationToken: cancellationToken);
                
                await _weatherService.GetWeatherAsync(city); 
                
                var userSettings = _userSettingsService.GetUserSettings(chatId);
                
                if (context == "broadcast")
                {
                    userSettings.BroadcastCity = city;
                    await botClient.SendTextMessageAsync(chatId: chatId, text: $"✅ Місто для розсилки оновлено на *{city}*.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else
                {
                    userSettings.City = city;
                    await botClient.SendTextMessageAsync(chatId: chatId, text: $"✅ Ваше основне місто встановлено: *{city}*.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                
                _userSettingsService.SaveUserSettings(userSettings);
                await ShowSettingsMenu(botClient, chatId, cancellationToken);
            }
            catch
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: $"❌ Місто '{city}' не знайдено. Спробуйте ще раз.", cancellationToken: cancellationToken);
            }
        }
        
        private static async Task HandleBroadcastTimeInput(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            if (TimeSpan.TryParse(message.Text, out _))
            {
                var settings = _userSettingsService.GetUserSettings(message.Chat.Id);
                settings.BroadcastTime = message.Text;
                _userSettingsService.SaveUserSettings(settings);
                await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"✅ Час розсилки встановлено на *{message.Text}*.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                await ShowBroadcastMenu(botClient, message.Chat.Id, null, cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "❌ Неправильний формат часу. Спробуйте ще раз (наприклад, 08:00).", cancellationToken: cancellationToken);
            }
        }
        
        private static async Task HandleTimeZoneInput(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var tzInput = message.Text;
            try
            {
                if (TZConvert.TryGetTimeZoneInfo(tzInput, out _))
                {
                    var settings = _userSettingsService.GetUserSettings(message.Chat.Id);
                    settings.TimeZoneId = tzInput;
                    _userSettingsService.SaveUserSettings(settings);
                    await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"✅ Ваш часовий пояс встановлено: *{tzInput}*.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    await ShowBroadcastMenu(botClient, message.Chat.Id, null, cancellationToken);
                }
                else
                {
                     await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"❌ Часовий пояс '{tzInput}' не знайдено. Спробуйте ще раз (наприклад, *Europe/Kyiv*).", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
            }
            catch
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"❌ Помилка при встановленні часового поясу. Спробуйте ще раз (наприклад, *Europe/Kyiv*).", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            }
        }

        private static Task HandleUnknownCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            return botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "🤔 Незрозуміла команда. Будь ласка, скористайтеся кнопками меню.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
        }

        public static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[POLLING ERROR] {exception.Message}");
            return Task.CompletedTask;
        }
        #endregion

        #region Menus
        private static async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, int? messageId, CancellationToken cancellationToken)
        {
            var text = "👋 Головне меню. Чим можу допомогти?";
            if (messageId.HasValue)
            {
                await botClient.EditMessageTextAsync(chatId: chatId, messageId: messageId.Value, text: text, replyMarkup: GetMainMenuInline(), cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: text, replyMarkup: GetMainMenuInline(), cancellationToken: cancellationToken);
            }
        }
        
        private static async Task ShowWeatherMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken, int? messageId = null)
        {
            var userSettings = _userSettingsService.GetUserSettings(chatId);
            if (string.IsNullOrEmpty(userSettings.City))
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: "Будь ласка, спершу вкажіть ваше місто у налаштуваннях.", cancellationToken: cancellationToken);
                await ShowSettingsMenu(botClient, chatId, cancellationToken, messageId);
                return;
            }

            var text = $"🌤️ *Погода у місті {userSettings.City}*\n\nОберіть тип прогнозу:";
            var markup = GetWeatherInlineMenu(userSettings.City);

            if (messageId.HasValue)
            {
                await botClient.EditMessageTextAsync(chatId: chatId, messageId: messageId.Value, text: text, parseMode: ParseMode.Markdown, replyMarkup: markup, cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: text, parseMode: ParseMode.Markdown, replyMarkup: markup, cancellationToken: cancellationToken);
            }
        }
        
        private static async Task ShowSettingsMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken, int? messageId = null)
        {
            var settings = _userSettingsService.GetUserSettings(chatId);
            var text = "⚙️ *Налаштування*\n\n" +
                       $"📍 Ваше місто: *{settings.City ?? "не вказано"}*\n" +
                       $"🔔 Щоденна розсилка: *{(settings.DailyWeatherBroadcast ? "Увімкнено" : "Вимкнено")}*";

            if (messageId.HasValue)
            {
                await botClient.EditMessageTextAsync(chatId: chatId, messageId: messageId.Value, text: text, parseMode: ParseMode.Markdown, replyMarkup: GetSettingsInlineMenu(), cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: text, parseMode: ParseMode.Markdown, replyMarkup: GetSettingsInlineMenu(), cancellationToken: cancellationToken);
            }
        }
        
        private static async Task ShowBroadcastMenu(ITelegramBotClient botClient, long chatId, int? messageId, CancellationToken cancellationToken)
        {
            var settings = _userSettingsService.GetUserSettings(chatId);
            string status = settings.DailyWeatherBroadcast ? "✅ Увімкнено" : "❌ Вимкнено";
            var text = $"🔔 *Налаштування щоденної розсилки*\n\n" +
                       $"Статус: *{status}*\n" +
                       $"Місто: *{settings.BroadcastCity ?? settings.City ?? "не вказано"}*\n" +
                       $"Час: *{settings.BroadcastTime ?? "не вказано"}*\n" +
                       $"Часовий пояс: *{settings.TimeZoneId ?? "не вказано"}*";

            if (messageId.HasValue)
            {
                await botClient.EditMessageTextAsync(chatId: chatId, messageId: messageId.Value, text: text, parseMode: ParseMode.Markdown, replyMarkup: GetBroadcastInlineMenu(settings.DailyWeatherBroadcast), cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: text, parseMode: ParseMode.Markdown, replyMarkup: GetBroadcastInlineMenu(settings.DailyWeatherBroadcast), cancellationToken: cancellationToken);
            }
        }
        
        private static async Task GetWeatherReport(ITelegramBotClient botClient, long chatId, string city, string type, int? messageId, CancellationToken cancellationToken)
        {
            try
            {
                // ВИПРАВЛЕНО: Використання правильної назви параметра chatAction
                await botClient.SendChatActionAsync(chatId: chatId, chatAction: ChatAction.Typing, cancellationToken: cancellationToken);
                string weatherReport = type == "today"
                    ? await _weatherService.GetTodayForecastAsync(city)
                    : await _weatherService.GetWeatherAsync(city);

                var markup = new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔄 Оновити", $"weather_refresh_{type}_{city}"),
                    InlineKeyboardButton.WithCallbackData("⬅️ Назад", "weather_menu")
                });
                
                if(messageId.HasValue)
                {
                    await botClient.EditMessageTextAsync(chatId: chatId, messageId: messageId.Value, text: weatherReport, parseMode: ParseMode.Markdown, replyMarkup: markup, cancellationToken: cancellationToken);
                }
                else
                {
                     await botClient.SendTextMessageAsync(chatId: chatId, text: weatherReport, parseMode: ParseMode.Markdown, replyMarkup: markup, cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: $"Помилка отримання погоди: {ex.Message}", cancellationToken: cancellationToken);
            }
        }
        #endregion

        #region Callbacks
        private static async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var messageId = callbackQuery.Message.MessageId;

            var data = callbackQuery.Data;
            
            if (data == "main_menu")
                await ShowMainMenu(botClient, chatId, messageId, cancellationToken);
            else if (data == "weather_menu")
                await ShowWeatherMenu(botClient, chatId, cancellationToken, messageId);
            else if (data.StartsWith("weather_get_"))
            {
                var parts = data.Split('_');
                await GetWeatherReport(botClient, chatId, parts[3], parts[2], messageId, cancellationToken);
            }
            else if (data.StartsWith("weather_refresh_"))
            {
                var parts = data.Split('_');
                await GetWeatherReport(botClient, chatId, parts[3], parts[2], messageId, cancellationToken);
                await botClient.AnswerCallbackQueryAsync(callbackQueryId: callbackQuery.Id, text: "Дані оновлено", cancellationToken: cancellationToken);
            }
            else if (data == "settings_menu")
                await ShowSettingsMenu(botClient, chatId, cancellationToken, messageId);
            else if (data == "set_city")
            {
                UserStates[chatId] = UserState.AwaitingCity;
                await botClient.SendTextMessageAsync(chatId: chatId, text: "Введіть назву вашого основного міста:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
                await botClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
            }
            else if (data == "broadcast_settings")
                await ShowBroadcastMenu(botClient, chatId, messageId, cancellationToken);
            else if (data == "broadcast_toggle")
            {
                var s = _userSettingsService.GetUserSettings(chatId);
                s.DailyWeatherBroadcast = !s.DailyWeatherBroadcast;
                _userSettingsService.SaveUserSettings(s);
                await botClient.AnswerCallbackQueryAsync(callbackQueryId: callbackQuery.Id, text: $"Розсилку {(s.DailyWeatherBroadcast ? "увімкнено" : "вимкнено")}", cancellationToken: cancellationToken);
                await ShowBroadcastMenu(botClient, chatId, messageId, cancellationToken);
            }
            else if (data == "broadcast_set_city")
            {
                UserStates[chatId] = UserState.AwaitingBroadcastCity;
                await botClient.SendTextMessageAsync(chatId: chatId, text: "Введіть місто для щоденної розсилки:", cancellationToken: cancellationToken);
                await botClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
            }
            else if (data == "broadcast_set_time")
            {
                UserStates[chatId] = UserState.AwaitingBroadcastTime;
                await botClient.SendTextMessageAsync(chatId: chatId, text: "Введіть час для розсилки у форматі 24h (наприклад, 08:00):", cancellationToken: cancellationToken);
                await botClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
            }
            else if (data == "broadcast_set_timezone")
            {
                UserStates[chatId] = UserState.AwaitingTimeZone;
                await botClient.SendTextMessageAsync(chatId: chatId, text: "Введіть ваш часовий пояс (наприклад, Europe/Kyiv):", cancellationToken: cancellationToken);
                await botClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
            }
        }
        #endregion

        #region Keyboards
        private static ReplyKeyboardMarkup GetMainMenu() => new(new[]
        {
            new KeyboardButton[] { "⛅️ Погода", "⚙️ Налаштування" }
        }) { ResizeKeyboard = true };

        private static InlineKeyboardMarkup GetMainMenuInline() => new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("⛅️ Дізнатись погоду", "weather_menu") },
            new[] { InlineKeyboardButton.WithCallbackData("⚙️ Налаштування", "settings_menu") },
        });

        private static InlineKeyboardMarkup GetWeatherInlineMenu(string city) => new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Сьогодні", $"weather_get_today_{city}"),
                InlineKeyboardButton.WithCallbackData("Зараз (детально)", $"weather_get_current_{city}"),
            },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад до головного меню", "main_menu") }
        });

        private static InlineKeyboardMarkup GetSettingsInlineMenu() => new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📍 Вказати моє місто", "set_city") },
            new[] { InlineKeyboardButton.WithCallbackData("🔔 Налаштувати розсилку", "broadcast_settings") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад до головного меню", "main_menu") }
        });

        private static InlineKeyboardMarkup GetBroadcastInlineMenu(bool isEnabled) => new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(isEnabled ? "❌ Вимкнути розсилку" : "✅ Увімкнути розсилку", "broadcast_toggle") },
            new[] { InlineKeyboardButton.WithCallbackData("🏙️ Змінити місто розсилки", "broadcast_set_city") },
            new[] { InlineKeyboardButton.WithCallbackData("⏰ Змінити час розсилки", "broadcast_set_time") },
            new[] { InlineKeyboardButton.WithCallbackData("🌍 Змінити часовий пояс", "broadcast_set_timezone") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад до налаштувань", "settings_menu") }
        });
        #endregion
    }
}
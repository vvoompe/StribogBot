using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Stribog;

public static class UpdateHandlers
{
    private static readonly UserSettingsService _userSettingsService = new UserSettingsService();
    private static readonly WeatherService _weatherService = new WeatherService();
    private static readonly Dictionary<long, UserState> UserStates = new Dictionary<long, UserState>();

    private enum UserState { None, AwaitingCity, AwaitingBroadcastCity, AwaitingBroadcastTime }

    public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // CallbackQuery обробляємо першочергово
        if (update.CallbackQuery != null)
        {
            await HandleBroadcastInlineCallback(botClient, update.CallbackQuery, cancellationToken);
            return;
        }

        if (update.Message?.Text == null) return;

        var message = update.Message;
        var chatId = message.Chat.Id;

        if (UserStates.TryGetValue(chatId, out var userState))
        {
            if (userState == UserState.AwaitingCity)
            {
                await HandleCityInput(botClient, message, cancellationToken);
                return;
            }
            if (userState == UserState.AwaitingBroadcastCity)
            {
                await HandleBroadcastCityInput(botClient, message, cancellationToken);
                return;
            }
            if (userState == UserState.AwaitingBroadcastTime)
            {
                await HandleBroadcastTimeInput(botClient, message, cancellationToken);
                return;
            }
        }

        // Без використання оператора or (для сумісності з більш ранніми версіями C#)
        var action = message.Text switch
        {
            "/start" => HandleStartCommand(botClient, message, cancellationToken),
            "/help" => HandleStartCommand(botClient, message, cancellationToken),
            "⛅️ Дізнатись погоду" => HandleWeatherCommand(botClient, message, cancellationToken),
            "⚙️ Вказати регіон/місто" => HandleSetCityCommand(botClient, message, cancellationToken),
            "📯 Розсилки" => HandleBroadcastCommand(botClient, message, cancellationToken),
            "/setdefault" => HandleSetDefaultCommand(botClient, message, cancellationToken),
            _ => HandleUnknownCommand(botClient, message, cancellationToken)
        };
        await action;
    }

    // --- Основні командні хендлери ---

    private static Task HandleStartCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        return botClient.SendTextMessageAsync(
            chatId: message.Chat.Id,
            text: "Ласкаво просимо! Цей бот допоможе вам дізнатись погоду.\n\n" +
                  "1. Натисніть *'⚙️ Вказати регіон/місто'*.\n" +
                  "2. Натисніть *'⛅️ Дізнатись погоду'*.",
            parseMode: ParseMode.Markdown,
            replyMarkup: GetMainMenu(),
            cancellationToken: cancellationToken);
    }

    private static async Task HandleWeatherCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var userSettings = _userSettingsService.GetUserSettings(message.Chat.Id);
        var city = string.IsNullOrEmpty(userSettings.City) ? null : userSettings.City;

        if (string.IsNullOrEmpty(city))
        {
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Спочатку вкажіть регіон.", cancellationToken: cancellationToken);
            return;
        }

        // Інлайн меню з вибором прогнозу
        await botClient.SendTextMessageAsync(
            chatId: message.Chat.Id,
            text: "Оберіть прогноз:",
            replyMarkup: GetWeatherInlineMenu(),
            cancellationToken: cancellationToken);
    }

    private static Task HandleSetCityCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        UserStates[message.Chat.Id] = UserState.AwaitingCity;
        return botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Введіть назву регіону/міста:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
    }

    private static async Task HandleCityInput(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var city = message.Text;
        try
        {
            await botClient.SendChatActionAsync(chatId: message.Chat.Id, chatAction: ChatAction.Typing, cancellationToken: cancellationToken);
            // перевірити місто й отримати коротку перевірку погоди
            await _weatherService.GetWeatherAsync(city);
            var userSettings = _userSettingsService.GetUserSettings(message.Chat.Id);
            userSettings.City = city;
            _userSettingsService.SaveUserSettings(userSettings);

            // Відправити розбитий великий вивід погоди
            string fullWeather = await _weatherService.GetWeatherAsync(city);
            foreach (var chunk in SplitMessage(fullWeather, 4096))
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: chunk, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            }

            UserStates.Remove(message.Chat.Id);
        }
        catch
        {
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"Регіон/місто '{city}' не знайдено. Спробуйте ще раз.", cancellationToken: cancellationToken);
        }
    }

    private static async Task HandleBroadcastCityInput(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var city = message.Text;
        var settings = _userSettingsService.GetUserSettings(message.Chat.Id);
        settings.BroadcastCity = city;
        // після введення міста - очікуємо час розсилки
        settings.DailyWeatherBroadcast = true;
        _userSettingsService.SaveUserSettings(settings);

        UserStates[message.Chat.Id] = UserState.AwaitingBroadcastTime;
        await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Введіть час розсилки у форматі HH:mm (наприклад 09:00):", cancellationToken: cancellationToken);
    }

    private static async Task HandleBroadcastTimeInput(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var timeText = message.Text;
        if (TimeSpan.TryParse(timeText, out var ts))
        {
            var settings = _userSettingsService.GetUserSettings(message.Chat.Id);
            settings.BroadcastTime = timeText;
            _userSettingsService.SaveUserSettings(settings);
            UserStates.Remove(message.Chat.Id);
            // Без екранування лапок — використати окрему змінну для надійності
            var cityDisplay = settings.BroadcastCity ?? settings.City ?? "не вказано";
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"Розсилка встановлена. Місто: {cityDisplay}, час: {timeText}",
                cancellationToken: cancellationToken);
        }
        else
        {
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Неправильний формат часу. Спробуйте ще раз (HH:mm).", cancellationToken: cancellationToken);
        }
    }

    private static Task HandleUnknownCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        return botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Невідома команда.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
    }

    // Виправлена публічна помилка polling
    public static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[POLLING ERROR] {exception.Message}");
        return Task.CompletedTask;
    }

    // --- Інлайн-меню та callback для розсилок та прогнозів ---

    private static async Task HandleBroadcastInlineCallback(ITelegramBotClient botClient, CallbackQuery callback, CancellationToken cancellationToken)
    {
        var data = callback.Data;
        var chatId = callback.Message.Chat.Id;

        switch (data)
        {
            case "broadcast_enable":
                {
                    var s = _userSettingsService.GetUserSettings(chatId);
                    s.DailyWeatherBroadcast = true;
                    _userSettingsService.SaveUserSettings(s);
                    await botClient.AnswerCallbackQueryAsync(callback.Id, "Розсилку увімкнено");
                    await botClient.EditMessageTextAsync(chatId, callback.Message.MessageId, "Управління розсилкою", replyMarkup: GetBroadcastInlineMenu(), cancellationToken: cancellationToken);
                    break;
                }
            case "broadcast_disable":
                {
                    var s = _userSettingsService.GetUserSettings(chatId);
                    s.DailyWeatherBroadcast = false;
                    _userSettingsService.SaveUserSettings(s);
                    await botClient.AnswerCallbackQueryAsync(callback.Id, "Розсилку вимкнено");
                    await botClient.EditMessageTextAsync(chatId, callback.Message.MessageId, "Управління розсилкою", replyMarkup: GetBroadcastInlineMenu(), cancellationToken: cancellationToken);
                    break;
                }
            case "broadcast_change_city":
                {
                    UserStates[chatId] = UserState.AwaitingBroadcastCity;
                    await botClient.AnswerCallbackQueryAsync(callback.Id);
                    await botClient.SendTextMessageAsync(chatId, "Введіть регіон/місто, для якого буде відправлятися розсилка:", cancellationToken: cancellationToken);
                    break;
                }
            case "broadcast_change_time":
                {
                    UserStates[chatId] = UserState.AwaitingBroadcastTime;
                    await botClient.AnswerCallbackQueryAsync(callback.Id);
                    await botClient.SendTextMessageAsync(chatId, "Введіть час розсилки у форматі HH:mm (наприклад 09:00):", cancellationToken: cancellationToken);
                    break;
                }
            case "broadcast_show":
                {
                    var s = _userSettingsService.GetUserSettings(chatId);
                    var current = $"Розсилка: {(s.DailyWeatherBroadcast ? "Увімкнено" : "Вимкнено")}\n" +
                                  $"Регіон/місто: {s.BroadcastCity ?? s.City ?? "не вказано"}\n" +
                                  $"Час: {s.BroadcastTime ?? "не вказано"}\n" +
                                  $"TZ: {s.TimeZoneId ?? "не вказано"}";
                    await botClient.AnswerCallbackQueryAsync(callback.Id, "Поточні налаштування");
                    await botClient.SendTextMessageAsync(chatId, current, cancellationToken: cancellationToken);
                    break;
                }
            case "weather_today":
                {
                    var s = _userSettingsService.GetUserSettings(chatId);
                    var cityForWeather = string.IsNullOrEmpty(s.BroadcastCity) ? s.City : s.BroadcastCity;
                    if (string.IsNullOrEmpty(cityForWeather))
                    {
                        await botClient.AnswerCallbackQueryAsync(callback.Id, "Спочатку вкажіть регіон.");
                        break;
                    }
                    string todayForecast = await _weatherService.GetTodayForecastAsync(cityForWeather);
                    await botClient.AnswerCallbackQueryAsync(callback.Id);
                    foreach (var chunk in SplitMessage(todayForecast, 4096))
                    {
                        await botClient.SendTextMessageAsync(chatId, chunk, cancellationToken: cancellationToken, parseMode: ParseMode.Markdown);
                    }
                    break;
                }
            case "weather_5days":
                {
                    var s = _userSettingsService.GetUserSettings(chatId);
                    var cityForWeather = string.IsNullOrEmpty(s.BroadcastCity) ? s.City : s.BroadcastCity;
                    if (string.IsNullOrEmpty(cityForWeather))
                    {
                        await botClient.AnswerCallbackQueryAsync(callback.Id, "Спочатку вкажіть регіон.");
                        break;
                    }
                    string fiveDay = await _weatherService.GetWeatherAsync(cityForWeather);
                    await botClient.AnswerCallbackQueryAsync(callback.Id);
                    foreach (var chunk in SplitMessage(fiveDay, 4096))
                    {
                        await botClient.SendTextMessageAsync(chatId, chunk, cancellationToken: cancellationToken, parseMode: ParseMode.Markdown);
                    }
                    break;
                }
            default:
                await botClient.AnswerCallbackQueryAsync(callback.Id, "Невідомий запит");
                break;
        }
    }

    private static async Task HandleSetDefaultCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        // Відображаємо інлайнове меню керування розсилкою
        await botClient.SendTextMessageAsync(message.Chat.Id, "Керування розсилкою:", replyMarkup: GetBroadcastInlineMenu(), cancellationToken: cancellationToken);
    }

    private static async Task HandleBroadcastCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        // Відобразити інлайнове меню розсилки
        await botClient.SendTextMessageAsync(message.Chat.Id, "Керування розсилкою:", replyMarkup: GetBroadcastInlineMenu(), cancellationToken: cancellationToken);
    }

    private static async Task HandleCityInputInline(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    // Списки утиліт
    private static IEnumerable<string> SplitMessage(string text, int maxLength = 4096)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        for (int i = 0; i < text.Length; i += maxLength)
            yield return text.Substring(i, Math.Min(maxLength, text.Length - i));
    }

    private static ReplyKeyboardMarkup GetMainMenu() => new(new[]
    {
        new KeyboardButton[] { "⛅️ Дізнатись погоду", "⚙️ Вказати регіон" , "📯 Розсилки" }
    }) { ResizeKeyboard = true };

    // Інлайн-меню керування погодою
    private static InlineKeyboardMarkup GetWeatherInlineMenu() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("Сьогодні", "weather_today"),
            InlineKeyboardButton.WithCallbackData("На 5 днів", "weather_5days")
        }
    });

    // Інлайн-меню керування розсилками
    private static InlineKeyboardMarkup GetBroadcastInlineMenu() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("Увімкнути розсилку", "broadcast_enable"),
            InlineKeyboardButton.WithCallbackData("Вимкнути розсилку", "broadcast_disable")
        },
        new[]
        {
            InlineKeyboardButton.WithCallbackData("Змінити місто", "broadcast_change_city"),
            InlineKeyboardButton.WithCallbackData("Змінити час", "broadcast_change_time")
        },
        new[]
        {
            InlineKeyboardButton.WithCallbackData("Показати налаштування", "broadcast_show")
        }
    });

}

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

    private enum UserState { None, AwaitingCity }

    public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message?.Text == null) return;
        
        var message = update.Message;
        var chatId = message.Chat.Id;

        if (UserStates.TryGetValue(chatId, out var userState) && userState == UserState.AwaitingCity)
        {
            await HandleCityInput(botClient, message, cancellationToken);
            return;
        }

        var action = message.Text switch
        {
            "/start" or "/help" => HandleStartCommand(botClient, message, cancellationToken),
            "⛅️ Дізнатись погоду" => HandleWeatherCommand(botClient, message, cancellationToken),
            "⚙️ Вказати місто" => HandleSetCityCommand(botClient, message, cancellationToken),
            _ => HandleUnknownCommand(botClient, message, cancellationToken)
        };
        await action;
    }

    private static Task HandleStartCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        return botClient.SendTextMessageAsync(
            chatId: message.Chat.Id,
            text: "Ласкаво просимо! Цей бот допоможе вам дізнатись погоду.\n\n" +
                  "1. Натисніть *'⚙️ Вказати місто'*.\n" +
                  "2. Натисніть *'⛅️ Дізнатись погоду'*.",
            parseMode: ParseMode.Markdown,
            replyMarkup: GetMainMenu(),
            cancellationToken: cancellationToken);
    }
    
    private static async Task HandleWeatherCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var userSettings = _userSettingsService.GetUserSettings(message.Chat.Id);
        if (string.IsNullOrEmpty(userSettings.City))
        {
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Спочатку вкажіть місто.", cancellationToken: cancellationToken);
            return;
        }
        try
        {
            await botClient.SendChatActionAsync(chatId: message.Chat.Id, chatAction: ChatAction.Typing, cancellationToken: cancellationToken);
            var weatherInfo = await _weatherService.GetWeatherAsync(userSettings.City);
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: weatherInfo, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"Не вдалося знайти місто '{userSettings.City}'.", cancellationToken: cancellationToken);
        }
    }

    private static Task HandleSetCityCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        UserStates[message.Chat.Id] = UserState.AwaitingCity;
        return botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Введіть назву міста:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
    }

    private static async Task HandleCityInput(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var city = message.Text;
        try
        {
            await botClient.SendChatActionAsync(chatId: message.Chat.Id, chatAction: ChatAction.Typing, cancellationToken: cancellationToken);
            await _weatherService.GetWeatherAsync(city);
            var userSettings = _userSettingsService.GetUserSettings(message.Chat.Id);
            userSettings.City = city;
            _userSettingsService.SaveUserSettings(userSettings);
            
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"Місто '{city}' збережено.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
            UserStates.Remove(message.Chat.Id);
        }
        catch
        {
            await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: $"Місто '{city}' не знайдено. Спробуйте ще раз.", cancellationToken: cancellationToken);
        }
    }
    
    private static Task HandleUnknownCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        return botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Невідома команда.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
    }

    public static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[POLLING ERROR] {exception.Message}");
        return Task.CompletedTask;
    }

    private static ReplyKeyboardMarkup GetMainMenu() => new(new[]
    {
        new KeyboardButton[] { "⛅️ Дізнатись погоду", "⚙️ Вказати місто" }
    }) { ResizeKeyboard = true };
}
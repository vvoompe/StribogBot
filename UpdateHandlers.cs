using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Stribog
{
    public class UpdateHandlers : IUpdateHandler
    {
        private readonly WeatherService _weatherService;
        private readonly UserSettingsService _userSettingsService;
        private readonly string _adminId;

        private static readonly ConcurrentDictionary<long, UserSetting> TempUserSettings = new();
        private static readonly ConcurrentDictionary<long, string> UserStates = new();

        public UpdateHandlers(WeatherService weatherService, UserSettingsService userSettingsService, string adminId)
        {
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
                    UpdateType.Message => HandleMessageAsync(botClient, update.Message!, cancellationToken),
                    UpdateType.CallbackQuery => HandleCallbackQueryAsync(botClient, update.CallbackQuery!, cancellationToken),
                    _ => Task.CompletedTask
                };
                await handler;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка обробки оновлення: {ex}");
            }
        }

        public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);
            return Task.CompletedTask;
        }

        private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            if (message.Text is not { } messageText && message.Location is null)
                return;

            var chatId = message.Chat.Id;

            if (UserStates.TryGetValue(chatId, out var state))
            {
                await HandleStatefulMessageAsync(botClient, message, state, cancellationToken);
                return;
            }

            if (message.Location != null)
                await ProcessLocationMessage(botClient, message, cancellationToken);
            else if (message.Text is { } text)
                await ProcessTextMessage(botClient, message, cancellationToken);
        }

        private async Task HandleStatefulMessageAsync(ITelegramBotClient botClient, Message message, string state, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var text = message.Text?.Trim() ?? "";

            if (state == "awaiting_city")
            {
                var (success, offset) = await _weatherService.GetTimezoneOffsetAsync(text);
                if (success)
                {
                    TempUserSettings[chatId] = new UserSetting { City = text, UtcOffsetSeconds = offset };
                    UserStates[chatId] = "awaiting_time";

                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"Добре, місто *{_weatherService.SanitizeMarkdown(text)}* знайдено\n\nТепер надішліть час для щоденного прогнозу \\(наприклад, `08:00`\\)",
                        parseMode: ParseMode.MarkdownV2,
                        cancellationToken: cancellationToken
                    );
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, $"Не можу знайти місто '{text}'", cancellationToken: cancellationToken);
                }
            }
            else if (state == "awaiting_time")
            {
                if (TimeSpan.TryParse(text, out var time) && TempUserSettings.TryGetValue(chatId, out var userSetting))
                {
                    userSetting.NotificationTime = time;
                    await _userSettingsService.SetUserSettingAsync(chatId, userSetting);

                    UserStates.TryRemove(chatId, out _);
                    TempUserSettings.TryRemove(chatId, out _);

                    var citySanitized = _weatherService.SanitizeMarkdown(userSetting.City!);
                    var timeSanitized = _weatherService.SanitizeMarkdown($"{time:hh\\:mm}");
                    
                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"Чудово\\! Щоденний прогноз для міста *{citySanitized}* буде надходити о *{timeSanitized}* за його місцевим часом\\.",
                        parseMode: ParseMode.MarkdownV2,
                        cancellationToken: cancellationToken
                    );
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Неправильний формат часу. Надішліть у форматі `ГГ:ХХ`, наприклад `08:30`", cancellationToken: cancellationToken);
                }
            }
        }

        private async Task ProcessTextMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var command = message.Text!.Split(' ').First().ToLower();
            var commandAction = command switch
            {
                "/start" => HandleStartAsync(botClient, message, cancellationToken),
                "/forecast" => HandleForecastCommandAsync(botClient, message, cancellationToken),
                "/setdefault" => HandleSetDefaultCommandAsync(botClient, message, cancellationToken),
                "/broadcast" => HandleBroadcastCommandAsync(botClient, message, cancellationToken),
                _ => ShowCurrentWeatherWithMenu(botClient, message.Chat.Id, message.Text!, cancellationToken)
            };
            await commandAction;
        }
        
        private async Task ProcessLocationMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var city = await _weatherService.GetCityNameByCoordsAsync(message.Location!.Latitude, message.Location.Longitude);
            if (city != null)
                await ShowCurrentWeatherWithMenu(botClient, message.Chat.Id, city, cancellationToken);
            else
                await botClient.SendTextMessageAsync(message.Chat.Id, "Не вдалося визначити місто.", cancellationToken: cancellationToken);
        }

        private async Task ShowCurrentWeatherWithMenu(ITelegramBotClient botClient, long chatId, string city, CancellationToken cancellationToken)
        {
            string report = await _weatherService.GetCurrentWeatherAsync(city);
            var sanitizedReport = _weatherService.SanitizeMarkdown(report);

            if (report.StartsWith("Не вдалося"))
            {
                await botClient.SendTextMessageAsync(chatId, sanitizedReport, parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
                return;
            }

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Погода до вечора", $"evening_{city}"),
                    InlineKeyboardButton.WithCallbackData("Прогноз на 5 днів", $"forecast_{city}"),
                }
            });

            await botClient.SendTextMessageAsync(chatId, sanitizedReport, parseMode: ParseMode.MarkdownV2, replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message!.Chat.Id;
            var dataParts = callbackQuery.Data!.Split(new[] { '_' }, 2);
            string resultText;

            if (dataParts.Length < 2) return;

            var queryType = dataParts[0];
            var city = dataParts[1];

            resultText = queryType switch
            {
                "evening" => await _weatherService.GetEveningForecastAsync(city),
                "forecast" => await _weatherService.GetForecastAsync(city),
                _ => ""
            };

            if (!string.IsNullOrEmpty(resultText))
            {
                await botClient.SendTextMessageAsync(chatId, _weatherService.SanitizeMarkdown(resultText), parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
            }
            
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        private async Task HandleStartAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { KeyboardButton.WithRequestLocation("📍 Надіслати моє місцезнаходження") }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            var welcomeMessage = "Вітаю\\! Я Стрибог, бог вітру, і я тут, щоб надавати точні прогнози погоди\\.\n\n" +
                                 "Надішліть мені назву міста або вашу геолокацію, щоб дізнатися поточну погоду\\.\n\n" +
                                 "Використовуйте /setdefault, щоб налаштувати щоденні сповіщення\\.";

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: welcomeMessage,
                parseMode: ParseMode.MarkdownV2,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken
            );
        }

        private async Task HandleForecastCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var userSettings = await _userSettingsService.GetUserSettingsAsync(chatId);

            if (userSettings?.City != null)
            {
                await ShowCurrentWeatherWithMenu(botClient, chatId, userSettings.City, cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Спочатку встановіть місто за замовчуванням за допомогою команди /setdefault\\.", parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
            }
        }

        private async Task HandleSetDefaultCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            UserStates[chatId] = "awaiting_city";
            await botClient.SendTextMessageAsync(chatId, "Будь ласка, надішліть ваш регіон для щоденного прогнозу\\.", parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
        }
        
        private async Task HandleBroadcastCommandAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            if (chatId.ToString() != _adminId)
            {
                await botClient.SendTextMessageAsync(chatId, "Ця команда доступна лише адміністратору\\.", parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
                return;
            }

            var messageParts = message.Text!.Split(new[] { ' ' }, 2);
            if (messageParts.Length < 2 || string.IsNullOrWhiteSpace(messageParts[1]))
            {
                await botClient.SendTextMessageAsync(chatId, "Введіть повідомлення для розсилки після команди /broadcast\\.", parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
                return;
            }

            var messageToSend = messageParts[1];
            var allUsers = await _userSettingsService.GetAllUserIdsAsync();
            var successCount = 0;
            var failureCount = 0;

            foreach (var userId in allUsers)
            {
                try
                {
                    await botClient.SendTextMessageAsync(userId, messageToSend, cancellationToken: cancellationToken);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    Console.WriteLine($"Не вдалося надіслати повідомлення користувачу {userId}: {ex.Message}");
                }
            }
            
            // ВИПРАВЛЕНО: Спрощено форматування рядка, щоб уникнути помилки
            var reportText = $"Розсилку завершено\\.\nПовідомлення надіслано до *{successCount}* користувачів\\.\nПомилок: *{failureCount}*\\.";
            await botClient.SendTextMessageAsync(chatId, reportText, parseMode: ParseMode.MarkdownV2, cancellationToken: cancellationToken);
        }
    }
}
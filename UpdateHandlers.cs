using System;
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
                _ => HandleUnknownUpdateAsync(botClient, update, cancellationToken)
            };

            try
            {
                await handler;
            }
            catch (Exception exception)
            {
                await HandlePollingErrorAsync(botClient, exception, cancellationToken);
            }
        }
        
        private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;

            if (message.Location != null)
            {
                Console.WriteLine($"Отримано координати: {message.Location.Latitude}, {message.Location.Longitude}");
                var weatherReport = await _weatherService.GetCurrentWeatherAsync(message.Location.Latitude, message.Location.Longitude);
                await botClient.SendTextMessageAsync(chatId, weatherReport, cancellationToken: cancellationToken);
                return;
            }

            if (message.Text is not { } messageText) return;
            
            Console.WriteLine($"Отримано повідомлення в чаті {chatId}: '{messageText}'");

            var command = messageText.Split(' ').First().ToLower();

            var action = command switch
            {
                "/start" => HandleStartAsync(botClient, message, cancellationToken),
                "/forecast" => HandleForecastAsync(botClient, message, cancellationToken),
                "/setdefault" => HandleSetDefaultAsync(botClient, message, cancellationToken),
                _ => HandleCityNameAsync(botClient, message, cancellationToken)
            };
            
            await action;
        }

        private async Task HandleStartAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { KeyboardButton.WithRequestLocation("📍 Надіслати моє місцезнаходження") }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
            
            string responseMessage = "Вітаю! Я Стрибогів Подих.\n\n" +
                                     "Надішли мені назву міста або поділись геолокацією, і я розповім про погоду.\n\n" +
                                     "Доступні команди:\n" +
                                     "`/forecast [місто]` - прогноз на 5 днів\n" +
                                     "`/setdefault [місто]` - встановити місто для щоденних оновлень";

            await botClient.SendTextMessageAsync(message.Chat.Id, responseMessage, replyMarkup: keyboard, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
        }
        
        private async Task HandleCityNameAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var weatherReport = await _weatherService.GetCurrentWeatherAsync(message.Text);
            await botClient.SendTextMessageAsync(message.Chat.Id, weatherReport, cancellationToken: cancellationToken);
        }
        
        private async Task HandleForecastAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var parts = message.Text.Split(' ');
            string city = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : (await _userSettingsService.GetDefaultCityAsync(message.Chat.Id));

            if (string.IsNullOrEmpty(city))
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Будь ласка, вкажіть місто (напр. `/forecast Київ`) або встановіть місто за замовчуванням.", cancellationToken: cancellationToken);
                return;
            }
            
            var forecastReport = await _weatherService.GetForecastAsync(city);
            await botClient.SendTextMessageAsync(message.Chat.Id, forecastReport, cancellationToken: cancellationToken);
        }

        private async Task HandleSetDefaultAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var parts = message.Text.Split(' ');
            if (parts.Length < 2)
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, "Будь ласка, вкажіть місто, яке хочете зберегти. Наприклад: `/setdefault Львів`", cancellationToken: cancellationToken);
                return;
            }
            var city = string.Join(" ", parts.Skip(1));
            await _userSettingsService.SetDefaultCityAsync(message.Chat.Id, city);
            await botClient.SendTextMessageAsync(message.Chat.Id, $"Чудово! Ваше місто за замовчуванням тепер — *{city}*. Ви будете отримувати щоденний прогноз о 8:00.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
        }

        private Task HandleUnknownUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Невідомий тип оновлення: {update.Type}");
            return Task.CompletedTask;
        }

        public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Помилка Telegram API:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);
            return Task.CompletedTask;
        }
    }
}
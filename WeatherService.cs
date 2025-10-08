using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Stribog
{
    public class WeatherService
    {
        private readonly HttpClient _httpClient = new();
        private readonly string _apiKey;

        public WeatherService(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<string> GetCurrentWeatherAsync(string city)
        {
            try
            {
                string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                var description = json["weather"]![0]!["description"]!.ToString();
                var temp = (int)Math.Round(json["main"]!["temp"]!.Value<double>());
                var feelsLike = (int)Math.Round(json["main"]!["feels_like"]!.Value<double>());
                var wind = json["wind"]!["speed"]!.Value<double>();
                var country = json["sys"]!["country"]!.ToString();
                var cityName = json["name"]!.ToString();

                var report = $"Погода в *{cityName}, {country}*:\n" +
                             $"{CapitalizeFirstLetter(description)}, температура *{temp}°C* (відчувається як *{feelsLike}°C*)\n" +
                             $"Швидкість вітру: {wind:F1} м/с";

                return report;
            }
            catch (Exception)
            {
                // ВИПРАВЛЕНО: Одинарний \ замінено на подвійний \\
                return $"Не вдалося знайти місто '{city}'\\.";
            }
        }
        
        public async Task<string> GetEveningForecastAsync(string city)
        {
            try
            {
                string url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                var sb = new StringBuilder();
                var cityName = json["city"]!["name"]!.ToString();
                sb.AppendLine($"*Прогноз до вечора для міста {cityName}*:");

                var forecasts = json["list"]!.Take(4);
                foreach (var forecast in forecasts)
                {
                    var time = DateTimeOffset.FromUnixTimeSeconds(forecast["dt"]!.Value<long>()).LocalDateTime;
                    var temp = (int)Math.Round(forecast["main"]!["temp"]!.Value<double>());
                    var description = forecast["weather"]![0]!["description"]!.ToString();
                    sb.AppendLine($"*- {time:HH:mm}:* {temp}°C, {description}");
                }
                
                return sb.ToString();
            }
            catch (Exception)
            {
                // ВИПРАВЛЕНО: Одинарний \ замінено на подвійний \\
                return $"Не вдалося отримати прогноз на вечір для '{city}'\\.";
            }
        }

        public async Task<string> GetForecastAsync(string city)
        {
            try
            {
                string url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                var sb = new StringBuilder();
                var cityName = json["city"]!["name"]!.ToString();
                sb.AppendLine($"*Прогноз на 5 днів для міста {cityName}*:");
                
                var dailyForecasts = json["list"]!
                    .GroupBy(f => DateTimeOffset.FromUnixTimeSeconds(f["dt"]!.Value<long>()).LocalDateTime.Date)
                    .Select(g =>
                    {
                        var dayTemp = g.Max(f => f["main"]!["temp_max"]!.Value<double>());
                        var nightTemp = g.Min(f => f["main"]!["temp_min"]!.Value<double>());
                        var description = g.First()["weather"]![0]!["description"]!.ToString();
                        return new
                        {
                            Date = g.Key,
                            DayTemp = (int)Math.Round(dayTemp),
                            NightTemp = (int)Math.Round(nightTemp),
                            Description = description
                        };
                    })
                    .Take(5);

                foreach (var day in dailyForecasts)
                {
                    sb.AppendLine($"*- {day.Date:dd.MM} ({ToTitleCase(day.Date.ToString("dddd", new CultureInfo("uk-UA")))}):*");
                    sb.AppendLine($"  Вдень: *{day.DayTemp}°C*, вночі: *{day.NightTemp}°C*, {day.Description}");
                }

                return sb.ToString();
            }
            catch (Exception)
            {
                // ВИПРАВЛЕНО: Одинарний \ замінено на подвійний \\
                return $"Не вдалося отримати прогноз на 5 днів для '{city}'\\.";
            }
        }

        public string SanitizeMarkdown(string text)
        {
            var escapeChars = new[] { "_", "*", "[", "]", "(", ")", "~", "`", ">", "#", "+", "-", "=", "|", "{", "}", ".", "!" };
            var sb = new StringBuilder(text);
            foreach (var escapeChar in escapeChars)
            {
                sb.Replace(escapeChar, "\\" + escapeChar);
            }
            return sb.ToString();
        }

        public async Task<(bool success, int offset)> GetTimezoneOffsetAsync(string city)
        {
            try
            {
                string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                return (true, json["timezone"]!.Value<int>());
            }
            catch
            {
                return (false, 0);
            }
        }

        public async Task<string?> GetCityNameByCoordsAsync(double latitude, double longitude)
        {
            try
            {
                string url = $"https://api.openweathermap.org/data/2.5/weather?lat={latitude}&lon={longitude}&appid={_apiKey}&units=metric&lang=ua";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                return json["name"]!.ToString();
            }
            catch
            {
                return null;
            }
        }
        
        private string CapitalizeFirstLetter(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            return char.ToUpper(input[0]) + input.Substring(1);
        }
        
        private string ToTitleCase(string input)
        {
            // Виправлено для сумісності з різними культурами
            return new CultureInfo("uk-UA").TextInfo.ToTitleCase(input);
        }
    }
}
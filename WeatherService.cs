using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Stribog
{
    public class WeatherService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public WeatherService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
        }
        
        // *** ФІНАЛЬНЕ ВИПРАВЛЕННЯ: Додано всі необхідні символи для екранування ***
        public string SanitizeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var charsToEscape = new[] { "_", "*", "[", "]", "(", ")", "~", "`", ">", "#", "+", "-", "=", "|", "{", "}", ".", "!" };
            foreach (var ch in charsToEscape)
            {
                text = text.Replace(ch, "\\" + ch);
            }
            return text;
        }

        public async Task<(bool Success, int OffsetSeconds)> GetTimezoneOffsetAsync(string city)
        {
            try
            {
                var weatherUrl = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}";
                var response = await _httpClient.GetStringAsync(weatherUrl);
                var json = JObject.Parse(response);
                if (json["cod"]?.ToString() == "200")
                {
                    var offset = json["timezone"].Value<int>();
                    return (true, offset);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не вдалося отримати часовий пояс для {city}: {ex.Message}");
            }
            return (false, 0);
        }

        public async Task<string> GetCurrentWeatherAsync(string city)
        {
            try
            {
                var weatherUrl = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var weatherResponse = await _httpClient.GetStringAsync(weatherUrl);
                var weatherJson = JObject.Parse(weatherResponse);
                if (weatherJson["cod"]?.ToString() != "200") return "Не вдалося знайти місто. Перевірте назву.";

                var forecastUrl = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var forecastResponse = await _httpClient.GetStringAsync(forecastUrl);
                var forecastJson = JObject.Parse(forecastResponse);

                return FormatWeatherReport(weatherJson, forecastJson);
            }
            catch
            {
                return "Помилка при отриманні погоди. Можливо, невірна назва міста.";
            }
        }

        private string FormatWeatherReport(JObject weatherJson, JObject forecastJson)
        {
            var sb = new StringBuilder();
            var utcOffset = TimeSpan.FromSeconds(weatherJson["timezone"].Value<int>());
            var localTime = DateTimeOffset.UtcNow.ToOffset(utcOffset);

            sb.AppendLine($"*{SanitizeMarkdown(weatherJson["name"].ToString())}, {SanitizeMarkdown(weatherJson["sys"]["country"].ToString())}*");
            sb.AppendLine($"_{localTime:dd MMMM, dddd HH:mm}_");
            sb.AppendLine();

            var description = SanitizeMarkdown(weatherJson["weather"][0]["description"].ToString());
            description = char.ToUpper(description[0]) + description.Substring(1);
            var icon = GetWeatherIcon(weatherJson["weather"][0]["main"].ToString());
            var temp = weatherJson["main"]["temp"].Value<double>();
            var feelsLike = weatherJson["main"]["feels_like"].Value<double>();

            sb.AppendLine($"{description} {icon}");
            sb.AppendLine($"🌡️ *{SanitizeMarkdown(temp.ToString("+#;-#;0"))}°C*");
            sb.AppendLine($"Відчувається як: *{SanitizeMarkdown(feelsLike.ToString("+#;-#;0"))}°C*");
            sb.AppendLine("`------------------------------`");

            var hourlyForecasts = forecastJson?["list"];
            string rainInfo = GetRainInfo(hourlyForecasts);
            if (!string.IsNullOrEmpty(rainInfo)) sb.AppendLine(rainInfo);
            
            string advice = GetWeatherAdvice(weatherJson["weather"][0]["main"].ToString(), temp);
            if (!string.IsNullOrEmpty(advice)) sb.AppendLine(advice);
            
            if (!string.IsNullOrEmpty(rainInfo) || !string.IsNullOrEmpty(advice))
            {
                sb.AppendLine("`------------------------------`");
            }
            
            double minTemp = weatherJson["main"]["temp_min"].Value<double>();
            double maxTemp = weatherJson["main"]["temp_max"].Value<double>();

            if (hourlyForecasts != null && hourlyForecasts.Any())
            {
                var todayForecasts = hourlyForecasts.Where(f => DateTimeOffset.FromUnixTimeSeconds(f["dt"].Value<long>()).ToOffset(utcOffset).Date == localTime.Date);
                if (todayForecasts.Any())
                {
                    minTemp = todayForecasts.Min(f => f["main"]["temp_min"].Value<double>());
                    maxTemp = todayForecasts.Max(f => f["main"]["temp_max"].Value<double>());
                }
            }
            
            sb.AppendLine($"Макс\\.: *{SanitizeMarkdown(maxTemp.ToString("+#;-#;0"))}°*, мін\\.: *{SanitizeMarkdown(minTemp.ToString("+#;-#;0"))}°*");
            sb.AppendLine($"Вітер: *{GetWindDirection(weatherJson["wind"]["deg"].Value<double>())} {SanitizeMarkdown(weatherJson["wind"]["speed"].ToString())} м/с*");
            sb.AppendLine($"Вологість: *{weatherJson["main"]["humidity"]}%*");
            sb.AppendLine($"Тиск: *{weatherJson["main"]["pressure"]} hPa*");

            var sunrise = DateTimeOffset.FromUnixTimeSeconds(weatherJson["sys"]["sunrise"].Value<long>()).ToOffset(utcOffset);
            var sunset = DateTimeOffset.FromUnixTimeSeconds(weatherJson["sys"]["sunset"].Value<long>()).ToOffset(utcOffset);
            sb.AppendLine($"Схід: *{sunrise:HH:mm}*");
            sb.AppendLine($"Захід: *{sunset:HH:mm}*");

            return sb.ToString();
        }

        private string GetRainInfo(JToken hourly)
        {
            if (hourly == null) return "";
            var rainSlots = hourly.Take(8)
                .Where(h => h["weather"]?[0]?["main"]?.ToString() == "Rain")
                .Select(h => DateTimeOffset.FromUnixTimeSeconds(h["dt"].Value<long>()).LocalDateTime)
                .ToList();

            if (!rainSlots.Any()) return "";
            return $"🌧 *Очікується дощ*, найближчий приблизно о {rainSlots.First():HH:mm}\\.";
        }

        private string GetWeatherAdvice(string weatherMain, double temp)
        {
            if (weatherMain == "Rain" || weatherMain == "Thunderstorm") return "💡 Порада: Не забудьте парасольку\\! 🌂";
            if (temp > 28) return "💡 Порада: Спека\\! Пийте більше води\\. 💧";
            if (temp < -5) return "💡 Порада: Одягайтеся тепліше, на вулиці морозно\\! 🧤";
            return "";
        }
        
        public async Task<string> GetEveningForecastAsync(string city)
        {
            try
            {
                var url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                if (json["cod"]?.ToString() != "200") return "Не вдалося знайти місто\\.";
                
                if (json["list"] == null || !json["list"].Any())
                {
                    return $"*Прогноз для м\\. {SanitizeMarkdown(json["city"]?["name"].ToString())}*\n\nНа жаль, погодинний прогноз на сьогодні недоступний\\.";
                }

                var sb = new StringBuilder($"*Погодинний прогноз для м\\. {SanitizeMarkdown(json["city"]["name"].ToString())} до кінця дня:*\n\n");
                var forecasts = json["list"]
                    .Select(item => new {
                        Date = DateTimeOffset.FromUnixTimeSeconds(item["dt"].Value<long>()).LocalDateTime,
                        Temp = (int)Math.Round(item["main"]["temp"].Value<double>()),
                        Description = SanitizeMarkdown(item["weather"][0]["description"].ToString()),
                        Icon = GetWeatherIcon(item["weather"][0]["main"].ToString())
                    })
                    .Where(f => f.Date.Date == DateTime.Today && f.Date.Hour >= DateTime.Now.Hour).Take(8);

                if (!forecasts.Any()) return $"*Прогноз для м\\. {SanitizeMarkdown(json["city"]["name"].ToString())}*\n\nНа сьогодні більше немає даних\\.";
                foreach (var f in forecasts) sb.AppendLine($"`{f.Date:HH:mm}` \\- {f.Temp}°C, {f.Description} {f.Icon}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка в GetEveningForecastAsync: {ex.Message}");
                return "Не вдалося отримати погодинний прогноз\\.";
            }
        }

        public async Task<string> GetForecastAsync(string city)
        {
            var url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            if (json["cod"]?.ToString() != "200") return "Не вдалося знайти місто\\.";

            var sb = new StringBuilder($"*Прогноз на 5 днів для м\\. {SanitizeMarkdown(json["city"]["name"].ToString())}:*\n\n");
            
            var forecasts = json["list"]
                .GroupBy(item => DateTimeOffset.FromUnixTimeSeconds(item["dt"].Value<long>()).LocalDateTime.Date)
                .Select(g => {
                    var midPoint = g.OrderBy(x=> x["dt"]).ElementAt(g.Count()/2);
                    return new {
                        Date = g.Key,
                        TempMin = g.Min(x => x["main"]["temp"].Value<double>()),
                        TempMax = g.Max(x => x["main"]["temp"].Value<double>()),
                        Description = SanitizeMarkdown(midPoint["weather"][0]["description"].ToString()),
                        Icon = GetWeatherIcon(midPoint["weather"][0]["main"].ToString())
                    };
                }).Where(f => f.Date.Date >= DateTime.Today).Take(5);

            foreach (var f in forecasts) sb.AppendLine($"*{f.Date:dd\\.MM (ddd)}*: від {Math.Round(f.TempMin)}° до {Math.Round(f.TempMax)}°, {f.Description} {f.Icon}");
            return sb.ToString();
        }

        public async Task<string> GetCityNameByCoordsAsync(double lat, double lon)
        {
            var url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={_apiKey}&units=metric&lang=ua";
            var response = await _httpClient.GetStringAsync(url);
            return JObject.Parse(response)["name"]?.ToString();
        }

        public async Task<bool> CityExistsAsync(string city)
        {
            var url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            return response.IsSuccessStatusCode;
        }

        private string GetWeatherIcon(string weather) => weather.ToLower() switch
        {
            "clear" => "☀️", "clouds" => "☁️", "rain" => "🌧", "drizzle" => "🌦",
            "thunderstorm" => "⛈", "snow" => "❄️", "mist" or "fog" or "haze" => "🌫", _ => "🌍"
        };
        
        private string GetWindDirection(double degrees)
        {
            string[] directions = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
            return directions[(int)Math.Round(((degrees %= 360) < 0 ? degrees + 360 : degrees) / 45) % 8];
        }
    }
}
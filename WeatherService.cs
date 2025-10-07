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

        public async Task<string> GetCurrentWeatherAsync(string city)
        {
            var url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
            return await FormatWeatherReport(url);
        }
        
        // *** НОВИЙ МЕТОД: Прогноз до кінця дня ***
        public async Task<string> GetEveningForecastAsync(string city)
        {
            var url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                if (json["cod"]?.ToString() != "200") return "Не вдалося знайти місто.";

                var sb = new StringBuilder();
                sb.AppendLine($"*Погодинний прогноз для м. {json["city"]["name"]} до кінця дня:*");
                sb.AppendLine();

                var hourlyForecasts = json["list"]
                    .Select(item => new
                    {
                        Date = DateTimeOffset.FromUnixTimeSeconds(item["dt"].Value<long>()).LocalDateTime,
                        Temp = (int)Math.Round(item["main"]["temp"].Value<double>()),
                        Description = item["weather"][0]["description"].ToString(),
                        Icon = GetWeatherIcon(item["weather"][0]["main"].ToString())
                    })
                    .Where(f => f.Date.Date == DateTime.Today && f.Date.Hour > DateTime.Now.Hour)
                    .Take(8); // Беремо до 8 наступних прогнозів

                if (!hourlyForecasts.Any())
                {
                    return $"*Прогноз для м. {json["city"]["name"]}*\n\nНа сьогодні більше немає даних.";
                }

                foreach (var forecast in hourlyForecasts)
                {
                    sb.AppendLine($"`{forecast.Date:HH:mm}` - {forecast.Temp}°C, {forecast.Description} {forecast.Icon}");
                }
                
                return sb.ToString();
            }
            catch { return "Помилка отримання погодинного прогнозу."; }
        }

        public async Task<string> GetForecastAsync(string city)
        {
            var url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                if (json["cod"]?.ToString() != "200") return "Не вдалося знайти місто.";

                var sb = new StringBuilder();
                sb.AppendLine($"*Прогноз на 5 днів для м. {json["city"]["name"]}:*");
                sb.AppendLine();
                
                var dailyForecasts = json["list"]
                    .GroupBy(item => DateTimeOffset.FromUnixTimeSeconds(item["dt"].Value<long>()).LocalDateTime.Date)
                    .Select(g => g.OrderBy(i => Math.Abs(i["main"]["temp"].Value<double>())).First()) // Беремо запис з середньою температурою
                    .Where(f => DateTimeOffset.FromUnixTimeSeconds(f["dt"].Value<long>()).LocalDateTime.Date > DateTime.Today)
                    .Take(5);

                foreach (var forecast in dailyForecasts)
                {
                    var date = DateTimeOffset.FromUnixTimeSeconds(forecast["dt"].Value<long>()).LocalDateTime;
                    var temp = (int)Math.Round(forecast["main"]["temp"].Value<double>());
                    var description = forecast["weather"][0]["description"].ToString();
                    var icon = GetWeatherIcon(forecast["weather"][0]["main"].ToString());
                    sb.AppendLine($"*{date:dd.MM (ddd)}*: {temp}°C, {description} {icon}");
                }
                return sb.ToString();
            }
            catch { return "Помилка отримання прогнозу на 5 днів."; }
        }
        
        private async Task<string> FormatWeatherReport(string url)
        {
            // ... (цей метод залишається без змін) ...
             try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                if (json["cod"]?.ToString() != "200") return "Не вдалося знайти місто.";

                var sb = new StringBuilder();
                var dt = DateTimeOffset.FromUnixTimeSeconds(json["dt"].Value<long>()).LocalDateTime;

                // --- Заголовок ---
                sb.AppendLine($"*{json["name"]}, {json["sys"]["country"]}*");
                sb.AppendLine($"_{dt:dd MMMM, dddd}_");
                sb.AppendLine();

                // --- Основна інформація ---
                var description = json["weather"][0]["description"].ToString();
                description = char.ToUpper(description[0]) + description.Substring(1); // Робимо першу літеру великою
                var icon = GetWeatherIcon(json["weather"][0]["main"].ToString());
                var temp = json["main"]["temp"].Value<double>();

                sb.AppendLine($"{description} {icon}");
                sb.AppendLine($"🌡️ *{temp:+#;-#;0}°C*");
                sb.AppendLine($"Відчувається як: *{json["main"]["feels_like"].Value<double>():+#;-#;0}°C*");
                sb.AppendLine("`------------------------------`");

                // --- Деталі ---
                sb.AppendLine($"Макс.: *{json["main"]["temp_max"].Value<double>():+#;-#;0}°*, мін.: *{json["main"]["temp_min"].Value<double>():+#;-#;0}°*");
                sb.AppendLine($"Вітер: *{GetWindDirection(json["wind"]["deg"].Value<double>())} {json["wind"]["speed"]} м/с*");
                sb.AppendLine($"Вологість: *{json["main"]["humidity"]}%*");
                sb.AppendLine($"Тиск: *{json["main"]["pressure"]} hPa*");
                
                var sunrise = DateTimeOffset.FromUnixTimeSeconds(json["sys"]["sunrise"].Value<long>()).LocalDateTime;
                var sunset = DateTimeOffset.FromUnixTimeSeconds(json["sys"]["sunset"].Value<long>()).LocalDateTime;
                sb.AppendLine($"Схід: *{sunrise:HH:mm}*");
                sb.AppendLine($"Захід: *{sunset:HH:mm}*");

                return sb.ToString();
            }
            catch { return "Помилка при отриманні погоди."; }
        }
        
        // ... (допоміжні методи GetCityNameByCoordsAsync, CityExistsAsync, GetWeatherIcon, GetWindDirection без змін) ...
        public async Task<string> GetCityNameByCoordsAsync(double lat, double lon) {
             var url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={_apiKey}&units=metric&lang=ua";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                return json["name"]?.ToString();
            }
            catch { return null; }
        }
        public async Task<bool> CityExistsAsync(string city) {
            var url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                return json["cod"]?.ToString() == "200";
            }
            catch { return false; }
        }
        private string GetWeatherIcon(string weather) {
            return weather.ToLower() switch
            {
                "clear" => "☀️",
                "clouds" => "☁️",
                "rain" => "🌧",
                "drizzle" => "🌦",
                "thunderstorm" => "⛈",
                "snow" => "❄️",
                "mist" or "fog" or "haze" => "🌫",
                _ => "🌍"
            };
        }
        private string GetWindDirection(double degrees)
        {
            string[] directions = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
            return directions[(int)Math.Round(((degrees %= 360) < 0 ? degrees + 360 : degrees) / 45) % 8];
        }
    }
}
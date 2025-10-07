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

        public async Task<string> GetForecastAsync(string city)
        {
            // Цей метод залишається без змін, але вивід можна теж стилізувати за бажанням
            var url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
            // ... (попередня логіка прогнозу на 5 днів)
            return "Прогноз на 5 днів..."; // Поки що заглушка
        }

        private async Task<string> FormatWeatherReport(string url)
        {
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
        
        // --- Допоміжні методи ---
        public async Task<string> GetCityNameByCoordsAsync(double lat, double lon) { /* ... без змін ... */ return null; }
        public async Task<bool> CityExistsAsync(string city) { /* ... без змін ... */ return false; }
        private string GetWeatherIcon(string weather) { /* ... без змін ... */ return ""; }

        private string GetWindDirection(double degrees)
        {
            string[] directions = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
            return directions[(int)Math.Round(((degrees %= 360) < 0 ? degrees + 360 : degrees) / 45) % 8];
        }
    }
}
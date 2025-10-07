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
            // Спочатку отримуємо координати міста
            var geoUrl = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
            try
            {
                var geoResponse = await _httpClient.GetStringAsync(geoUrl);
                var geoJson = JObject.Parse(geoResponse);
                if (geoJson["cod"]?.ToString() != "200") return "Не вдалося знайти місто. Перевірте назву.";

                var lat = geoJson["coord"]["lat"].Value<double>();
                var lon = geoJson["coord"]["lon"].Value<double>();
                var cityName = geoJson["name"].ToString();
                var country = geoJson["sys"]["country"].ToString();

                // Тепер робимо один запит для отримання всіх даних
                var oneCallUrl = $"https://api.openweathermap.org/data/2.5/onecall?lat={lat}&lon={lon}&appid={_apiKey}&units=metric&lang=ua&exclude=minutely,alerts";
                var oneCallResponse = await _httpClient.GetStringAsync(oneCallUrl);
                var json = JObject.Parse(oneCallResponse);
                
                return FormatWeatherReport(json, cityName, country);
            }
            catch
            {
                return "Помилка при отриманні погоди.";
            }
        }
        
        // *** ЗМІНА: Метод форматування тепер приймає дані з OneCall API ***
        private string FormatWeatherReport(JObject json, string cityName, string country)
        {
            var current = json["current"];
            var todayDaily = json["daily"][0];
            
            var sb = new StringBuilder();
            var dt = DateTimeOffset.FromUnixTimeSeconds(current["dt"].Value<long>()).LocalDateTime;

            sb.AppendLine($"*{cityName}, {country}*");
            sb.AppendLine($"_{dt:dd MMMM, dddd}_");
            sb.AppendLine();

            var description = current["weather"][0]["description"].ToString();
            description = char.ToUpper(description[0]) + description.Substring(1);
            var icon = GetWeatherIcon(current["weather"][0]["main"].ToString());
            var temp = current["temp"].Value<double>();

            sb.AppendLine($"{description} {icon}");
            sb.AppendLine($"🌡️ *{temp:+#;-#;0}°C*");
            sb.AppendLine($"Відчувається як: *{current["feels_like"].Value<double>():+#;-#;0}°C*");
            sb.AppendLine("`------------------------------`");
            
            // *** ДОДАНО: Прогноз дощу та поради ***
            string rainInfo = GetRainInfo(json["hourly"]);
            if (!string.IsNullOrEmpty(rainInfo))
            {
                sb.AppendLine(rainInfo);
            }
            string advice = GetWeatherAdvice(current["weather"][0]["main"].ToString(), temp, todayDaily["uvi"]?.Value<double>() ?? 0);
            if (!string.IsNullOrEmpty(advice))
            {
                sb.AppendLine(advice);
                sb.AppendLine("`------------------------------`");
            }
            
            sb.AppendLine($"Макс.: *{todayDaily["temp"]["max"].Value<double>():+#;-#;0}°*, мін.: *{todayDaily["temp"]["min"].Value<double>():+#;-#;0}°*");
            sb.AppendLine($"Вітер: *{GetWindDirection(current["wind_deg"].Value<double>())} {current["wind_speed"]} м/с*");
            sb.AppendLine($"Вологість: *{current["humidity"]}%*");
            sb.AppendLine($"Тиск: *{current["pressure"]} hPa*");

            var sunrise = DateTimeOffset.FromUnixTimeSeconds(current["sunrise"].Value<long>()).LocalDateTime;
            var sunset = DateTimeOffset.FromUnixTimeSeconds(current["sunset"].Value<long>()).LocalDateTime;
            sb.AppendLine($"Схід: *{sunrise:HH:mm}*");
            sb.AppendLine($"Захід: *{sunset:HH:mm}*");

            return sb.ToString();
        }

        // *** ДОДАНО: Нові/оновлені методи ***
        private string GetRainInfo(JToken hourly)
        {
            var rainHours = hourly?
                .Take(12) // Перевіряємо наступні 12 годин
                .Where(h => h["weather"][0]["main"].ToString() == "Rain")
                .Select(h => DateTimeOffset.FromUnixTimeSeconds(h["dt"].Value<long>()).LocalDateTime)
                .ToList();

            if (rainHours == null || !rainHours.Any()) return "";
            
            // Простий вивід першого очікуваного дощу
            return $"🌧 *Очікується дощ*, найближчий о {rainHours.First():HH:mm}.";
        }
        
        private string GetWeatherAdvice(string weatherMain, double temp, double uvi)
        {
            if (weatherMain == "Rain" || weatherMain == "Thunderstorm")
                return "💡 Порада: Не забудьте парасольку! 🌂";
            if (uvi > 6) // УФ-індекс високий
                return "💡 Порада: Високий УФ-індекс, краще захиститися від сонця! 🧴";
            if (temp > 28)
                return "💡 Порада: Спека! Пийте більше води. 💧";
            if (temp < -5)
                 return "💡 Порада: Одягайтеся тепліше, на вулиці морозно! 🧤";
            
            return "";
        }
        
        // ... (решта методів: GetEveningForecastAsync, GetForecastAsync, CityExistsAsync і т.д. без змін) ...
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
                    .Where(f => DateTimeOffset.FromUnixTimeSeconds(f["dt"].Value<long>()).LocalDateTime.Date >= DateTime.Today)
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
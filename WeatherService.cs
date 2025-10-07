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
            try
            {
                // ЗАПИТ 1: Отримуємо поточні дані (температура, схід/захід сонця і т.д.)
                var weatherUrl = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var weatherResponse = await _httpClient.GetStringAsync(weatherUrl);
                var weatherJson = JObject.Parse(weatherResponse);
                if (weatherJson["cod"]?.ToString() != "200") return "Не вдалося знайти місто. Перевірте назву.";

                // ЗАПИТ 2: Отримуємо погодинний та денний прогноз (для дощу та мін/макс температури)
                var forecastUrl = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
                var forecastResponse = await _httpClient.GetStringAsync(forecastUrl);
                var forecastJson = JObject.Parse(forecastResponse);

                return FormatWeatherReport(weatherJson, forecastJson);
            }
            catch
            {
                // Ця помилка тепер буде виникати значно рідше
                return "Помилка при отриманні погоди. Можливо, невірна назва міста.";
            }
        }

        private string FormatWeatherReport(JObject weatherJson, JObject forecastJson)
        {
            var sb = new StringBuilder();
            var dt = DateTimeOffset.FromUnixTimeSeconds(weatherJson["dt"].Value<long>()).LocalDateTime;

            sb.AppendLine($"*{weatherJson["name"]}, {weatherJson["sys"]["country"]}*");
            sb.AppendLine($"_{dt:dd MMMM, dddd}_");
            sb.AppendLine();

            var description = weatherJson["weather"][0]["description"].ToString();
            description = char.ToUpper(description[0]) + description.Substring(1);
            var icon = GetWeatherIcon(weatherJson["weather"][0]["main"].ToString());
            var temp = weatherJson["main"]["temp"].Value<double>();

            sb.AppendLine($"{description} {icon}");
            sb.AppendLine($"🌡️ *{temp:+#;-#;0}°C*");
            sb.AppendLine($"Відчувається як: *{weatherJson["main"]["feels_like"].Value<double>():+#;-#;0}°C*");
            sb.AppendLine("`------------------------------`");

            // --- Прогноз дощу та поради з даних 5-денного прогнозу ---
            var hourlyForecasts = forecastJson["list"];
            string rainInfo = GetRainInfo(hourlyForecasts);
            if (!string.IsNullOrEmpty(rainInfo))
            {
                sb.AppendLine(rainInfo);
            }
            string advice = GetWeatherAdvice(weatherJson["weather"][0]["main"].ToString(), temp);
            if (!string.IsNullOrEmpty(advice))
            {
                sb.AppendLine(advice);
                sb.AppendLine("`------------------------------`");
            }
            
            // --- Мін/макс температура на сьогодні ---
            var todayForecasts = hourlyForecasts.Where(f => DateTimeOffset.FromUnixTimeSeconds(f["dt"].Value<long>()).LocalDateTime.Date == DateTime.Today);
            var minTemp = todayForecasts.Any() ? todayForecasts.Min(f => f["main"]["temp_min"].Value<double>()) : weatherJson["main"]["temp_min"].Value<double>();
            var maxTemp = todayForecasts.Any() ? todayForecasts.Max(f => f["main"]["temp_max"].Value<double>()) : weatherJson["main"]["temp_max"].Value<double>();
            
            sb.AppendLine($"Макс.: *{maxTemp:+#;-#;0}°*, мін.: *{minTemp:+#;-#;0}°*");
            sb.AppendLine($"Вітер: *{GetWindDirection(weatherJson["wind"]["deg"].Value<double>())} {weatherJson["wind"]["speed"]} м/с*");
            sb.AppendLine($"Вологість: *{weatherJson["main"]["humidity"]}%*");
            sb.AppendLine($"Тиск: *{weatherJson["main"]["pressure"]} hPa*");

            var sunrise = DateTimeOffset.FromUnixTimeSeconds(weatherJson["sys"]["sunrise"].Value<long>()).LocalDateTime;
            var sunset = DateTimeOffset.FromUnixTimeSeconds(weatherJson["sys"]["sunset"].Value<long>()).LocalDateTime;
            sb.AppendLine($"Схід: *{sunrise:HH:mm}*");
            sb.AppendLine($"Захід: *{sunset:HH:mm}*");

            return sb.ToString();
        }
        
        private string GetRainInfo(JToken hourly)
        {
            var rainSlots = hourly?
                .Take(8) // Перевіряємо наступні 24 години (8 слотів по 3 години)
                .Where(h => h["weather"][0]["main"].ToString() == "Rain")
                .Select(h => DateTimeOffset.FromUnixTimeSeconds(h["dt"].Value<long>()).LocalDateTime)
                .ToList();

            if (rainSlots == null || !rainSlots.Any()) return "";
            
            return $"🌧 *Очікується дощ*, найближчий приблизно о {rainSlots.First():HH:mm}.";
        }
        
        private string GetWeatherAdvice(string weatherMain, double temp)
        {
            if (weatherMain == "Rain" || weatherMain == "Thunderstorm")
                return "💡 Порада: Не забудьте парасольку! 🌂";
            if (temp > 28)
                return "💡 Порада: Спека! Пийте більше води. 💧";
            if (temp < -5)
                 return "💡 Порада: Одягайтеся тепліше, на вулиці морозно! 🧤";
            
            return "";
        }
        
        // ... (решта методів: GetEveningForecastAsync, GetForecastAsync, CityExistsAsync і т.д. залишаються без змін) ...
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
                    .Where(f => f.Date.Date == DateTime.Today && f.Date.Hour >= DateTime.Now.Hour)
                    .Take(8); 

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
                    .Select(g => new {
                        Date = g.Key,
                        TempMin = g.Min(x => x["main"]["temp"].Value<double>()),
                        TempMax = g.Max(x => x["main"]["temp"].Value<double>()),
                        Description = g.First()["weather"][0]["description"].ToString(),
                        Icon = GetWeatherIcon(g.First()["weather"][0]["main"].ToString())
                    })
                    .Where(f => f.Date.Date >= DateTime.Today)
                    .Take(5);

                foreach (var forecast in dailyForecasts)
                {
                    sb.AppendLine($"*{forecast.Date:dd.MM (ddd)}*: від {Math.Round(forecast.TempMin)}° до {Math.Round(forecast.TempMax)}°, {forecast.Description} {forecast.Icon}");
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
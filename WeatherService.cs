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

        // --- Методи для поточної погоди ---
        public async Task<string> GetCurrentWeatherAsync(string city)
        {
            var url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
            return await GetWeatherReport(url);
        }

        public async Task<string> GetCurrentWeatherAsync(double lat, double lon)
        {
            var url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={_apiKey}&units=metric&lang=ua";
            return await GetWeatherReport(url);
        }
        
        // --- Метод для прогнозу на 5 днів ---
        public async Task<string> GetForecastAsync(string city)
        {
            var url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            
            if (json["cod"]?.ToString() != "200")
            {
                return "Не вдалося знайти місто. Спробуйте ще раз.";
            }
            
            var cityName = json["city"]["name"];
            var dailyForecasts = json["list"]
                .GroupBy(item => DateTime.Parse(item["dt_txt"].ToString()).Date)
                .Select(g => g.OrderBy(i => Math.Abs(TimeSpan.Parse(i["dt_txt"].ToString().Substring(11)).TotalHours - 14)).First()) // Обираємо прогноз, найближчий до 14:00
                .Take(5);

            var sb = new StringBuilder();
            sb.AppendLine($"Прогноз погоди на 5 днів для міста *{cityName}*:");
            sb.AppendLine();
            
            foreach (var forecast in dailyForecasts)
            {
                var date = DateTime.Parse(forecast["dt_txt"].ToString());
                var temp = (int)Math.Round(forecast["main"]["temp"].Value<double>());
                var description = forecast["weather"][0]["description"].ToString();
                var icon = GetWeatherIcon(forecast["weather"][0]["main"].ToString());
                
                sb.AppendLine($"{date:dd.MM (ddd)}: {temp}°C, {description} {icon}");
            }
            
            return sb.ToString();
        }

        private async Task<string> GetWeatherReport(string url)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                if (json["cod"]?.ToString() != "200")
                {
                    return "Не вдалося знайти місто. Спробуйте ще раз.";
                }

                var cityName = json["name"];
                var temp = (int)Math.Round(json["main"]["temp"].Value<double>());
                var feelsLike = (int)Math.Round(json["main"]["feels_like"].Value<double>());
                var description = json["weather"][0]["description"].ToString();
                var windSpeed = json["wind"]["speed"];
                var humidity = json["main"]["humidity"];
                var weatherMain = json["weather"][0]["main"].ToString();
                var icon = GetWeatherIcon(weatherMain);

                // Отримуємо погодинний прогноз для інформації про дощ
                var lat = json["coord"]["lat"];
                var lon = json["coord"]["lon"];
                var rainInfo = await GetRainInfoAsync(lat.Value<double>(), lon.Value<double>());

                var sb = new StringBuilder();
                sb.AppendLine($"Погода в місті *{cityName}* зараз:");
                sb.AppendLine($"{icon} {temp}°C (відчувається як {feelsLike}°C), {description}");
                sb.AppendLine($"🌬 Вітер: {windSpeed} м/с");
                sb.AppendLine($"💧 Вологість: {humidity}%");
                if (!string.IsNullOrEmpty(rainInfo))
                {
                    sb.AppendLine(rainInfo);
                }
                sb.AppendLine();
                sb.AppendLine(GetWeatherAdvice(weatherMain, temp));
                
                return sb.ToString();
            }
            catch (Exception)
            {
                return "Щось пішло не так. Перевірте назву міста.";
            }
        }
        
        private async Task<string> GetRainInfoAsync(double lat, double lon)
        {
            var url = $"https://api.openweathermap.org/data/2.5/onecall?lat={lat}&lon={lon}&exclude=current,minutely,daily,alerts&appid={_apiKey}&units=metric";
            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            
            var hourly = json["hourly"]?.Take(12); // Беремо прогноз на 12 годин
            if (hourly == null) return "";

            var rainHours = hourly
                .Where(h => h["weather"][0]["main"].ToString() == "Rain")
                .Select(h => DateTimeOffset.FromUnixTimeSeconds(h["dt"].Value<long>()).LocalDateTime.Hour)
                .ToList();

            if (!rainHours.Any()) return "";

            // Простий алгоритм для знаходження проміжків
            var startTime = rainHours.First();
            var endTime = startTime;
            var intervals = new StringBuilder("🌧 ");

            for (int i = 1; i < rainHours.Count; i++)
            {
                if (rainHours[i] == endTime + 1)
                {
                    endTime = rainHours[i];
                }
                else
                {
                    intervals.Append($"з {startTime}:00 до {endTime + 1}:00, ");
                    startTime = endTime = rainHours[i];
                }
            }
            intervals.Append($"з {startTime}:00 до {endTime + 1}:00.");

            return intervals.ToString();
        }

        private string GetWeatherIcon(string weather)
        {
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

        private string GetWeatherAdvice(string weather, double temp)
        {
            if (weather == "Rain" || weather == "Thunderstorm")
                return "Порада: Не забудьте парасольку! 🌂";
            if (weather == "Snow" || temp < -5)
                return "Порада: Одягайтеся тепліше, на вулиці морозно! 🧤";
            if (temp > 28)
                return "Порада: Спека! Пийте більше води та уникайте сонця. 💧";
            
            return "";
        }
    }
}
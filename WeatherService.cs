using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Stribog
{
    public class WeatherService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public WeatherService()
        {
            _httpClient = new HttpClient();
            _apiKey = Environment.GetEnvironmentVariable("OPENWEATHERMAP_API_KEY");
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("API ключ для OpenWeatherMap не встановлено.");
            }
        }

        public async Task<string> GetWeatherAsync(string city)
        {
            var requestUrl = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
            var response = await _httpClient.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("На жаль, не вдалося отримати дані про погоду для цього міста.");
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            var description = data.SelectToken("weather[0].description")?.ToString() ?? "Невідомо";
            var temp = data.SelectToken("main.temp")?.Value<double>() ?? 0.0;
            var feelsLike = data.SelectToken("main.feels_like")?.Value<double>() ?? 0.0;
            var humidity = data.SelectToken("main.humidity")?.Value<int>() ?? 0;
            var windSpeed = data.SelectToken("wind.speed")?.Value<double>() ?? 0.0;
            var pressure = data.SelectToken("main.pressure")?.Value<int>() ?? 0;
            var visibility = data.SelectToken("visibility")?.Value<int>() ?? 0;
            var cloudiness = data.SelectToken("clouds.all")?.Value<int>() ?? 0;
            
            var timezoneOffset = data.SelectToken("timezone")?.Value<int>() ?? 0;
            var sunriseUnix = data.SelectToken("sys.sunrise")?.Value<long>() ?? 0;
            var sunsetUnix = data.SelectToken("sys.sunset")?.Value<long>() ?? 0;
            
            var sunrise = DateTimeOffset.FromUnixTimeSeconds(sunriseUnix).ToOffset(TimeSpan.FromSeconds(timezoneOffset));
            var sunset = DateTimeOffset.FromUnixTimeSeconds(sunsetUnix).ToOffset(TimeSpan.FromSeconds(timezoneOffset));

            var cityName = data.SelectToken("name")?.ToString() ?? city;
            var country = data.SelectToken("sys.country")?.ToString() ?? "";
            
            // ВИПРАВЛЕНО: Використовуємо часовий пояс міста
            var cityTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromSeconds(timezoneOffset));

            var sb = new StringBuilder();
            sb.AppendLine($"*Погода в місті {cityName}, {country}*");
            sb.AppendLine($"_{cityTime:dd MMMM, HH:mm}_");
            sb.AppendLine();
            sb.AppendLine($"🔹 *Зараз:* {char.ToUpper(description[0]) + description.Substring(1)}");
            sb.AppendLine($"🌡️ *Температура:* {temp:+#.#;-#.#;0}°C (відчувається як {feelsLike:+#.#;-#.#;0}°C)");
            sb.AppendLine();
            sb.AppendLine("*Додаткові показники:*");
            sb.AppendLine($"💧 *Вологість:* {humidity}%");
            sb.AppendLine($"💨 *Вітер:* {windSpeed:0.0} м/с");
            sb.AppendLine($"☁️ *Хмарність:* {cloudiness}%");
            sb.AppendLine($"🧭 *Тиск:* {pressure} гПа");
            sb.AppendLine($"👁️ *Видимість:* {visibility / 1000.0:0.0} км");
            sb.AppendLine();
            sb.AppendLine($"🌅 *Схід сонця:* {sunrise:HH:mm}");
            sb.AppendLine($"🌇 *Захід сонця:* {sunset:HH:mm}");
            sb.AppendLine();
            sb.AppendLine($"💡 *Порада:* {GetWeatherAdvice(temp, humidity, windSpeed, description)}");
            sb.AppendLine();
            sb.AppendLine("_Бажаю Вам гарного дня!_");

            return sb.ToString();
        }

        public async Task<string> GetTodayForecastAsync(string city)
        {
            var forecastUrl = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
            var forecastResp = await _httpClient.GetAsync(forecastUrl);
            if (!forecastResp.IsSuccessStatusCode)
                throw new Exception("Не вдалося отримати сьогоднішній прогноз.");

            var forecastJson = await forecastResp.Content.ReadAsStringAsync();
            var forecastData = JObject.Parse(forecastJson);
            var cityName = forecastData["city"]["name"].ToString();
            var country = forecastData["city"]["country"].ToString();
            var offset = forecastData["city"]["timezone"].Value<int>();
            var cityTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromSeconds(offset));

            var sb = new StringBuilder();
            sb.AppendLine($"*Прогноз на сьогодні для міста {cityName}, {country}*");
            sb.AppendLine($"_{cityTime:dd MMMM, dddd}_");
            sb.AppendLine();

            var list = forecastData["list"] as JArray;
            if (list != null)
            {
                var todayForecasts = list.Where(item =>
                {
                    var localTime = DateTimeOffset.FromUnixTimeSeconds(item["dt"].Value<long>()).ToOffset(TimeSpan.FromSeconds(offset)).DateTime;
                    return localTime.Date == cityTime.Date;
                });
                
                if (!todayForecasts.Any())
                {
                    sb.AppendLine("На жаль, детальний прогноз на сьогодні наразі недоступний.");
                }
                else
                {
                    foreach (var item in todayForecasts)
                    {
                        var localTime = DateTimeOffset.FromUnixTimeSeconds(item["dt"].Value<long>()).ToOffset(TimeSpan.FromSeconds(offset)).DateTime;
                        var temp = item["main"]["temp"].Value<double>();
                        var desc = item["weather"][0]["description"].ToString();
                        var pop = item["pop"]?.Value<double>() ?? 0;
                        var emoji = GetWeatherEmoji(desc, pop);
                        
                        sb.AppendLine($"*{localTime:HH:mm}* - {temp:+#.#;-#.#;0}°C, {emoji} {desc}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("_Гарного дня!_");
            return sb.ToString();
        }

        // НОВИЙ МЕТОД: Прогноз на 5 днів
        public async Task<string> GetFiveDayForecastAsync(string city)
        {
            var forecastUrl = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
            var response = await _httpClient.GetAsync(forecastUrl);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Не вдалося отримати прогноз на 5 днів.");
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            var cityName = data["city"]["name"].ToString();
            var country = data["city"]["country"].ToString();
            var offset = data["city"]["timezone"].Value<int>();

            var sb = new StringBuilder();
            sb.AppendLine($"*Прогноз погоди на 5 днів у місті {cityName}, {country}*");
            sb.AppendLine();
            
            var culture = new CultureInfo("uk-UA");
            
            var forecastsByDay = (data["list"] as JArray)
                .GroupBy(item => DateTimeOffset.FromUnixTimeSeconds(item["dt"].Value<long>()).ToOffset(TimeSpan.FromSeconds(offset)).Date)
                .Take(5);

            foreach (var day in forecastsByDay)
            {
                var date = day.Key;
                var dayOfWeek = culture.DateTimeFormat.GetDayName(date.DayOfWeek);
                var minTemp = day.Min(item => item["main"]["temp_min"].Value<double>());
                var maxTemp = day.Max(item => item["main"]["temp_max"].Value<double>());
                
                // Опис погоди беремо з прогнозу на середину дня (близько 12:00)
                var midDayForecast = day.OrderBy(item => Math.Abs(item["dt_txt"].Value<string>().Substring(11, 2) == "12" ? 0 : 1)).First();
                var description = midDayForecast["weather"][0]["description"].ToString();
                var pop = midDayForecast["pop"]?.Value<double>() ?? 0;
                var emoji = GetWeatherEmoji(description, pop);
                
                sb.AppendLine($"*🗓️ {date:dd MMMM}, {char.ToUpper(dayOfWeek[0]) + dayOfWeek.Substring(1)}*");
                sb.AppendLine($"   {emoji} {char.ToUpper(description[0]) + description.Substring(1)}");
                sb.AppendLine($"   🌡️ Температура: від {minTemp:+#.#;-#.#;0}°C до {maxTemp:+#.#;-#.#;0}°C");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GetWeatherEmoji(string description, double pop)
        {
            var baseDesc = description.ToLower();
            if (baseDesc.Contains("гроза")) return "⛈️";
            if (pop > 0.1) return "🌧️";
            if (baseDesc.Contains("дощ") || baseDesc.Contains("мряка")) return "💧";
            if (baseDesc.Contains("сніг")) return "❄️";
            if (baseDesc.Contains("хмар")) return "☁️";
            if (baseDesc.Contains("мінлива")) return "⛅️";
            if (baseDesc.Contains("ясно")) return "☀️";
            return "🌤️";
        }

        private string GetWeatherAdvice(double temp, int humidity, double windSpeed, string description)
        {
            if (description.Contains("гроза")) return "Будь ласка, будьте обережні. Радимо залишатися вдома під час грози.";
            if (description.Contains("дощ") || description.Contains("мряка")) return "Не забудьте парасольку!";
            if (description.Contains("сніг")) return "Одягніться тепло, взуття має бути неслизьким.";

            if (temp > 30) return "Сьогодні дуже спекотно. Пийте багато води та уникайте сонця опівдні.";
            if (temp > 22) return "Чудова тепла погода! Гарний день для прогулянок.";
            if (temp < 0) return "Мороз! Одягайтесь тепліше, не забувайте про шапку та рукавички.";
            if (temp < 5) return "Прохолодно. Рекомендуємо вдягнути куртку.";

            if (windSpeed > 12) return "Сильний вітер. Тримайтеся подалі від дерев та конструкцій, що хитаються.";
            if (humidity > 85) return "Висока вологість. Може відчуватися задуха.";

            return "Сьогодні сприятливі погодні умови. Гарного дня!";
        }
    }
}
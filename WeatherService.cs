using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Stribog;

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

        // --- Парсинг даних ---
        var description = data["weather"][0]["description"].ToString();
        var temp = data["main"]["temp"].Value<double>();
        var humidity = data["main"]["humidity"].Value<int>();
        var windSpeed = data["wind"]["speed"].Value<double>();

        // --- Формування повідомлення ---
        var cityName = data["name"].ToString();
        var country = data["sys"]["country"].ToString();

        var sb = new StringBuilder();
        sb.AppendLine($"🌤️ Погода в місті {cityName}, {country} 🌤️");
        sb.AppendLine($"🌡️ Температура: +{temp:0.0}°C  (відчувається як +{data["main"]["feels_like"]:0.0}°C)");
        sb.AppendLine($"💨 Вітер: {windSpeed:0.0} м/с");
        sb.AppendLine($"💧 Вологість: {humidity}%");
        sb.AppendLine();
        sb.AppendLine($"💡 Порада: {GetWeatherAdvice(temp, humidity, windSpeed, description)}");
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

        var sb = new StringBuilder();
        sb.AppendLine($"{cityName}, {country}");
        var headerDate = DateTime.Now.ToString("dd MMMM, dddd HH:mm", CultureInfo.CreateSpecificCulture("en-US"));
        sb.AppendLine(headerDate);
        sb.AppendLine();

        var list = forecastData["list"] as JArray;
        if (list != null)
        {
            var nowCity = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromSeconds(offset)).DateTime;
            var today = new DateTime(nowCity.Year, nowCity.Month, nowCity.Day);

            var windows = new Dictionary<string, string>();
            foreach (var item in list)
            {
                var dt = item["dt"].Value<long>();
                var local = DateTimeOffset.FromUnixTimeSeconds(dt).ToOffset(TimeSpan.FromSeconds(offset)).DateTime;
                if (local.Date != today.Date) continue;

                int blockStartHour = (local.Hour / 2) * 2;
                var windowLabel = $"{blockStartHour:00}:00-{(blockStartHour + 2):00}:00";

                var desc = item["weather"][0]["description"].ToString();
                var pop = item["pop"]?.Value<double>() ?? 0;
                var emoji = GetWeatherEmoji(desc, pop);

                if (!windows.ContainsKey(windowLabel))
                    windows[windowLabel] = $"{emoji} {desc}";
                else
                    windows[windowLabel] = windows[windowLabel] + " " + $"{emoji} {desc}";
            }

            foreach (var w in windows.OrderBy(k => k.Key))
            {
                sb.AppendLine($"{w.Key}: {w.Value}");
            }

            // Одна загальна порада на сьогодні
            var todayTemp = list[0]["main"]["temp"].Value<double>();
            var todayHumidity = list[0]["main"]["humidity"].Value<int>();
            var todayWind = list[0]["wind"]["speed"].Value<double>();
            var todayDesc = list[0]["weather"][0]["description"].ToString();
            sb.AppendLine();
            sb.AppendLine($"💡 Порада дня: {GetWeatherAdvice(todayTemp, todayHumidity, todayWind, todayDesc)}");
            sb.AppendLine();
        }

        sb.AppendLine("_Гарного дня!_");
        return sb.ToString();
    }

    private string GetWindDirectionArrow(double deg)
    {
        string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
        int idx = (int)((deg / 45) + 0.5) % 8;
        return arrows[idx];
    }

    private string GetWeatherEmoji(string description, double pop)
    {
        var baseDesc = description.ToLower();
        if (baseDesc.Contains("гроза")) return "⛈️";
        if (pop > 0) return "🌧️";
        if (baseDesc.Contains("дощ") || baseDesc.Contains("мряка")) return "🌧️";
        if (baseDesc.Contains("сніг")) return "❄️";
        if (baseDesc.Contains("хмар")) return "☁️";
        if (baseDesc.Contains("ясн") || baseDesc.Contains("сон") || baseDesc.Contains("солн")) return "☀️";
        return "🌤️";
    }

    private string GetWeatherAdvice(double temp, int humidity, double windSpeed, string description)
    {
        if (description.Contains("гроза")) return "Будь ласка, будьте обережні. Радимо залишатися вдома під час грози.";
        if (description.Contains("дощ") || description.Contains("мряка")) return "Парасолька потрібна сьогодні.";
        if (description.Contains("сніг")) return "Одягніться тепло, взуття із протектором.";

        if (temp > 30) return "Сьогодні дуже спекотно. Пийте багато води.";
        if (temp > 22) return "Чудова тепла погода! Гарний день для прогулянок.";
        if (temp < 0) return "Мороз! Одягайтесь тепліше.";
        if (temp < 5) return "Прохолодно. Рекомендуємо тепло вдягнути.";

        if (windSpeed > 12) return "Сильний вітер. Тримайтеся подалі від дерев.";
        if (humidity > 85) return "Висока вологість. Можлива втома; пийте більше води.";

        return "Сьогодні сприятливі погодні умови. Гарного дня!";
    }
}

using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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

        // ---- 5-денний прогноз (кожні 3 години) ----
        var forecastUrl = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
        var forecastResp = await _httpClient.GetAsync(forecastUrl);
        var forecastJson = await forecastResp.Content.ReadAsStringAsync();
        var forecastData = JObject.Parse(forecastJson);

        var sb = new StringBuilder();

        // Заголовок
        var cityName = data["name"].ToString();
        var country = data["sys"]["country"].ToString();
        sb.AppendLine($"{cityName}, {country}");
        var headerDate = DateTime.Now.ToString("dd MMMM, dddd HH:mm", CultureInfo.CreateSpecificCulture("en-US"));
        sb.AppendLine(headerDate);
        sb.AppendLine();

        // Поточна погода
        var description = data["weather"][0]["description"].ToString();
        var temp = data["main"]["temp"].Value<double>();
        var feels = data["main"]["feels_like"].Value<double>();
        sb.AppendLine($"🌧️ {char.ToUpper(description[0]) + description.Substring(1)}");
        sb.AppendLine($"🌡️ +{temp:0.0}°C");
        sb.AppendLine($"Відчувається як: +{feels:0.0}°C");
        sb.AppendLine(new string('-', 30));

        // Додати пораду для поточних умов
        sb.AppendLine($"💡 Порада: {GetWeatherAdvice(temp, data["main"]["humidity"].Value<int>(), data["wind"]["speed"].Value<double>(), description)}");
        sb.AppendLine(new string('-', 30));

        // Очікувані опади (найближчий проміжок)
        string nextRainStr = null;
        if (forecastData?["list"] is JArray list)
        {
            foreach (var item in list)
            {
                var pop = item["pop"]?.Value<double>() ?? 0;
                if (pop > 0)
                {
                    var dt = item["dt"].Value<long>();
                    var dtLocal = DateTimeOffset.FromUnixTimeSeconds(dt).ToLocalTime().DateTime;
                    var hour = dtLocal.ToString("HH:mm");
                    nextRainStr = $"🌧 Очікується дощ, найближчий приблизно о {hour}.";
                    break;
                }
            }
        }
        if (nextRainStr != null)
        {
            sb.AppendLine(nextRainStr);
            sb.AppendLine("💡 Порада: Не забудьте парасольку! 🌂");
            sb.AppendLine(new string('-', 30));
        }

        // Мін/Макс і вітер, вологість, тиск
        var maxTemp = data["main"]["temp_max"].Value<double>();
        var minTemp = data["main"]["temp_min"].Value<double>();
        var humidity = data["main"]["humidity"].Value<int>();
        var windSpeed = data["wind"]["speed"].Value<double>();
        var windDeg = data["wind"]["deg"]?.Value<double>() ?? 0;
        var pressure = data["main"]["pressure"].Value<int>();

        string windDir = GetWindDirectionArrow(windDeg);

        sb.AppendLine($"Макс.: +{maxTemp:0.0}°, мін.: +{minTemp:0.0}°");
        sb.AppendLine($"Вітер: {windDir} {windSpeed:0.0} м/с");
        sb.AppendLine($"Вологість: {humidity}%");
        sb.AppendLine($"Тиск: {pressure} hPa");

        // Схід/Захід
        var sunrise = data["sys"]["sunrise"].Value<long>();
        var sunset = data["sys"]["sunset"].Value<long>();
        var sunriseTime = DateTimeOffset.FromUnixTimeSeconds(sunrise).ToLocalTime().ToString("HH:mm");
        var sunsetTime = DateTimeOffset.FromUnixTimeSeconds(sunset).ToLocalTime().ToString("HH:mm");
        sb.AppendLine($"Схід: {sunriseTime}");
        sb.AppendLine($"Захід: {sunsetTime}");

        // Прогноз на 5 днів (перші 40 записів з forecast)
        sb.AppendLine(new string('-', 30));
        sb.AppendLine("Прогноз на 5 днів (кожні 3 години):");
        if (forecastData?["list"] is JArray fl)
        {
            int count = 0;
            foreach (var fi in fl)
            {
                if (count >= 40) break;
                var dt = fi["dt"].Value<long>();
                var dtLocal = DateTimeOffset.FromUnixTimeSeconds(dt).ToLocalTime().DateTime;
                var fDesc = fi["weather"][0]["description"].ToString();
                var fTemp = fi["main"]["temp"].Value<double>();
                var fHumidity = fi["main"]["humidity"].Value<int>();
                var fWind = fi["wind"]["speed"].Value<double>();
                var fPop = fi["pop"]?.Value<double>() ?? 0;
                var fRain = fPop > 0 ? $"опади {fPop * 100:0.0}%" : "без опадів";
                // Порада для прогнозу
                var fAdvice = GetWeatherAdvice(fTemp, fHumidity, fWind, fDesc);
                sb.AppendLine($"{dtLocal:yyyy-MM-dd HH:mm} - {fDesc} - {fRain}");
                sb.AppendLine($"💡 Порада: {fAdvice}");
                count++;
            }
        }

        sb.AppendLine("_Бажаю Вам гарного дня!_");

        return sb.ToString();
    }

    private string GetWindDirectionArrow(double deg)
    {
        // 8 напрямків
        string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
        int idx = (int)((deg / 45) + 0.5) % 8;
        return arrows[idx];
    }

    // Загальна порада для даних погоди (можна використовувати як для поточної, так і для прогнозу)
    private string GetWeatherAdvice(double temp, int humidity, double windSpeed, string description)
    {
        // Реагуємо на опади передусім
        if (description.Contains("гроза"))
        {
            return "Будь ласка, будьте особливо обережні. Радимо утриматися від виходу на вулицю без нагальної потреби.";
        }
        if (description.Contains("дощ") || description.Contains("мряка"))
        {
            return "Схоже, що сьогодні знадобиться парасолька. Не забудьте її, виходячи з дому.";
        }
        if (description.Contains("сніг"))
        {
            return "Падає сніг! Будь ласка, вдягайтеся тепліше та оберіть взуття, що не ковзає.";
        }

        // Потім за температуру
        if (temp > 30)
        {
            return "Сьогодні дуже спекотно. Пийте багато води та залишайтесь у тіні.";
        }
        if (temp > 22)
        {
            return "Чудова тепла погода! Гарний час для прогулянки.";
        }
        if (temp < 5 && temp >= 0)
        {
            return "Зовні прохолодно. Рекомендуємо теплу куртку.";
        }
        if (temp < 0)
        {
            return "Мороз! Одягайтесь тепліше, не забудьте рукавички та шапку.";
        }

        // Вітр/Вологість
        if (windSpeed > 12)
        {
            return "Сильний вітер. Тримайтеся від високих конструкцій.";
        }
        if (humidity > 85)
        {
            return "Висока вологість. Можлива відчутна прохолода.";

        }

        return "Сьогодні сприятливі погодні умови. Гарного дня!";
    }
}

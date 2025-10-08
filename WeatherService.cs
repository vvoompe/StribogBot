using System;
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
        var requestUrl = $"http://api.openweathermap.org/data/2.5/weather?q={city}&appid={_apiKey}&units=metric&lang=ua";
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
        var result = new StringBuilder();
        result.AppendLine($"*Погода в місті {data["name"]}, {data["sys"]["country"]}*");
        result.AppendLine($"*{char.ToUpper(description[0]) + description.Substring(1)}*");
        result.AppendLine();
        result.AppendLine($"🌡️ *Температура*: {data["main"]["temp"]:F1}°C (відчувається як {data["main"]["feels_like"]:F1}°C)");
        result.AppendLine($"💨 *Вітер*: {data["wind"]["speed"]:F1} м/с");
        result.AppendLine($"💧 *Вологість*: {data["main"]["humidity"]}%");
        result.AppendLine();
        result.AppendLine($"💡 *Наша порада*: {GetWeatherAdvice(temp, humidity, windSpeed, description)}");
        result.AppendLine();
        result.AppendLine("_Бажаю Вам гарного дня!_");

        return result.ToString();
    }

    /// <summary>
    /// Генерує ввічливу та детальну пораду на основі погодних умов.
    /// </summary>
    private string GetWeatherAdvice(double temp, int humidity, double windSpeed, string description)
    {
        // --- Спочатку реагуємо на опади ---
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

        // --- Потім на температуру ---
        if (temp > 30)
        {
            return "Сьогодні дуже спекотно. Будь ласка, пийте більше води та намагайтеся перебувати в тіні.";
        }
        if (temp > 22)
        {
            return "Чудова тепла погода! Це гарна нагода для прогулянки, але не забувайте про головний убір.";
        }
        if (temp < 5 && temp >= 0)
        {
            return "Надворі досить прохолодно. Рекомендуємо вдягнути теплу куртку, щоб не змерзнути.";
        }
        if (temp < 0)
        {
            return "Обережно, мороз! Будь ласка, вдягайтеся якомога тепліше, не забудьте про рукавички та шапку.";
        }

        // --- Додаткові поради щодо вітру та вологості ---
        if (windSpeed > 12)
        {
            return "Зверніть увагу на сильний вітер. Будь ласка, тримайтеся подалі від дерев та хитких конструкцій.";
        }
        if (humidity > 85)
        {
            return "Висока вологість, через це може відчуватися прохолодніше. Можливо, варто взяти додаткову кофтинку.";
        }

        return "Сьогодні сприятливі погодні умови. Бажаємо Вам приємно провести час!";
    }
}
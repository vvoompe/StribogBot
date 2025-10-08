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
        // Заголовок дати англомовний, як ви просили
        var headerDate = DateTime.Now.ToString("dd MMMM, dddd HH:mm", CultureInfo.CreateSpecificCulture("en-US"));
        sb.AppendLine(headerDate);
        sb.AppendLine();

        // Поточна погода з більш яскравим форматуванням
        var description = data["weather"][0]["description"].ToString();
        var temp = data["main"]["temp"].Value<double>();
        var feels = data["main"]["feels_like"].Value<double>();
        sb.AppendLine($"🌤️ {Char.ToUpper(description[0]) + description.Substring(1)}");
        sb.AppendLine($"🌡️ Температура: +{temp:0.0}°C  |  Відчувається як +{feels:0.0}°C");
        sb.AppendLine("💨 Вітер: " + GetWindDirectionArrow(data["wind"]["deg"]?.Value<double>() ?? 0) + $" {data["wind"]["speed"].Value<double>():0.0} м/с");
        sb.AppendLine($"💧 Вологість: {data["main"]["humidity"].Value<int>()}%  |  Тиск: {data["main"]["pressure"].Value<int>()} hPa");
        sb.AppendLine(new string('-', 30));

        // Додати пораду для поточних умов
        sb.AppendLine($"💡 Порада: {GetWeatherAdvice(temp, data["main"]["humidity"].Value<int>(), data["wind"]["speed"].Value<double>(), description)}");
        sb.AppendLine(new string('-', 30));

        // Очікувані опади (найближчий проміжок) та 2-годинні проміжки для сьогодні
        string nextRainStr = null;
        if (forecastData?["list"] is JArray list)
        {
            // 2-годинні проміжки для сьогодні
            sb.AppendLine("📅 Прогноз на сьогодні (2-годинні проміжки):");
            int cityOffset = data["timezone"].Value<int>();
            DateTime todayCity = DateTimeOffset.FromUnixTimeSeconds(0).DateTime; // placeholder
            var nowCity = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromSeconds(cityOffset)).DateTime;
            todayCity = new DateTime(nowCity.Year, nowCity.Month, nowCity.Day);

            // побудова мапи: windowLabel -> опис
            var todayWindows = new Dictionary<string, string>();
            var todayWindowSet = new HashSet<string>(); // для уникнення дублікатів

            foreach (var item in list)
            {
                var dt = item["dt"].Value<long>();
                var local = DateTimeOffset.FromUnixTimeSeconds(dt).ToOffset(TimeSpan.FromSeconds(cityOffset)).DateTime;
                if (local.Date != todayCity.Date) continue;

                int windowIndex = (local.Hour / 2);
                var windowStart = new DateTime(local.Year, local.Month, local.Day, windowIndex * 2, 0, 0);
                var windowEnd = windowStart.AddHours(2);
                var windowLabel = $"{windowStart:HH:mm}–{windowEnd:HH:mm}";

                // описание и иконка
                var pop = item["pop"]?.Value<double>() ?? 0;
                var desc = item["weather"][0]["description"].ToString();
                var emoji = GetWeatherEmoji(desc, pop);

                if (!todayWindows.ContainsKey(windowLabel))
                    todayWindows[windowLabel] = $"{emoji} {desc}";
                else
                    todayWindows[windowLabel] = todayWindows[windowLabel] + " " + $"{emoji} {desc}";
            }

            foreach (var w in todayWindows)
            {
                sb.AppendLine($"{w.Key}: {w.Value}");
            }

            // одна общая порада для сьогодні
            var todayTemp = data["main"]["temp"].Value<double>();
            var todayHumidity = data["main"]["humidity"].Value<int>();
            var todayWind = data["wind"]["speed"].Value<double>();
            var todayDesc = description;
            var todayAdvice = GetWeatherAdvice(todayTemp, todayHumidity, todayWind, todayDesc);
            sb.AppendLine();
            sb.AppendLine($"💡 Порада дня: {todayAdvice}");
            sb.AppendLine(new string('-', 30));
        }

        // 5-дневний прогноз: компактно по днях
        sb.AppendLine("📆 Прогноз на 5 днів:");
        if (forecastData?["list"] is JArray fl)
        {
            var cityOffset = data["timezone"].Value<int>();

            // згрупувати за днем (city local time)
            var daily = new Dictionary<DateTime, List<JToken>>();
            foreach (var fi in fl)
            {
                var dt = fi["dt"].Value<long>();
                var local = DateTimeOffset.FromUnixTimeSeconds(dt).ToOffset(TimeSpan.FromSeconds(cityOffset)).DateTime;
                var dayKey = new DateTime(local.Year, local.Month, local.Day);
                if (!daily.ContainsKey(dayKey)) daily[dayKey] = new List<JToken>();
                daily[dayKey].Add(fi);
            }

            // Сортуємо дні і виводимо п'ять днів
            var days = new List<DateTime>(daily.Keys);
            days.Sort();
            int printedDays = 0;
            foreach (var day in days)
            {
                if (printedDays >= 5) break;
                var items = daily[day];
                // мін./макс. температура за день
                double minTemp = double.MaxValue, maxTemp = double.MinValue;
                int sumHumidity = 0;
                double sumWind = 0;
                int count = 0;
                var descCounts = new Dictionary<string, int>();
                foreach (var it in items)
                {
                    var t = it["main"]["temp"].Value<double>();
                    var u = it["main"]["humidity"].Value<int>();
                    var w = it["wind"]["speed"].Value<double>();
                    minTemp = Math.Min(minTemp, t);
                    maxTemp = Math.Max(maxTemp, t);
                    sumHumidity += u;
                    sumWind += w;
                    count++;

                    var d = it["weather"][0]["description"].ToString();
                    if (!descCounts.ContainsKey(d)) descCounts[d] = 0;
                    descCounts[d]++;
                }

                // опис дня - найчастіше зустрічається
                string dailyDesc = "Хмарно";
                int maxCount = 0;
                foreach (var kv in descCounts)
                {
                    if (kv.Value > maxCount)
                    {
                        maxCount = kv.Value;
                        dailyDesc = kv.Key;
                    }
                }
                string dailyEmoji = GetWeatherEmoji(dailyDesc, 0);

                double avgHumidity = count > 0 ? sumHumidity / (double)count : 0;
                double avgWind = count > 0 ? sumWind / count : 0;

                sb.AppendLine($"{day:yyyy-MM-dd} ({day:dddd}): {dailyEmoji} {dailyDesc} | Tmin: {minTemp:0.0}°C, Tmax: {maxTemp:0.0}°C | Вітри: {GetWindDirectionArrow(0)} {avgWind:0.0} м/с | Вологість: {avgHumidity:0.0}%");

                printedDays++;
            }
        }

        sb.AppendLine("_Гарного дня!_");
        return sb.ToString();
    }

    public async Task<string> GetTodayForecastAsync(string city)
    {
        // Проста версія сьогоднішнього прогнозу: 2-годинні окремо з описом та порадою
        var todayForecastUrl = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_apiKey}&units=metric&lang=ua";
        var resp = await _httpClient.GetAsync(todayForecastUrl);
        if (!resp.IsSuccessStatusCode)
            throw new Exception("Не вдалося отримати сьогоднішній прогноз.");

        var json = await resp.Content.ReadAsStringAsync();
        var data = JObject.Parse(json);
        var forecastList = data["list"] as JArray;
        var cityName = data["city"]["name"].ToString();
        var country = data["city"]["country"].ToString();
        int cityOffset = data["city"]["timezone"].Value<int>();

        // заголовок
        var sb = new StringBuilder();
        sb.AppendLine($"{cityName}, {country}");
        var headerDate = DateTime.Now.ToString("dd MMMM, dddd HH:mm", CultureInfo.CreateSpecificCulture("en-US"));
        sb.AppendLine(headerDate);
        sb.AppendLine();

        // сьогодняшні 2-годинні блоки
        // визначаємо поточний день в локальній для міста TZ часі
        var nowCity = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromSeconds(cityOffset)).DateTime;
        var today = new DateTime(nowCity.Year, nowCity.Month, nowCity.Day);

        var todayWindows = new Dictionary<string, string>();
        foreach (var item in forecastList)
        {
            var dt = item["dt"].Value<long>();
            var local = DateTimeOffset.FromUnixTimeSeconds(dt).ToOffset(TimeSpan.FromSeconds(cityOffset)).DateTime;
            if (local.Date != today.Date) continue;

            int windowIndex = (local.Hour / 2) * 2;
            var windowStart = new DateTime(local.Year, local.Month, local.Day, windowIndex, 0, 0);
            var windowEnd = windowStart.AddHours(2);
            var label = $"{windowStart:HH:mm}–{windowEnd:HH:mm}";

            var pop = item["pop"]?.Value<double>() ?? 0;
            var desc = item["weather"][0]["description"].ToString();
            var emoji = GetWeatherEmoji(desc, pop);

            if (!todayWindows.ContainsKey(label))
                todayWindows[label] = $"{emoji} {desc}";
            else
                todayWindows[label] = todayWindows[label] + " " + $"{emoji} {desc}";
        }

        foreach (var w in todayWindows)
        {
            sb.AppendLine($"{w.Key}: {w.Value}");
        }

        // порада разом
        var currentTemp = data["list"][0]["main"]["temp"].Value<double>();
        var humidity = data["list"][0]["main"]["humidity"].Value<int>();
        var wind = data["list"][0]["wind"]["speed"].Value<double>();
        var currentDesc = data["list"][0]["weather"][0]["description"].ToString();
        sb.AppendLine();
        sb.AppendLine($"💡 Порада: {GetWeatherAdvice(currentTemp, humidity, wind, currentDesc)}");
        sb.AppendLine();

        return sb.ToString();
    }

    private string GetWindDirectionArrow(double deg)
    {
        // 8 напрямків
        string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
        int idx = (int)((deg / 45) + 0.5) % 8;
        return arrows[idx];
    }

    private string GetWeatherEmoji(string description, double pop)
    {
        // префікс емодзі залежно від опису та ймовірності дощів
        string baseDesc = description.ToLower();
        if (baseDesc.Contains("гроза")) return "⛈️";
        if (pop > 0) return "🌧️";
        if (baseDesc.Contains("дощ") || baseDesc.Contains("мряка")) return "🌧️";
        if (baseDesc.Contains("сніг")) return "❄️";
        if (baseDesc.Contains("хмар") || baseDesc.Contains("хмарність")) return "☁️";
        if (baseDesc.Contains("ясн") || baseDesc.Contains("сонячно") || baseDesc.Contains("сонячна")) return "☀️";
        return "🌤️";
    }

    private string GetWeatherAdvice(double temp, int humidity, double windSpeed, string description)
    {
        if (description.Contains("гроза"))
        {
            return "Будь ласка, будьте обережні. Радимо залишатися вдома під час грози.";
        }
        if (description.Contains("дощ") || description.Contains("мряка"))
        {
            return "Парасолька потрібна сьогодні. Не забудьте її!";
        }
        if (description.Contains("сніг"))
        {
            return "Одягніться тепло, взуття із протектором.";
        }

        // Температура
        if (temp > 30) return "Сьогодні дуже спекотно. Пийте багато води і залишайтеся в тіні.";
        if (temp > 22) return "Чудова тепла погода! Вдалий день для активності на свіжому повітрі.";
        if (temp < 0) return "Мороз! Одягайтесь тепліше.";
        if (temp < 5) return "Прохолодно. Радимо вдягнутися тепліше.";

        if (windSpeed > 12) return "Сильний вітер. Тримайтеся подалі від дерев.";
        if (humidity > 85) return "Висока вологість. Можлива легка втома; попийте води.";

        return "Сьогодні сприятливі погодні умови. Гарного дня!";
    }
}

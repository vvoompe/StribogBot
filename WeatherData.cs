using Newtonsoft.Json; // Обов'язково додаємо для атрибутів JsonProperty

namespace WeatherBot
{
    // Цей клас відповідає загальній структурі JSON-відповіді від OpenWeatherMap
    public class OpenWeatherResponse
    {
        [JsonProperty("name")] // Атрибут JsonProperty вказує, яке поле в JSON відповідає цій властивості
        public string CityName { get; set; }

        [JsonProperty("main")]
        public MainWeatherData Main { get; set; }

        [JsonProperty("weather")]
        public WeatherDescription[] Weather { get; set; } // Це масив, бо може бути кілька описів (напр. "дощ" і "хмарно")

        [JsonProperty("wind")]
        public WindData Wind { get; set; }

        [JsonProperty("sys")]
        public SystemData Sys { get; set; }
    }

    // Клас для основних даних (температура, тиск, вологість)
    public class MainWeatherData
    {
        [JsonProperty("temp")]
        public double Temperature { get; set; }

        [JsonProperty("feels_like")]
        public double FeelsLike { get; set; }

        [JsonProperty("temp_min")]
        public double TempMin { get; set; }

        [JsonProperty("temp_max")]
        public double TempMax { get; set; }

        [JsonProperty("pressure")]
        public int Pressure { get; set; }

        [JsonProperty("humidity")]
        public int Humidity { get; set; }
    }

    // Клас для опису погоди (основний опис, детальний опис, іконка)
    public class WeatherDescription
    {
        [JsonProperty("main")]
        public string MainDescription { get; set; } // Напр. "Clouds"

        [JsonProperty("description")]
        public string DetailedDescription { get; set; } // Напр. "overcast clouds"

        [JsonProperty("icon")]
        public string Icon { get; set; } // Код іконки погоди
    }

    // Клас для даних про вітер
    public class WindData
    {
        [JsonProperty("speed")]
        public double Speed { get; set; } // Швидкість вітру
    }

    // Клас для системних даних (схід/захід сонця, країна)
    public class SystemData
    {
        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("sunrise")]
        public long Sunrise { get; set; } // Unix timestamp

        [JsonProperty("sunset")]
        public long Sunset { get; set; }  // Unix timestamp
    }
    public class ForecastResponse
    {
        [JsonProperty("list")]
        public List<ForecastEntry> Forecasts { get; set; }

        // Клас для одного запису прогнозу (на кожні 3 години)
        public class ForecastEntry
        {
            // Дата і час прогнозу у текстовому вигляді (напр., "2025-10-02 12:00:00")
            [JsonProperty("dt_txt")]
            public string ForecastTimeText { get; set; }

            [JsonProperty("main")]
            public MainWeatherData Main { get; set; }

            [JsonProperty("weather")]
            public WeatherDescription[] Weather { get; set; }

            [JsonProperty("wind")]
            public WindData Wind { get; set; }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Npgsql;
using System.Threading.Tasks;

namespace Stribog
{
    public class UserSettingsService
    {
        private readonly string _connectionString;

        public UserSettingsService()
        {
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            
            if (string.IsNullOrEmpty(databaseUrl))
            {
                throw new InvalidOperationException("DATABASE_URL не встановлено.");
            }

            _connectionString = BuildConnectionString(databaseUrl);
        }

        public static string BuildConnectionString(string databaseUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(databaseUrl) || databaseUrl.StartsWith("${{"))
                {
                    throw new InvalidOperationException("DATABASE_URL має неправильний формат або не встановлено.");
                }

                var uri = new Uri(databaseUrl);
                var userInfo = uri.UserInfo.Split(':');

                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 5432,
                    Username = userInfo[0],
                    Password = userInfo[1],
                    Database = uri.AbsolutePath.TrimStart('/'),
                    SslMode = SslMode.Require,
                    TrustServerCertificate = true
                };

                return builder.ConnectionString;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB ERROR] Помилка розбору DATABASE_URL: '{databaseUrl}'. Повідомлення: {ex.Message}");
                throw new InvalidOperationException($"Не вдалося розібрати DATABASE_URL.", ex);
            }
        }

        public UserSetting GetUserSettings(long chatId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            var result = conn.QueryFirstOrDefault<UserSetting>(
                "SELECT * FROM usersettings WHERE chatid = @chatId",
                new { chatId });
            
            return result ?? new UserSetting { ChatId = chatId };
        }

        public void SaveUserSettings(UserSetting setting)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Execute(@"
                INSERT INTO usersettings (chatid, city, dailyweatherbroadcast, broadcastcity, broadcasttime, timezoneid, lastbroadcastsentutc)
                VALUES (@ChatId, @City, @DailyWeatherBroadcast, @BroadcastCity, @BroadcastTime, @TimeZoneId, @LastBroadcastSentUtc)
                ON CONFLICT (chatid) DO UPDATE SET
                    city = EXCLUDED.city,
                    dailyweatherbroadcast = EXCLUDED.dailyweatherbroadcast,
                    broadcastcity = EXCLUDED.broadcastcity,
                    broadcasttime = EXCLUDED.broadcasttime,
                    timezoneid = EXCLUDED.timezoneid,
                    lastbroadcastsentutc = EXCLUDED.lastbroadcastsentutc,
                    updatedat = CURRENT_TIMESTAMP;",
                setting);
        }

        public List<UserSetting> GetAllSettings()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            return conn.Query<UserSetting>("SELECT * FROM usersettings WHERE dailyweatherbroadcast = TRUE").ToList();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Npgsql;

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
                throw new InvalidOperationException("DATABASE_URL not set");
            }

            _connectionString = BuildConnectionString(databaseUrl);
        }

        private static string BuildConnectionString(string databaseUrl)
        {
            try
            {
                // Railway може надавати URL у форматі postgres:// або postgresql://
                if (databaseUrl.StartsWith("postgres://"))
                {
                    databaseUrl = databaseUrl.Replace("postgres://", "postgresql://");
                }

                // Якщо це вже готовий connection string (не URL)
                if (!databaseUrl.StartsWith("postgresql://"))
                {
                    // Можливо це вже connection string у форматі Npgsql
                    return databaseUrl;
                }

                var uri = new Uri(databaseUrl);
                var userInfo = uri.UserInfo.Split(':');

                if (userInfo.Length != 2)
                {
                    throw new InvalidOperationException("Invalid DATABASE_URL format: missing username or password");
                }

                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : 5432;
                var database = uri.AbsolutePath.TrimStart('/');
                var username = Uri.UnescapeDataString(userInfo[0]);
                var password = Uri.UnescapeDataString(userInfo[1]);

                // Railway PostgreSQL зазвичай вимагає SSL
                var connString = $"Host={host};Port={port};Username={username};Password={password};Database={database};SSL Mode=Require;Trust Server Certificate=true";

                return connString;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse DATABASE_URL: {ex.Message}", ex);
            }
        }

        public UserSetting GetUserSettings(long chatId)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                var result = conn.QueryFirstOrDefault<UserSetting>(
                    "SELECT * FROM usersettings WHERE chatid = @chatId",
                    new { chatId });
                
                return result ?? new UserSetting { ChatId = chatId };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting user settings for {chatId}: {ex.Message}");
                return new UserSetting { ChatId = chatId };
            }
        }

        public void SaveUserSettings(UserSetting setting)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                conn.Execute(@"
                    INSERT INTO usersettings (chatid, city, dailyweatherbroadcast, broadcastcity, broadcasttime, timezoneid)
                    VALUES (@ChatId, @City, @DailyWeatherBroadcast, @BroadcastCity, @BroadcastTime, @TimeZoneId)
                    ON CONFLICT (chatid) DO UPDATE SET
                        city = EXCLUDED.city,
                        dailyweatherbroadcast = EXCLUDED.dailyweatherbroadcast,
                        broadcastcity = EXCLUDED.broadcastcity,
                        broadcasttime = EXCLUDED.broadcasttime,
                        timezoneid = EXCLUDED.timezoneid,
                        updatedat = CURRENT_TIMESTAMP;",
                    setting);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving user settings for {setting.ChatId}: {ex.Message}");
                throw;
            }
        }

        public List<UserSetting> GetAllSettings()
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                return conn.Query<UserSetting>("SELECT * FROM usersettings WHERE dailyweatherbroadcast = TRUE").ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting all settings: {ex.Message}");
                return new List<UserSetting>();
            }
        }
    }
}
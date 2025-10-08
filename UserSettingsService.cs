using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Npgsql;

namespace Stribog;

public class UserSettingsService
{
    private readonly string _connectionString;

    public UserSettingsService()
    {
        var rawUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("DATABASE_URL not set");

        _connectionString = BuildConnectionString(rawUrl);
    }

    private static string BuildConnectionString(string databaseUrl)
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':');

        return $"Host={uri.Host};Port={uri.Port};Username={userInfo[0]};Password={userInfo[1]};Database={uri.AbsolutePath.TrimStart('/')};SSL Mode=Require;Trust Server Certificate=true";
    }

    public UserSetting GetUserSettings(long chatId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        return conn.QueryFirstOrDefault<UserSetting>(
            "SELECT * FROM usersettings WHERE chatid = @chatId",
            new { chatId }) ?? new UserSetting { ChatId = chatId };
    }

    public void SaveUserSettings(UserSetting setting)
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
                timezoneid = EXCLUDED.timezoneid;",
            setting);
    }

    public List<UserSetting> GetAllSettings()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        return conn.Query<UserSetting>("SELECT * FROM usersettings").ToList();
    }
}

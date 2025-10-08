using Npgsql;
using Dapper;
namespace Stribog;

public class UserSettingsService
{
    private readonly string _connectionString;

    public UserSettingsService()
    {
        _connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                            ?? throw new InvalidOperationException("DATABASE_URL not set");
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
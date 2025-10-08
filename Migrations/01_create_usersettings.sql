-- Migrations/01_create_usersettings.sql
-- Створення таблиці налаштувань користувачів

CREATE TABLE IF NOT EXISTS usersettings (
    chatid BIGINT PRIMARY KEY,
    city VARCHAR(255),
    dailyweatherbroadcast BOOLEAN DEFAULT FALSE,
    broadcastcity VARCHAR(255),
    broadcasttime VARCHAR(10),
    timezoneid VARCHAR(100),
    createdat TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updatedat TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Створення індексу для швидкого пошуку активних розсилок
CREATE INDEX IF NOT EXISTS idx_daily_broadcast 
ON usersettings(dailyweatherbroadcast) 
WHERE dailyweatherbroadcast = TRUE;

-- Тригер для оновлення updatedat
CREATE OR REPLACE FUNCTION update_updatedat_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updatedat = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS update_usersettings_updatedat ON usersettings;

CREATE TRIGGER update_usersettings_updatedat
    BEFORE UPDATE ON usersettings
    FOR EACH ROW
    EXECUTE FUNCTION update_updatedat_column();

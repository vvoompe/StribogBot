CREATE TABLE IF NOT EXISTS usersettings (
    chatid BIGINT PRIMARY KEY,
    city VARCHAR(255),
    dailyweatherbroadcast BOOLEAN DEFAULT FALSE,
    broadcastcity VARCHAR(255),
    broadcasttime VARCHAR(10),
    timezoneid VARCHAR(100),
    createdat TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updatedat TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    -- НОВА КОЛОНКА
    lastbroadcastsentutc TIMESTAMP WITH TIME ZONE
);

-- Додаємо колонку, якщо таблиця вже існує
DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='usersettings' AND column_name='lastbroadcastsentutc') THEN
        ALTER TABLE usersettings ADD COLUMN lastbroadcastsentutc TIMESTAMP WITH TIME ZONE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_daily_broadcast 
ON usersettings(dailyweatherbroadcast) 
WHERE dailyweatherbroadcast = TRUE;

// See https://aka.ms/new-console-template for more information

using EmulatorApp;
using Npgsql;

class Program
{
    private static List<ChannelConfig> _channelConfigs = new List<ChannelConfig>();
    private static int _periodMs = 5000;
    
    static async Task Main(string[] args)
    {
        var _dbConfig  = LoadDatabaseConfigFromTextFile();
        
        var connectionString = $"Host={_dbConfig .Host};Port={_dbConfig .Port};Database={_dbConfig .Database};Username={_dbConfig .Username};Password={_dbConfig .Password}";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Загрузка конфигурации каналов
        _channelConfigs = LoadChannelConfigs("channels_config.txt");
        if (_channelConfigs.Count == 0)
        {
            Console.WriteLine("Не загружено ни одного канала. Завершение работы.");
            return;
        }

        // Загрузка периода из app.txt
        LoadPeriodFromAppConfig("app.txt");
        
        await Loop(connection);

        Console.WriteLine("Данные успешно добавлены!");
    }

    private async static Task Loop(NpgsqlConnection connection)
    {
        var random = new Random();
        
        while (true)
        {
            var tasks = new List<Task>();
            
            foreach (var channel in _channelConfigs)
            {
                var val = random.NextDouble() * (channel.Max - channel.Min) + channel.Min;
                val = Math.Round(val, 2); // Округляем до 2 знаков после запятой
                
                tasks.Add(InsertData(connection, channel.Id, DateTime.Now.ToUniversalTime(), val));
                tasks.Add(UpdateLastInfo(connection, channel.Id, val, DateTime.Now.ToUniversalTime()));
            }
            
            // Ожидаем завершения всех операций с БД
            await Task.WhenAll(tasks);
            
            Thread.Sleep(_periodMs);
        }
    }
    
    private static async Task InsertData(NpgsqlConnection connection, int id, DateTime date, double num)
    {
        try
        {
            const string insertSql = @"
            INSERT INTO data (channelid,timevalue,value)
            VALUES (@channelid, @timevalue, @value);";

            await using var cmd = new NpgsqlCommand(insertSql, connection);
            cmd.Parameters.AddWithValue("channelid", id);
            cmd.Parameters.AddWithValue("timevalue", date);
            cmd.Parameters.AddWithValue("value", num);

            await cmd.ExecuteNonQueryAsync();

            Console.WriteLine($"Данные успешно добавлены!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при добавлении данных в таблицу data!");
        }
    }

    private static async Task UpdateLastInfo(NpgsqlConnection connection, int channelId, double value, DateTime timevalue)
    {
        try
        {
            // Используем параметризованный запрос для безопасности
            var updateSql = $"UPDATE latestinfo SET value = @value, timevalue = @timevalue  WHERE channelid = @channelid";

            await using var cmd = new NpgsqlCommand(updateSql, connection);
            cmd.Parameters.AddWithValue("value", value);
            cmd.Parameters.AddWithValue("timevalue", timevalue);
            cmd.Parameters.AddWithValue("channelid", channelId);

            var affectedRows = await cmd.ExecuteNonQueryAsync();

            if (affectedRows > 0)
                Console.WriteLine($"Таблица LatestInfo успешно обновлена");
            else
                Console.WriteLine("Данные в таблице LatestInfo не изменились");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обновлении поля: {ex.Message}");
        }
    }

    // Альтернативный метод для чтения из простого текстового файла (формат: ключ=значение)
    private static DatabaseConfig LoadDatabaseConfigFromTextFile()
    {
        var configFile = "db_config.txt";

        try
        {
            if (!File.Exists(configFile))
            {
                Console.WriteLine("Не найден файл подключеия к базе данных");
                return null;
            }

            var config = new DatabaseConfig();
            var lines = File.ReadAllLines(configFile);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Пропускаем пустые строки и комментарии
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                var parts = trimmedLine.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    switch (key.ToLower())
                    {
                        case "host":
                            config.Host = value;
                            break;
                        case "port":
                            if (int.TryParse(value, out int port))
                                config.Port = port;
                            break;
                        case "database":
                            config.Database = value;
                            break;
                        case "username":
                            config.Username = value;
                            break;
                        case "password":
                            config.Password = value;
                            break;
                    }
                }
            }

            Console.WriteLine("Конфигурация загружена из db_config.txt");
            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке конфигурации из текстового файла: {ex.Message}");
            return null;
        }
    }
    
    // Загрузка конфигурации каналов
    private static List<ChannelConfig> LoadChannelConfigs(string filePath)
    {
        var channels = new List<ChannelConfig>();

        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Не найден файл конфигурации каналов: {filePath}");
                return channels;
            }

            var lines = File.ReadAllLines(filePath);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Пропускаем пустые строки и комментарии (как #, так и //)
                if (string.IsNullOrEmpty(trimmedLine) || 
                    trimmedLine.StartsWith("#") || 
                    trimmedLine.StartsWith("//"))
                    continue;

                var parts = trimmedLine.Split('|');
                if (parts.Length == 3)
                {
                    if (int.TryParse(parts[0].Trim(), out int id) &&
                        double.TryParse(parts[1].Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double min) &&
                        double.TryParse(parts[2].Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double max))
                    {
                        channels.Add(new ChannelConfig { Id = id, Min = min, Max = max });
                        Console.WriteLine($"Загружен канал: ID={id}, Min={min}, Max={max}");
                    }
                    else
                    {
                        Console.WriteLine($"Ошибка разбора строки: {line}");
                    }
                }
                else
                {
                    Console.WriteLine($"Неверный формат строки: {line}. Ожидается: id|min|max");
                }
            }

            Console.WriteLine($"Загружено каналов: {channels.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке конфигурации каналов: {ex.Message}");
        }

        return channels;
    }
    
    // Загрузка периода из app.txt
    private static void LoadPeriodFromAppConfig(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Не найден файл конфигурации приложения: {filePath}. Используется период по умолчанию: {_periodMs}мс");
                return;
            }

            var lines = File.ReadAllLines(filePath);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Пропускаем пустые строки и комментарии
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                if (trimmedLine.StartsWith("Period=", StringComparison.OrdinalIgnoreCase))
                {
                    var periodStr = trimmedLine.Substring(7).Trim();
                    if (int.TryParse(periodStr, out int period))
                    {
                        _periodMs = period;
                        Console.WriteLine($"Установлен период: {_periodMs}мс");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"Ошибка разбора периода: {periodStr}. Используется период по умолчанию: {_periodMs}мс");
                    }
                }
            }

            Console.WriteLine($"Период не найден в конфигурации. Используется период по умолчанию: {_periodMs}мс");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке конфигурации приложения: {ex.Message}. Используется период по умолчанию: {_periodMs}мс");
        }
    }
}

// Класс для хранения конфигурации
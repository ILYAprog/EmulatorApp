// See https://aka.ms/new-console-template for more information

using EmulatorApp;
using Npgsql;

class Program
{
    private static List<ChannelConfig> _channelConfigs = new();
    private static int _periodMs = 5000;
    private static DatabaseConfig _dbConfig;

    static async Task Main(string[] args)
    {
        _dbConfig = LoadDatabaseConfigFromTextFile();

        // Загрузка конфигурации каналов
        _channelConfigs = LoadChannelConfigs("channels_config.txt");
        if (_channelConfigs.Count == 0)
        {
            Console.WriteLine("Не загружено ни одного канала. Завершение работы.");
            return;
        }

        // Загрузка периода из app.txt
        LoadPeriodFromAppConfig("app.txt");

        await Loop();

        Console.WriteLine("Завершение работы!");
    }

    private async static Task Loop()
    {
        var random = new Random();

        while (true)
        {
            var tasks = new List<Task>();

            foreach (var channel in _channelConfigs)
            {
                var channelCopy = channel; // Важно: создаем копию для замыкания
                tasks.Add(ProcessChannel(channelCopy, random));
            }

            // Ожидаем завершения всех операций с БД
            await Task.WhenAll(tasks);

            Console.WriteLine($"Все операции успешно завершены. Ожидание {_periodMs}мс...");
            await Task.Delay(_periodMs);
        }
    }

    private static async Task ProcessChannel(ChannelConfig channel, Random random)
    {
        var connectionString =
            $"Host={_dbConfig.Host};Port={_dbConfig.Port};Database={_dbConfig.Database};Username={_dbConfig.Username};Password={_dbConfig.Password}";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        double valueToStore;

        if (channel.Type == ChannelType.Boolean)
        {
            // Обработка булевого канала
            bool newValue = CalculateBooleanValue(channel, random);
            channel.CurrentBoolValue = newValue;
            valueToStore = newValue ? 1.0 : 0.0; // Конвертируем в число для хранения в БД
            Console.WriteLine($"Булев канал {channel.Id}: значение = {newValue}");
        }
        else
        {
            // Обработка числового канала
            double newValue = CalculateSmoothValue(channel, random);
            channel.CurrentValue = newValue;
            valueToStore = newValue;
        }

        await InsertData(connection, channel.Id, DateTime.Now.ToUniversalTime(), valueToStore);
        await UpdateLastInfo(connection, channel.Id, valueToStore, DateTime.Now.ToUniversalTime());
    }

    private static bool CalculateBooleanValue(ChannelConfig channel, Random random)
    {
        bool currentValue = channel.CurrentBoolValue;

        // Проверяем, нужно ли переключить значение на основе вероятности канала
        if (random.NextDouble() < channel.ToggleProbability)
        {
            bool newValue = !currentValue;
            Console.WriteLine($"Булев канал {channel.Id}: переключен {currentValue} -> {newValue} (вероятность: {channel.ToggleProbability:P0})");
            return newValue;
        }

        // Если переключения не произошло, возвращаем текущее значение
        return currentValue;
    }

    private static double CalculateSmoothValue(ChannelConfig channel, Random random)
    {
        // Проверяем, нужно ли сменить цель на основе вероятности канала
        if (random.NextDouble() < channel.ChangeProbability)
        {
            channel.TargetValue = random.NextDouble() * (channel.Max - channel.Min) + channel.Min;
            Console.WriteLine($"Числовой канал {channel.Id}: цель изменена на {channel.TargetValue:F2} (вероятность: {channel.ChangeProbability:P0})");
        }

        // Двигаемся к цели с заданным шагом
        double difference = channel.TargetValue - channel.CurrentValue;
        double movement = Math.Sign(difference) * Math.Min(Math.Abs(difference), channel.Step);

        double newValue = channel.CurrentValue + movement;
        newValue = Math.Max(channel.Min, Math.Min(channel.Max, newValue));

        return Math.Round(newValue, 2);
    }

    private static async Task InsertData(NpgsqlConnection connection, int id, DateTime date, double num)
    {
        try
        {
            const string insertSql = @"
            INSERT INTO data (channelid, timevalue, value)
            VALUES (@channelid, @timevalue, @value);";

            await using var cmd = new NpgsqlCommand(insertSql, connection);
            cmd.Parameters.AddWithValue("channelid", id);
            cmd.Parameters.AddWithValue("timevalue", date);
            cmd.Parameters.AddWithValue("value", num);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при добавлении данных в таблицу data для канала {id}: {ex.Message}");
        }
    }

    private static async Task UpdateLastInfo(NpgsqlConnection connection, int channelId, double value,
        DateTime timevalue)
    {
        try
        {
            var updateSql =
                $"UPDATE latestinfo SET value = @value, timevalue = @timevalue WHERE channelid = @channelid";

            await using var cmd = new NpgsqlCommand(updateSql, connection);
            cmd.Parameters.AddWithValue("value", value);
            cmd.Parameters.AddWithValue("timevalue", timevalue);
            cmd.Parameters.AddWithValue("channelid", channelId);

            var affectedRows = await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обновлении поля для канала {channelId}: {ex.Message}");
        }
    }

    private static List<ChannelConfig> LoadChannelConfigs(string filePath)
    {
        var channels = new List<ChannelConfig>();
        var random = new Random();

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

                if (string.IsNullOrEmpty(trimmedLine) ||
                    trimmedLine.StartsWith("#") ||
                    trimmedLine.StartsWith("//"))
                    continue;

                var parts = trimmedLine.Split('|');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0].Trim(), out int id))
                    {
                        // Определяем тип канала по количеству параметров
                        if (parts.Length == 2) // Булев канал: id|toggleProbability
                        {
                            // Булев канал
                            var channel = new ChannelConfig
                            {
                                Id = id,
                                Type = ChannelType.Boolean,
                                CurrentBoolValue = random.Next(2) == 1 // Случайное начальное значение
                            };

                            // Парсим вероятность переключения
                            if (double.TryParse(parts[1].Trim().Replace(',', '.'),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out double toggleProb))
                            {
                                channel.ToggleProbability = Math.Max(0, Math.Min(1, toggleProb));
                            }

                            channels.Add(channel);
                            Console.WriteLine(
                                $"Загружен БУЛЕВ канал: ID={id}, ToggleProbability={channel.ToggleProbability:P0}, StartValue={channel.CurrentBoolValue}");
                        }
                        else if (parts.Length >= 3) // Числовой канал: id|min|max|step|probability
                        {
                            // Числовой канал
                            if (double.TryParse(parts[1].Trim().Replace(',', '.'),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out double min) &&
                                double.TryParse(parts[2].Trim().Replace(',', '.'),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out double max))
                            {
                                var channel = new ChannelConfig
                                {
                                    Id = id,
                                    Type = ChannelType.Numeric,
                                    Min = min,
                                    Max = max
                                };

                                // Инициализируем начальное и целевое значение
                                channel.CurrentValue = (min + max) / 2;
                                channel.TargetValue = random.NextDouble() * (max - min) + min;

                                // Если указан шаг, используем его (4-й параметр)
                                if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]) &&
                                    double.TryParse(parts[3].Trim().Replace(',', '.'),
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out double step))
                                {
                                    channel.Step = Math.Abs(step);
                                }

                                // Если указана вероятность, используем её (5-й параметр)
                                if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]) &&
                                    double.TryParse(parts[4].Trim().Replace(',', '.'),
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out double probability))
                                {
                                    channel.ChangeProbability = Math.Max(0, Math.Min(1, probability));
                                }

                                channels.Add(channel);
                                Console.WriteLine(
                                    $"Загружен ЧИСЛОВОЙ канал: ID={id}, Min={min}, Max={max}, Step={channel.Step}, ChangeProbability={channel.ChangeProbability:P0}, StartValue={channel.CurrentValue:F2}");
                            }
                            else
                            {
                                Console.WriteLine($"Ошибка разбора числового канала: {line}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Неверное количество параметров для канала: {line}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Ошибка разбора ID канала: {line}");
                    }
                }
                else
                {
                    Console.WriteLine(
                        $"Неверный формат строки: {line}. Ожидается: для числовых - id|min|max|step|probability, для булевых - id|toggleProbability");
                }
            }

            Console.WriteLine(
                $"Загружено каналов: {channels.Count} (числовых: {channels.Count(c => c.Type == ChannelType.Numeric)}, булевых: {channels.Count(c => c.Type == ChannelType.Boolean)})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке конфигурации каналов: {ex.Message}");
        }

        return channels;
    }

    private static void LoadPeriodFromAppConfig(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine(
                    $"Не найден файл конфигурации приложения: {filePath}. Используется период по умолчанию: {_periodMs}мс");
                return;
            }

            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (string.IsNullOrEmpty(trimmedLine) ||
                    trimmedLine.StartsWith("#") ||
                    trimmedLine.StartsWith("//"))
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
                        Console.WriteLine(
                            $"Ошибка разбора периода: {periodStr}. Используется период по умолчанию: {_periodMs}мс");
                    }
                }
            }

            Console.WriteLine($"Период не найден в конфигурации. Используется период по умолчанию: {_periodMs}мс");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Ошибка при загрузке конфигурации приложения: {ex.Message}. Используется период по умолчанию: {_periodMs}мс");
        }
    }

    private static DatabaseConfig LoadDatabaseConfigFromTextFile()
    {
        var configFile = "db_config.txt";

        try
        {
            if (!File.Exists(configFile))
            {
                Console.WriteLine("Не найден файл подключения к базе данных");
                return new DatabaseConfig();
            }

            var config = new DatabaseConfig();
            var lines = File.ReadAllLines(configFile);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (string.IsNullOrEmpty(trimmedLine) ||
                    trimmedLine.StartsWith("#") ||
                    trimmedLine.StartsWith("//"))
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
            return new DatabaseConfig();
        }
    }
}
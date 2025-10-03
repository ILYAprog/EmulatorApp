public class DatabaseConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "testdb";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "password";
}
namespace EmulatorApp;

public class ChannelConfig
{
    public int Id { get; set; }
    public ChannelType Type { get; set; } = ChannelType.Numeric;
    public double Min { get; set; }
    public double Max { get; set; }
    public double Step { get; set; } = 0.5;
    public double ChangeProbability { get; set; } = 0.3;
    public double ToggleProbability { get; set; } = 0.1;
    public bool CurrentBoolValue { get; set; }
    public double CurrentValue { get; set; }
    public double TargetValue { get; set; }
}

public enum ChannelType
{
    Numeric,
    Boolean
}
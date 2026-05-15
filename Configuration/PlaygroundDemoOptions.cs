namespace QRCodeAPI.Configuration;

public class PlaygroundDemoOptions
{
    public const string SectionName = "PlaygroundDemo";

    public bool Enabled { get; set; }

    public string UserEmail { get; set; } = "seth@ezofis.com";

    public string UserPassword { get; set; } = "";
}

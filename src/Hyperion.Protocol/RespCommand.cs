namespace Hyperion.Protocol;

public class RespCommand
{
    public string Cmd { get; set; } = string.Empty;
    public string[] Args { get; set; } = Array.Empty<string>();
}

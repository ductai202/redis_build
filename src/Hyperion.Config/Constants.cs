namespace Hyperion.Config;

public static class Constants
{
    public static readonly byte[] RespNil = "$-1\r\n"u8.ToArray();
    public static readonly byte[] RespOk = "+OK\r\n"u8.ToArray();
    public static readonly byte[] RespZero = ":0\r\n"u8.ToArray();
    public static readonly byte[] RespOne = ":1\r\n"u8.ToArray();
    public static readonly byte[] TtlKeyNotExist = ":-2\r\n"u8.ToArray();
    public static readonly byte[] TtlKeyExistNoExpire = ":-1\r\n"u8.ToArray();

    public static readonly TimeSpan ActiveExpireFrequency = TimeSpan.FromMilliseconds(100);
    public const int ActiveExpireSampleSize = 20;
    public const double ActiveExpireThreshold = 0.1;

    public const int DefaultBPlusTreeDegree = 4;
    public const int BfDefaultInitCapacity = 100;
    public const double BfDefaultErrRate = 0.01;

    public const int ServerStatusIdle = 1;
    public const int ServerStatusBusy = 2;
    public const int ServerStatusShuttingDown = 3;
}

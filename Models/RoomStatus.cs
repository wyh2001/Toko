namespace Toko.Models
{
    public enum RoomStatus
    {
        Waiting,   // 创建后未开始
        Playing,   // 已开始
        Finished   // 已结束
    }

    public enum PlayingPhase
    {
        Collecting, // 收集卡片
        Executing,  // 执行指令
        Finished    // 已结束
    }

    public enum Trigger { Start, AllPlayersDone, Timeout, ExecutionDone, Finish }

    public enum CollectTrigger { Submit, PlayerTimeout, Reset }
}

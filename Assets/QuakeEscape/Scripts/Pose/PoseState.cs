namespace Collapse
{
    public enum PoseState { None, OneLegLeft, OneLegRight, Tiptoe }

    public interface IPoseSource
    {
        PoseState CurrentPose { get; }
        bool IsTracking { get; }
    }
}

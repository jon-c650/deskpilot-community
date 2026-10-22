namespace Terminals.Connections
{
    // Presentation-only scaling. Must not reconnect or change remote desktop dimensions.
    public interface IViewportScaling
    {
        bool FitToViewport { get; set; }
    }
}

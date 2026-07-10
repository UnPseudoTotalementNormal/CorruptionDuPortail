namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// PBoundByInk's power-local write port: record a slot as an ink target (its current + already-targeted
    /// lists live on the carrier as NGO state). The pure decision only emits the RegisterInkTarget intention.
    /// </summary>
    public interface IInkTargetRegister
    {
        void RegisterInkTarget(int slot);
    }
}

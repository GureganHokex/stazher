namespace InGameSurface
{
    // Demo thresholds, deliberately not a certified measurement implementation.
    public sealed class ExposureGate
    {
        public double ContinuousSeconds { get; private set; }
        public bool Counted { get; private set; }
        public readonly double RequiredSeconds;
        bool previousEligible;
        public ExposureGate(double requiredSeconds = 2, bool counted = false)
        { RequiredSeconds = requiredSeconds; Counted = counted; }
        public bool Tick(bool eligible, double elapsed)
        {
            if (!eligible || elapsed < 0 || elapsed > 0.3)
            { ContinuousSeconds = 0; previousEligible = eligible; return false; }
            if (previousEligible) ContinuousSeconds += elapsed;
            previousEligible = true;
            if (Counted || ContinuousSeconds + 1e-6 < RequiredSeconds) return false;
            Counted = true;
            return true;
        }
        public void Interrupt() { previousEligible = false; ContinuousSeconds = 0; }
    }
}

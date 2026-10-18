namespace TerraWikiOverlay;

internal readonly record struct FloatingBallState(bool Busy, bool Dark, NavigationSnapshot Navigation);
internal readonly record struct FloatingBallFrame(double Time, float Hover, float Press, float Busy,
    float Navigation, float Arrival, float Angle, float Dark, float Reveal, bool ReducedMotion, NavigationSnapshot Target,
    float Drag = 0, float Release = 0, double Orbit = 0);

// Time-based easing keeps the same motion under irregular Windows timer scheduling.
internal sealed class FloatingBallMotion
{
    private FloatingBallFrame _frame;
    private bool _wasDragging;
    public bool Settling { get; private set; }

    public void Reset(FloatingBallState state, bool reducedMotion)
    {
        _wasDragging = false;
        _frame = new(0, 0, 0, state.Busy ? 1 : 0, state.Navigation.Active ? 1 : 0,
            Arrived(state) ? 1 : 0, SafeAngle(state.Navigation), state.Dark ? 1 : 0,
            reducedMotion ? 1 : 0, reducedMotion, state.Navigation);
    }

    public FloatingBallFrame Advance(double seconds, FloatingBallState state, bool hovered, bool pressed, bool reducedMotion, bool dragging = false)
    {
        double dt = double.IsFinite(seconds) ? Math.Max(seconds, 0) : 0;
        float Ease(float from, float to, double duration = .11) => reducedMotion ? to :
            to + (from - to) * (float)Math.Exp(-dt / duration);
        float angle = _frame.Angle;
        if (state.Navigation.Active)
        {
            float delta = MathF.IEEERemainder(SafeAngle(state.Navigation) - angle, MathF.Tau);
            angle += Ease(0, delta, .13);
            angle = MathF.IEEERemainder(angle, MathF.Tau);
        }
        float drag = Ease(_frame.Drag, dragging ? 1 : 0, .075);
        float release = reducedMotion ? 0 : _wasDragging && !dragging ? 1 : _frame.Release * (float)Math.Exp(-dt / .16);
        // Integrate angular velocity so grabbing/releasing never jumps to another point on the orbit.
        double Integral(float from, bool enabled, double duration) => (enabled ? dt : 0) +
            (from - (enabled ? 1 : 0)) * duration * (1 - Math.Exp(-dt / duration));
        double orbit = reducedMotion ? 0 : (_frame.Orbit + dt * .13 +
            .16 * Integral(_frame.Busy, state.Busy, .11) + .65 * Integral(_frame.Drag, dragging, .075)) % 1;
        _wasDragging = dragging;
        _frame = new(_frame.Time + dt, Ease(_frame.Hover, hovered ? 1 : 0), Ease(_frame.Press, pressed ? 1 : 0, .065),
            Ease(_frame.Busy, state.Busy ? 1 : 0), Ease(_frame.Navigation, state.Navigation.Active ? 1 : 0),
            Ease(_frame.Arrival, Arrived(state) ? 1 : 0), angle, Ease(_frame.Dark, state.Dark ? 1 : 0),
            Ease(_frame.Reveal, 1, .085), reducedMotion, state.Navigation.Active ? state.Navigation : _frame.Target,
            drag, release, orbit);
        Settling = Math.Abs(_frame.Hover - (hovered ? 1 : 0)) > .005f ||
            Math.Abs(_frame.Press - (pressed ? 1 : 0)) > .005f || _frame.Reveal < .995f ||
            Math.Abs(_frame.Busy - (state.Busy ? 1 : 0)) > .005f ||
            Math.Abs(_frame.Navigation - (state.Navigation.Active ? 1 : 0)) > .005f ||
            Math.Abs(_frame.Arrival - (Arrived(state) ? 1 : 0)) > .005f ||
            Math.Abs(_frame.Dark - (state.Dark ? 1 : 0)) > .005f ||
            Math.Abs(drag - (dragging ? 1 : 0)) > .005f || release > .005f;
        return _frame;
    }

    private static bool Arrived(FloatingBallState state) => state.Navigation.Active && state.Navigation.DistanceTiles <= 8;
    private static float SafeAngle(NavigationSnapshot target) => float.IsFinite(target.AngleRadians) ? target.AngleRadians : 0;
}

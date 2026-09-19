using UnityEngine;

public sealed class BloomMissileAbility : MechPartAbility
{
    [Header("Bloom Carrier")]
    [SerializeField, Range(0.1f, 100)] float carrierSpeed = 20;
    [SerializeField, Range(0.05f, 5)] float burstDelay = 0.35f;
    [Header("Radial Bomblets")]
    [SerializeField, Range(1, 24)] int bombletCount = 8;
    [SerializeField, Range(1, 360)] float spread = 360;
    [SerializeField, Range(-45, 60)] float elevation = 0;
    [SerializeField, Range(0.1f, 60)] float speed = 8;
    [SerializeField, Range(0.05f, 10)] float lifetime = 1.2f;
    [SerializeField, Range(0.1f, 10)] float explosionRadius = 2;
    [SerializeField, Range(0.03f, 0.5f)] float collisionRadius = 0.12f;
    [Tooltip("Carrier plus bomblets, bounded storage per equipped part.")]
    [SerializeField, Range(2, 25)] int maximumProjectiles = 17;
    sealed class Flight
    {
        public bool Active;
        public Vector3 Position, Direction;
        public float Remaining;
        public CombatEffectLease Visual;
    }
    readonly Flight[] flights = new Flight[25];
    int children;
    bool burst;
    float carrierVelocity, velocity, childLifetime, blastRadius, shotRadius, arc, tilt;
    Vector3 forward;
    public int ProjectilePoolSize { get; private set; }
    protected override int RequiredVisuals => 1 + Mathf.Min(Mathf.Clamp(bombletCount, 1, 24),
        Mathf.Clamp(maximumProjectiles, 2, 25) - 1, DetonationLimit);
    public int ActiveProjectiles
    { get { int n = 0; for (int i = 0; i < ProjectilePoolSize; i++) if (flights[i].Active) n++; return n; } }

    public static Vector3 BombletDirection(Vector3 heading, int index, int count, float spread, float elevation)
    {
        heading = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
        if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;
        float angle = count <= 1 ? 0 : spread >= 359.99f ? index * 360f / count : -spread * 0.5f + index * spread / (count - 1);
        return Quaternion.AngleAxis(angle, Vector3.up) * heading * Mathf.Cos(elevation * Mathf.Deg2Rad)
            + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad);
    }

    protected override void Begin()
    {
        children = Mathf.Min(Mathf.Clamp(bombletCount, 1, 24), Mathf.Clamp(maximumProjectiles, 2, 25) - 1, DetonationLimit);
        carrierVelocity = Mathf.Clamp(carrierSpeed, 0.1f, 100); velocity = Mathf.Clamp(speed, 0.1f, 60);
        childLifetime = Mathf.Clamp(lifetime, 0.05f, 10); blastRadius = Mathf.Clamp(explosionRadius, 0.1f, 10);
        shotRadius = Mathf.Clamp(collisionRadius, 0.03f, 0.5f); arc = Mathf.Clamp(spread, 1, 360); tilt = Mathf.Clamp(elevation, -45, 60);
        forward = AimDirection; burst = false;
        for (int i = ProjectilePoolSize; i <= children; i++) { flights[i] = new Flight(); ProjectilePoolSize++; }
        for (int i = 0; i <= children; i++)
        {
            flights[i].Visual = Feedback != null ? Feedback.LeaseAbilityEffect(CombatFeedbackEffect.Kind.Orb, Context.Origin, Tint) : default;
            flights[i].Visual.Hide();
        }
        Launch(flights[0], Context.Origin, forward, Mathf.Clamp(burstDelay, 0.05f, 5));
    }

    void Launch(Flight flight, Vector3 position, Vector3 direction, float life)
    {
        flight.Active = true; flight.Position = position; flight.Direction = direction; flight.Remaining = life;
        Show(flight);
    }

    void Show(Flight flight)
    {
        if (!flight.Visual.IsValid && Feedback != null)
            flight.Visual = Feedback.LeaseAbilityEffect(CombatFeedbackEffect.Kind.Orb, flight.Position, Tint);
        flight.Visual.Orb(flight.Position, shotRadius * 1.5f);
    }

    protected override void Advance(float deltaTime)
    {
        if (!burst)
        {
            Flight carrier = flights[0];
            bool collision = Move(carrier, carrierVelocity, deltaTime, out _);
            if (collision || carrier.Remaining <= 0)
            {
                burst = true; carrier.Active = false; carrier.Visual.Release();
                if (Feedback != null) Feedback.PlayAbilityExplosion(carrier.Position, 0.5f, Tint);
                for (int i = 0; i < children; i++)
                    Launch(flights[i + 1], carrier.Position, BombletDirection(forward, i, children, arc, tilt), childLifetime);
            }
            return;
        }
        for (int i = 1; i <= children; i++)
        {
            Flight flight = flights[i];
            if (!flight.Active) continue;
            bool collision = Move(flight, velocity, deltaTime, out Collider direct);
            if (!collision && flight.Remaining > 0) continue;
            flight.Active = false; flight.Visual.Release();
            Detonate(flight.Position, blastRadius, direct);
            if (!IsActive) return;
        }
        if (ActiveProjectiles == 0) Finish();
    }

    bool Move(Flight flight, float travelSpeed, float deltaTime, out Collider direct)
    {
        float step = Mathf.Min(deltaTime, flight.Remaining);
        bool collision = Queries.Sweep(flight.Position, flight.Direction * (travelSpeed * step), shotRadius,
            Context.Owner, Context.HitLayers, out Vector3 position, out direct);
        flight.Position = position;
        flight.Remaining = Mathf.Max(0, flight.Remaining - step);
        Show(flight);
        return collision;
    }

    protected override void Release()
    {
        for (int i = 0; i < ProjectilePoolSize; i++) { flights[i].Active = false; flights[i].Visual.Release(); }
    }
}

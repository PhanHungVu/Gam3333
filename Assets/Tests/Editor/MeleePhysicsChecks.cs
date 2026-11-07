// Retain the old batch entry point, but validate the current upright/no-stun behavior.
// The retired temporary-ragdoll suite referenced APIs removed by the main-branch merge.
public static class MeleePhysicsChecks
{
    public static void RunBatch() => MeleePushChecks.RunBatch();
}
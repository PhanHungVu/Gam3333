using UnityEngine;

// A rating of 50 preserves existing motor tuning; heavy legs use the authored 25 rating.
public sealed class BipedLocomotionModule : LocomotionModule
{
    [SerializeField] bool usePartSpeedRating;
    [SerializeField, Min(0.01f)] float referenceSpeedRating = 50f;
    public override LocomotionType Type => LocomotionType.Biped;

    public override MechLocomotionSettings Configure(MechLocomotionSettings defaults)
    {
        var part = GetComponent<Part>();
        if (!usePartSpeedRating || part.data == null) return defaults;
        float rating = part.data.GetStat(StatType.MoveSpeed);
        if (rating <= 0f) return defaults;
        float scale = rating / Mathf.Max(0.01f, referenceSpeedRating);
        defaults.moveSpeed *= scale;
        defaults.sprintSpeed *= scale;
        return defaults;
    }
}

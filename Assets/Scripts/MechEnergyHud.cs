using UnityEngine;
using UnityEngine.UI;

public class MechEnergyHud : MonoBehaviour
{
    [SerializeField] MechEnergyController energy;
    [SerializeField] Image fill;
    [SerializeField] Text label;

    public void Bind(MechEnergyController controller) => energy = controller;

    void LateUpdate()
    {
        if (energy == null)
            return;
        if (fill != null)
        {
            fill.fillAmount = energy.Normalized;
            fill.color = energy.Normalized < 0.2f ? new Color(1f, 0.55f, 0.25f)
                : new Color(0.25f, 0.9f, 0.85f);
        }
        if (label != null)
            label.text = "ENERGY  " + Mathf.CeilToInt(energy.Current);
    }
}

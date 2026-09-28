# Blue Golem Joint

Drag **Blue Golem Joint.prefab** under a golem socket or the transform at a part connection. Set its local position to zero, then offset it into the visible gap. The effect loops automatically in Play mode. Select its particle systems in the Scene view to preview with Unity's particle playback controls.

The five layers are a cyan core, two segmented energy rings, orbiting ions, and short outward sparks. The outer ring lies in local XY; point the prefab's blue Z axis along the joint connection. The tilted ring and spherical particles keep the effect visible from other angles.

- Default outer diameter: approximately 0.4 Unity units. Scale the prefab uniformly to fit each joint. Particle simulation follows its parent, including hierarchy scale.
- Adjust the two materials' **Energy Tint** to change HDR brightness and color. URP Bloom can enhance the glow, but is optional.
- Tune individual Particle System emission rates, sizes, and lifetimes normally. All five layers together are capped at 60 particles per connection. There are no lights, collisions, or textures.
- **Golem Joint Glow** automatically finds the parent mech's movement controller. Ground boost and assault boost brighten the effect to 6x; a dodge (quick boost) uses a smaller 1.8x glow. Brightening takes effect quickly and fades smoothly back to the original look. Adjust **Boost Brightness**, **Dodge Brightness**, **Brighten Time**, and **Fade Time** on the joint prefab to tune all instances. These times are exponential smoothing time constants.
- Each joint controls its own renderer brightness without changing the shared materials. Dropped parts return to the normal glow; standalone joints keep their original appearance. Disabling the glow component restores normal brightness.
- The whole joint effect expands to **2.75x its original size during boosts** and **2.25x during dodges**, then smoothly returns to normal. Adjust **Boost Size** and **Dodge Size** on the prefab. These multipliers preserve the individual scale you set for each joint and affect only the effect, including its rings and existing particles. Disabling the component restores the original size.
- Parent to the socket if the effect should stay on the body; parent to the detachable part if it should travel with that part. This prefab does not automatically detect whether a socket is occupied.
- **Tools > VFX > Create Blue Golem Joint** selects the existing prefab, or recreates it when missing.

The shader uses additive transparency, respects scene depth, and is compatible with the project's Universal Render Pipeline. Existing golem prefabs and scenes are not modified.

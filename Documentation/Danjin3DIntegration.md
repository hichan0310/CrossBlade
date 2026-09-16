# Danjin persistent 3D test

Open `Assets/Scenes/CombatScene.unity` in Unity 6000.3.7f1 and enter Play mode. Player already contains the Danjin visual. Select Attack1_1 through the existing move-planning UI. No manual asset wiring or Animator controller is required.

## Runtime structure

Player retains all existing 2D gameplay components and its negatively scaled FacingRoot/MoveMount. Its new sibling Character3D contains CharacterAnimationPlayer and DanjinModel (the imported rig, skin and attached preview sword). Runtime Moves contain no character clone.

Move.characterAnimation is an optional AnimationClip. ActorVisualController passes the current runtime Move and authoritative MoveProgress to CharacterAnimationPlayer. It samples `Clamp01(MoveProgress) * clip.length` using AnimationClip.SampleAnimation; imported Animators are disabled. Total progress includes startup and active duration. The 2.1-second source therefore fits the existing default 0.1-second startup plus 0.3-second Move duration. Changing gameplay duration retimes playback automatically. There is no independent animation speed, controller state machine, or root-motion authority.

The optional motionRoot holds horizontal position in the model's coordinate space. Imported Blender bone axes differ from Unity world axes, so this is not a bone-local X/Z lock. Vertical motion and body pose remain sampled; Actor movement remains under existing combat control.

Release, completion and a Move without a clip sample frame zero of defaultPose. Hit and Guard still start the existing reaction Moves; Clash immediately clears the slash even without a successor. Pose channels reset on Move/clip changes. Frame zero of the slash is a temporary default pose, not a newly authored idle/reaction animation.

Character3D rotates around Y to 90/270 degrees using Actor.FacingSign. It remains outside FacingRoot and uses positive scale. Existing 2D facing stays unchanged. ActorVisualController hides only the replaced root character SpriteRenderer; child sprites, particles, visual-reveal behavior and sprite-only Actors remain on the existing path. Existing TurnMotion work is preserved.

## Assets and attachment

`Assets/Characters/Danjin/` contains the actual exported FBX, native AnimationClip, persistent prefab, textures, materials and source_manifest.json. Materials use URP/Unlit with alpha clipping for this first test.

The export uses `../../output/danjin_eldenring_retarget_weaponfix_xy.blend`, Action `Danjin_ER_SwordSlash_Test01`, rig `DanjinRig`, mesh `Danjin`, and `Existing_MagicSword_Preview` on `WeaponProp02` under the right-hand chain. This is the latest approved attachment: original basis followed by local X +90 degrees and the subsequently requested local Y +90-degree edge roll. Re-exporting was necessary because the earlier staged FBX preceded those corrections. No body retarget or source body Action edit was performed.

The sword is the existing MagicSword preview prop, not a Danjin-specific weapon asset. FBX coordinate conversion changes numerical Euler representation. The Unity sword local quaternion is approximately (0.5, -0.5, -0.5, 0.5); the corrected attachment is stored on the prefab. Sword-object animation curves are excluded from the native clip so playback cannot overwrite this fixed grip. Weapon-bone motion follows the existing source animation. Future clips must target this rig and use the same weapon socket convention.

## Setup and validation tools

`Tools > CrossBlade > Set Up Danjin 3D Test` regenerates this test's materials, clip, prefab and scene assignment from its real FBX. Existing checked-in references are already wired. Re-running intentionally replaces this test character and the Attack1_1 clip assignment; it is not required for normal use.

Batch entry points:

- `CrossBlade.EditorTools.DanjinIntegrationSetup.Run`: setup, with `-batchmode -quit`.
- `CrossBlade.EditorTools.DanjinIntegrationValidation.Run`: deterministic play-mode checks, with `-batchmode -nographics` and no `-quit`; exits itself.
- `CrossBlade.EditorTools.DanjinVisualPreview.Run`: GPU-skinned pose captures on separate play-mode frames, with `-batchmode` and no `-quit`; requires graphics and exits itself.

Pass `-projectPath` pointing to this Unity project and `-logFile` to the desired log. Validation outputs are in `../../output/unity_integration/`. These tools freeze simulation only in the unsaved diagnostic session.

## Scope and next work

Attack1_1 already has category Dash, startup-only movement, a 2D body collider and no weapon Hitbox components. This integration preserves that data; it cannot demonstrate sword damage or blade/contact alignment. Core Actor, ActorActionController, ActorManager, hit detection, forces and move graph were not changed. Run a separate combat tuning pass to define the intended damaging phase and align existing 2D hitboxes with the visible blade. Add matching idle/hit/guard clips next. Other rigs need compatible clip bindings. The full 475-bone source rig is retained for this test; production optimization and final shading remain future work.

# IRVA AR - laboratoarele 1-4

AR-only project based on the [UPB-Graphics/IRVA course repository](https://github.com/UPB-Graphics/IRVA). This repository includes the implemented AR laboratories and their presentation guides, without the VR project or generated builds.

## Prerequisites
- Built using Unity **6000.3.25f1** - we **highly recommend** using the same version

## Usage
- We **highly recommend** following our [git guide](https://ocw.cs.pub.ro/courses/irva/resurse/gitguide)
- Download or clone repo
- Open the `IRVA_AR` folder as a project using **Unity Hub**

## Setup after cloning
- Install Unity **6000.3.25f1** with **Android Build Support**, including its SDK, NDK and OpenJDK modules.
- Keep Git available on PATH: some dependencies in `Packages/manifest.json` are Git packages. Allow Unity to restore the packages and regenerate its local cache on the first opening.
- Keep all `.meta` files together with their assets. Scene and prefab references depend on their GUIDs.
- Cloud Anchors authentication is deliberately excluded: `IRVA_AR/ProjectSettings/ARCoreExtensionsProjectSettings.json` must remain local. Configure the Cloud Anchors feature and authorization in Unity's ARCore Extensions settings for Lab 3. Never commit API keys, signing credentials or real Cloud Anchor IDs.
- Labs 1, 2 and 4 do not require the Cloud Anchors key. The first runtime-image download in Lab 4 requires internet access.
- Build and test manually on an ARCore-compatible Android device. The repository does not include APKs, the `Library` cache, local project ZIP exports, build backups or debug-output folders.
- Unity MCP is an optional editor integration already listed in the project dependencies; starting its server is not required to run the AR application.

## Ghiduri de prezentare pentru laboratoarele AR
- [Laboratorul 1: AR Foundation, plane, puncte si statistici](IRVA_AR/Documentatie/Laboratorul_1.md)
- [Laboratorul 2: masurarea distantelor si mutarea punctelor](IRVA_AR/Documentatie/Laboratorul_2.md)
- [Laboratorul 3: Host, Delete si Resolve pentru un Cloud Anchor](IRVA_AR/Documentatie/Laboratorul_3.md)
- [Laboratorul 4: imagini, fete si bonusul accesoriilor](IRVA_AR/Documentatie/Laboratorul_4.md)

Ghidurile explica implementarea actuala, conceptele, fluxul datelor si metodele importante. Fiecare include pasi de demonstratie, intrebari posibile, o prezentare scurta si un checklist de invatare. Laboratorul 3 este documentat fara bonusul cu ancore multiple.

## AR Lab 1 bonus
- In `L1_ARFoundationIntro`, `Canvas/AR Statistics Panel` displays live AR statistics.
- `ARStatisticsDisplay` references the plane and point cloud managers on `XR Origin`, the AR camera, and the panel's TextMesh Pro text.
- The display refreshes every 0.1 seconds and shows current plane trackables and the total number of feature point positions across all current point clouds, not cumulative detections.
- Camera position is in Unity world coordinates (meters); orientation is shown as Euler angles (degrees).
- The panel does not intercept touches or change the existing menu button. Test live tracking on an ARCore-compatible device; without an active XR provider, detection counts remain zero.

## AR Lab 2: distance measurement
- Open `IRVA_AR/Assets/Scenes/L2_MeasureDistancesApp.unity`. On an ARCore-compatible device, scan a textured surface and tap detected planes to place points.
- Each new point connects to the previous one with a 5 mm thick line. Segment labels show distances in meters and follow the world-space midpoints as the camera moves. The fixed HUD shows the sum of consecutive segment lengths.
- `MeasureDistances` uses the project's Input System, `ARRaycastManager` and `ARAnchorManager`. Points are attached to plane anchors; lines and labels are recomputed from those endpoints when tracking refines their poses. Labels behind the camera are hidden, and HUD touches do not place points.
- The marker, text prefab, material and script originate from `UnityPackages/IRVA_L2_AR.unitypackage`; existing AR assets and scene setup are retained. The line prefab uses a unit cube so its Z scale directly represents meters.
- Bonus: tap an existing cube to select it (yellow), then tap a detected plane to move it. Tap the selected cube again to cancel, or another cube to change the selection. With nothing selected, tapping a plane adds a new point.
- Moving a point creates its replacement anchor before releasing the old one; failed placement leaves the original point and selection intact. Both adjacent segments and the total are recalculated without adding points or labels. Selection highlighting does not modify the shared material.
- If an anchor is removed, measurements and selection are cleared with an on-screen message. Test placement and movement on the device; no Play Mode run is required for compilation.
- Before building this lab, enable the Lab 2 scene in the active build profile's Scene List. No build profile selection is changed by the implementation.

## AR Lab 3: single Cloud Anchor
- `L3_CloudAnchors` implements tasks 1-6: tap a detected plane to place one anchored cube, press **Host**, wait for success, press **Delete**, then **Resolve** while scanning the original area.
- **Delete removes only the local cube and anchor**, not the hosted Cloud Anchor ID. The last successfully hosted ID is kept in device `PlayerPrefs`, including across app restarts. A new successful Host replaces that saved ID.
- API-key hosting uses a one-day lifetime. The existing project authentication/key is retained; the scene uses its own `ARCoreExtensionsCloudAnchorsConfig` with Cloud Anchor Mode enabled.
- Host follows the lab handout: only `Sufficient` or `Good` feature-map quality is accepted. There is no low-quality override or second-press bypass. The Host button requires an unhosted, tracked cube and an acceptable estimate; the handler obtains a fresh estimate before uploading, so a previously good reading cannot bypass the check. Host/Resolve/Delete are disabled while a cloud operation is pending. Leaving the scene cancels pending operations and releases local/resolved anchors.
- The existing status panel shows live AR session state, the tracking-loss reason when available, and current **Host** quality. Mapping refreshes every 0.25 seconds of real time, immediately on tracking-state/reason changes, and on a Host attempt. Evaluation is skipped in the editor, without the required AR configuration/subsystems/tracking, or during a cloud operation; the panel then explains why quality is unavailable instead of displaying an old good reading.
- Live telemetry and the last operation/instruction message share the panel without replacing each other: success, SDK errors, and Resolve timeouts remain visible while tracking/quality update. `ErrorHostingDatasetProcessingFailed` explicitly asks for a more detailed scan with stable tracking. An acceptable estimate improves the chance of success but does not guarantee hosting or relocalization.
- Feature-map estimation converts the AR camera pose to session space through `XROrigin.TrackablesParent`, removing Unity origin transforms and camera-height offsets. Scan a well-lit, textured area from different viewpoints before hosting. Host diagnostics log quality and success/failure states, never API keys or anchor IDs.
- Resolve displays the remaining time and AR tracking status. It cancels a still-pending attempt after 60 seconds of real time (independent of time scale), restores the controls, and keeps the saved ID for retrying. Screen taps during an operation do not replace these instructions with a generic wait message. A result that completes concurrently with cancellation is still handled normally.
- Resolve needs visual tracking of the original area; changed lighting or moved objects can make relocalization harder, especially after hosting with insufficient feature-map quality. A timeout does not establish that the anchor expired. The one-day hosting lifetime runs from successful hosting, not until midnight.
- `CloudAnchorObjectPlacement` receives pointer events through the existing Input System UI module on a transparent touch surface behind the controls. UI button taps therefore do not place cubes. `ARCloudAnchorManager` owns the host/resolve workflow and button states.
- Test on an ARCore-compatible device with internet and ARCore API authorization. Editor compilation/reference checks do not verify cloud authorization or physical relocalization; no Play Mode or live cloud request is needed for those checks. The multiple-anchor bonus is not included.
- Enable this scene in the active Android build profile's Scene List before building. Keep API keys out of commits, screenshots and logs.

## AR Lab 4: augmented images (tasks 1-3)
- [L4_AugmentedImages](IRVA_AR/Assets/Scenes/L4_AugmentedImages.unity) starts with [a reference library](IRVA_AR/Assets/Lab4ReferenceImageLibrary.asset) containing **Earth, Astronaut and LunarRover**. Each tracked image receives a cube from the existing placed-cube prefab.
- Press **Runtime (1)** to download the fourth target, **Footprint**, and add it to a new mutable library using `ScheduleAddImageWithValidationJob`. The initial library remains active until the download and validation succeed. Failures are shown in the status panel and allow retrying. Internet is required for this download, not for tracking the initial targets.
- Press **Initiala (3)** to return to the three-image library. Returning to runtime mode reuses its downloaded library for the current scene visit; leaving the scene releases its resources. The two libraries are deliberately separate: Footprint is not in the initial library.
- [ARRuntimeImageLibrary](IRVA_AR/Assets/Scripts/ARRuntimeImageLibrary.cs) handles both libraries through AR Foundation 6's `trackablesChanged` event. It reuses the configured `ARTrackedImageManager` and changes its non-null library without stopping the image subsystem. After switching, it reconciles the manager's existing trackables as well as future events: repeated **Runtime -> Initiala -> Runtime** cycles must restore already-tracked targets even if they emit no new added/updated event. **Tracked Image Prefab stays None**: the script creates the augmentation under the tracked image, explicitly resets its local pose, reuses it on updates, and hides it unless tracking is `Tracking`. Removed images and library changes clean up the cubes.
- Physical widths are not hardcoded because targets can be printed or displayed at different sizes; ARCore estimates their size. The scene uses the rear AR camera. Plane/point-cloud overlays, the duplicate non-AR camera and the unused ARCore Extensions component are disabled only in this scene. Native ARCore image tracking does not require the Cloud Anchor extension; its configuration assets and authentication are unchanged.
- Open the complete target on the laptop, or print it flat. Keep it stationary, well lit and free of glare, initially covering at least approximately 25% of the tablet camera view. Scan the matching target for the selected library. The runtime target must remain visible after its download completes.
- The photographs below are NASA Apollo imagery, credited to **NASA** and used for educational testing under [NASA's media usage guidance](https://www.nasa.gov/nasa-brand-center/images-and-media/); no endorsement is implied. All are larger than 300 x 300 pixels. Scores are from the installed ARCore `arcoreimg eval-img` utility and are not guarantees of recognition on a device.

| Target to display/print | Library | ARCore score | Original NASA image |
|---|---|---:|---|
| [Earth](IRVA_AR/Assets/Textures/Lab4/Earth.jpg) | Initial | 75 | [AS17-148-22727](https://images-assets.nasa.gov/image/as17-148-22727/as17-148-22727~medium.jpg) |
| [Astronaut](IRVA_AR/Assets/Textures/Lab4/Astronaut.jpg) | Initial | 80 | [AS11-40-5903](https://images-assets.nasa.gov/image/as11-40-5903/as11-40-5903~medium.jpg) |
| [LunarRover](IRVA_AR/Assets/Textures/Lab4/LunarRover.jpg) | Initial | 100 | [AS15-88-11901](https://images-assets.nasa.gov/image/as15-88-11901/as15-88-11901~medium.jpg) |
| [Footprint-Runtime](IRVA_AR/Assets/Textures/Lab4/Footprint-Runtime.jpg) | Runtime | 100 | [KSC-AS11-40-5877](https://images-assets.nasa.gov/image/KSC-as11-40-5877/KSC-as11-40-5877~medium.jpg) |

- Include the image scene in the active Android build profile's Scene List before a manual build. When building the full menu, preserve the existing scene order: images uses index 4. Editor checks do not verify real camera recognition, device internet access or native runtime-library switching; these require tablet testing.

## AR Lab 4: blue face mask and three-accessory bonus
- [L4_AugmentedFaces](IRVA_AR/Assets/Scenes/L4_AugmentedFaces.unity) detects one face with the **front/selfie camera**. The session requests **Rotation Only**, and `ARCameraManager` requests **User**, as required by the handout.
- The scene's `ARFaceManager` instantiates [Tracked Face](IRVA_AR/Assets/Prefabs/FaceAccessories/Tracked%20Face.prefab), based on the standard **XR > AR Default Face**. The SDK still owns face detection, mesh updates and removal. **Albastra** is the default selection and uses the original semi-transparent turquoise material. Only accessory modes use the [depth-only URP shader](IRVA_AR/Assets/Prefabs/FaceAccessories/FaceOcclusion.shader), leaving the real face visible while hiding accessory parts behind it. The original [turquoise face prefab](IRVA_AR/Assets/Prefabs/Lab4%20Face%20Mask.prefab) remains unchanged.
- Select **Albastra**, **Ochelari**, **Pisica** or **Coroana**: the original blue mask plus three distinct 3D accessories. **Urmatoarea >** cycles through all four and wraps from crown to blue. Switching back to blue removes accessories and restores the original material immediately; disabling the bonus controller also restores blue. The accessory models are locally authored procedural meshes, not downloaded assets, and use dedicated URP Unlit materials.
- [ARFaceMaskController](IRVA_AR/Assets/Scripts/ARFaceMaskController.cs) uses ARCore's nose-tip and left/right forehead regions to place and scale the selected accessory. Region poses are converted from session space through `XROrigin.TrackablesParent` into face-local space, preserving origin offsets, rotation and scale. The models have stylized proportions; precise fit still needs tablet testing.
- Selection is allowed before detection and while a face is temporarily lost. It applies immediately to existing tracked faces and persists for replacement/re-detected faces during the current scene visit. Only one accessory instance is owned per face; switches/removals clean it up. `Limited`/`None` face tracking or unavailable/invalid region data hides accessories instead of using stale poses. Blue uses the standard face mesh visualizer and does not depend on ARCore region poses. Leaving or disabling the controller releases its native region buffer and instances.
- Face tracking must **not** require world-camera tracking: [ARCore's front camera always reports `PAUSED`](https://developers.google.com/ar/reference/java/com/google/ar/core/CameraConfig.FacingDirection#FRONT). The controller accepts a running face subsystem while the session is `SessionInitializing` or `SessionTracking`, then checks each face's own tracking state. `Ready`/earlier session states and stopped face subsystems still prevent native region queries.
- Copied image-controller/image-manager components were removed only from this face scene. Plane/point managers, the duplicate non-AR camera and the unused Cloud Anchor extension are disabled here; the image scene and authentication settings are unchanged. The existing menu/back button is preserved.
- Include this scene in the active Android build profile before a manual build. With the full menu, retain its existing **index 5**. On the tablet, allow camera access and face the selfie camera in good lighting; move or turn your head gently to check that the mask follows. No reference images, downloads or Cloud Anchors are required.
- The accessory bonus requires ARCore's face regions on Android, not eye tracking or Cloud Anchors. Editor checks cover selection, cycling, simulated tracking/coordinate transforms, saved references and UI/rendering; actual face alignment and occlusion must also be checked on the tablet. No Play Mode, app launch or APK build is required for editor checks.

## Project support
- [OCW documentation](https://ocw.cs.pub.ro/courses/irva)

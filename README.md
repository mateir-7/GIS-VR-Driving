# Driving Simulator using GIS Data

A Unity 6 VR driving simulator that builds a drivable slice of real-world Bucharest (the Piața Unirii area) procedurally from raw **OpenStreetMap** data. It uses no imported 3D city models. Streets, intersections, buildings, parks, water, street furniture and ambient traffic are all generated from a single `.osm` file and baked into the scene as static meshes.

Bachelor's thesis, Faculty of Engineering in Foreign Languages (FILS), National University of Science and Technology POLITEHNICA Bucharest, 2026.

## What it does

The simulator places the driver in a car, in seated VR on a Meta Quest 3, inside a recognisable piece of Bucharest. The city is built by an editor-side pipeline:

```
.osm XML  →  streaming parser  →  lat/lon projected to local metres
          →  typed ways & nodes  →  procedural meshes (.asset)
          →  scene hierarchy  →  custom URP shaders
```

### Features

- **OSM parsing**: a streaming XML parser reads nodes, ways and tags. Coordinates are projected from WGS84 lat/lon to local metres with an equirectangular tangent-plane projection centred on Piața Unirii (1 Unity unit = 1 m).
- **Roads**: quad-strip meshes along each `highway=*` way, with widths derived from lane count and mitred joints at bends. Footways and paths get separate materials. A custom URP shader draws edge lines and dashed lane dividers procedurally, in metres.
- **Junctions**: plain-asphalt patches are generated at every OSM node shared by two or more roads. They are coplanar with the roads so the wheel colliders never hit a step.
- **Buildings**: footprints are extruded to a height taken from OSM tags (`height`, `building:levels`), or from a seeded random value when no tag exists. Roofs are ear-clipped so concave footprints work. A custom facade shader draws the window grid, a single door and parapets. Walls are tinted per building.
- **Ground cover**: parks, grass, water and parking areas, laid over a base ground plane.
- **Props**: trees come from OSM nodes and tree rows. Street lamps come from OSM, and extra lamps are auto-placed along primary and secondary roads.
- **Decor**: benches, bus stops, monuments, and tiered fountains with particle spray.
- **Lighting**: a one-click daytime setup that creates the sun, a trilight ambient, a procedural skybox and URP post-processing.
- **Ambient traffic**: kinematic cars that follow Catmull-Rom splines through waypoint chains. Chains can be auto-generated from OSM roads or placed by hand with a custom scene-view editor.
- **VR driving**: the car uses `WheelCollider` physics, a grabbable steering wheel and a grabbable gear shift, and runs on a Meta Quest 3 over Quest Link.

### Why not Cesium or Mapbox?

Streaming 3D tile solutions swap geometry at runtime as levels of detail change. That destabilises `WheelCollider` physics, because wheels can catch on seams or fall through while tiles are being replaced. Baking static, non-convex `MeshCollider`s from OSM gives the car the same kind of ground it was already tuned for. It also keeps rendering predictable under URP in VR.

---

## Requirements

### Software

| Component | Version |
|---|---|
| Unity Editor | **6000.4.3f1** (other Unity 6 versions may work but are untested) |
| Operating system | Windows 10/11 (Quest Link is Windows-only) |
| Meta Quest Link app | Latest |
| Git + Git LFS | Required to clone (large binary assets are stored with LFS) |

These Unity packages are resolved automatically from `Packages/manifest.json` when the project opens:

- Universal Render Pipeline 17.4.0
- OpenXR + Meta OpenXR 2.5.0
- XR Interaction Toolkit 3.4.1
- XR Hands 1.7.3
- AR Foundation 6.4.2
- Input System 1.19.0
- Splines 2.8.4 (installed, currently unused)

### Hardware

- Meta Quest 3, connected with a Link cable or Air Link. Other OpenXR headsets have not been tested.
- A Windows PC that meets Meta's Quest Link hardware requirements.

---

## Getting started

1. **Clone the repository with LFS:**
   ```bash
   git lfs install
   git clone https://github.com/<your-username>/<repo-name>.git
   ```
2. **Open the project in Unity Hub.** Choose *Add → Add project from disk* and select the cloned folder, then open it with Unity **6000.4.3f1**. The first import rebuilds the `Library/` folder and can take several minutes.
3. **Check the XR settings.** Go to *Edit → Project Settings → XR Plug-in Management* and confirm that **OpenXR** is enabled on the Windows tab.
4. **Open the main scene:** `BasicScene.unity`.
5. **Start Quest Link** on the headset, then press **Play** in the Editor.

### Controls

| Action | Input |
|---|---|
| Steer | Grab the steering wheel with the controller grip and turn it |
| Change gear | Grab the gear shift |
| Throttle / brake | *[fill in your controller mapping]* |

---

## Rebuilding the city from OSM data

The baked city is already included in the repository. You only need these steps to rebake it or to use a different area.

1. **Get OSM data.** Use the *Export* function on [openstreetmap.org](https://www.openstreetmap.org) for a small bounding box. Place the `.osm` file in `Assets/GIS/Data/`.
2. Select the **OsmRoadBaker** GameObject and set `osmFile` to your file name. If the area is not centred on Piața Unirii, adjust the projection origin as well.
3. Click the bake buttons in the Inspector in this order. The same actions are also available under the **GIS** menu.
   1. `Bake Roads`
   2. `Bake Buildings`
   3. `Bake Ground`
   4. `Bake Props`
   5. `Bake Decor`
   6. `Setup Daytime Lighting + Sky`
   7. `Bake AI Waypoints` (optional)

Every bake step is idempotent: it clears its own output before rebuilding. Baked meshes are saved to `Assets/GIS/Meshes/`.

### Adding ambient traffic

1. Drag one of the stylised car prefabs from `Assets/VRTemplateAssets/Stylized Vehicles/Prefabs/` into the scene.
2. Add a **SimpleRailCar** component to it.
3. Assign a waypoint chain to `Waypoints Parent`. It can be an auto-generated `Chain_<id>_<highway>`, or a manual chain created with *GIS → Create Waypoint Chain*. For a manual chain, Ctrl+Click in the Scene view to drop waypoints.
4. To put several cars on one chain, duplicate the car and give each copy a different `startOffsetMeters`, for example `0`, `L/N`, `2L/N`, where `L` is the chain length and `N` is the number of cars. All cars move at the same speed, so the gaps between them stay constant.

---

## Project structure

```
Assets/GIS/
├── Data/        raw .osm extracts
├── Materials/   RoadLit.shader, BuildingFacade.shader, URP materials, skybox
├── Meshes/      baked mesh assets (one per road/building/polygon + shared prop meshes)
├── Scripts/     runtime code: projection, OSM data model, parser, traffic, waypoint chains
└── Editor/      bake pipeline (OsmRoadBakerEditor) and waypoint chain editor
```

| Key file | Role |
|---|---|
| `Scripts/GeoProjection.cs` | lat/lon → local metres |
| `Scripts/OsmParser.cs` | streaming `.osm` XML parser |
| `Scripts/OsmData.cs` | `OsmWay`, `OsmNode`, `OsmDataset` |
| `Scripts/SimpleRailCar.cs` | kinematic spline-following traffic |
| `Scripts/WaypointChain.cs` | traffic lane definition |
| `Editor/OsmRoadBakerEditor.cs` | the full bake pipeline |
| `Materials/RoadLit.shader` | procedural lane markings |
| `Materials/BuildingFacade.shader` | procedural windows, door and parapet |

The original driving controller (`SimKartController.cs`) is used as-is. The GIS layer is built around it and does not modify it.

---

## Known limitations

- OSM **relations** (multipolygons) are ignored. Only `<way>` elements are baked, so parks or lakes with holes are not represented correctly.
- Parking areas are flat surfaces with no painted stripes or kerbs.
- There are no pedestrians, traffic lights or vehicle signals, and there is no day/night cycle.
- Automatic wheel detection for traffic cars depends on child-object naming. Third-party car prefabs may need their wheels assigned manually.
- The bake is sized for a single neighbourhood. Scaling to the whole city would need optimisation.

---

## Credits and attribution

- Map data © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright), available under the Open Database License (ODbL).
- Race track pieces: [Kenney](https://kenney.nl) Race Track Kit (CC0).

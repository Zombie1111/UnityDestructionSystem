<h1 align="center">UnityDestructionSystem by David Westberg</h1>

## Overview
The primary purpose of this project is to provide a efficient way to add destruction and deformation capabilities to medium sized objects like cars, robots or a wall. Its designed to work well with dynamic objects and scripting. This destruction solution is currently not designed to simulate a large building collapsing, for those purposes I suggest trying RayFire by RayFire Studios.

<img src="https://media.giphy.com/media/hIvqXY8HGMtml0vS1V/giphy.gif" width="200%" height="200%"/>

<img src="https://media.giphy.com/media/0XjLDCdhNnC3LW9Nj3/giphy.gif" width="200%" height="200%"/>

<img src="https://media.giphy.com/media/3EvyuUwIGKieDKzktM/giphy.gif" width="200%" height="200%"/>

## Key Features
<ul>
<li>Destruction and deformation based on forces applied to the object</li>
<li>Maintains the hierarchy of the object, meaning object references will still work</li>
<li>No significant lag spikes when destruction occures (See `Technical Details`)</li>
<li>Restore objects to a saved state</li>
<li>Scripting support and delegates</li>
<li>Easy to setup and highly customizable</li>
<li>Multiple materials per object</li>
<li>Compatible with prefabs</li>
</ul>

## Installation
**Requirements** (Should work in other versions/render piplines)
<ul>
<li>Unity 2023.2.20f1 (Built-in)</li>
<li>Burst 1.8.17</li>
<li>Collections 2.1.4</li>
<li>Compute Shader Support</li>
<li>Allow unsafe code (Because using pointers)</li>
<li>Meshes should be watertight, no self-intersections and no open-edges</li>
<li>Blender 3.6 (For advanced setup)</li>
</ul>

**General Setup**
<ol>
  <li>Download and copy the Resources, Plugins, Scripts, and _Demo (optional) folders into an empty folder inside your Assets folder</li>
  <li>Create a new empty gameobject and add the DestructionHandler script to it</li>
  <li>Create a empty parent for the gameobject you want to be destructable and add the DestructableObject script to the empty parent</li>
  <li>Create a SaveAsset and assign it to the DestructableObject script, Tools->Destruction->CreateSaveAsset</li>
  <li>Configure the DestructableObject settings and press the Generate Fracture button</li>
  <li>Enter playmode and you should be able to destroy the object</li>
</ol>

**Advanced Setup**
<ol>
  <li>Complete the General Setup</li>
  <li>Open Blender, import your mesh and install the addon found in the `Scipts/BlenderAddon/` folder</li>
  <li>Assign a vertex group too all vertics in your mesh. Maybe assign group A to a door and group B to the walls</li>
  <li>Create a new vertex group that has a name that start with `link`. Assign the `link` vertex group to vertics that you want to be able to connect that are in different base vertex groups. Like the door hinges and the wall</li>
  <li>Press the `Setup vertex colors for unity fracture` button and export+import the mesh with vertex colors</li>
  <li>Select your DestructableObject and add a second destructionMaterial and make it affect groupIndex 1</li>
  <li>Add saveState and should work, joints</li>
</ol>

## Documentation
Most functions are documented and all parameters visible in the unity inspector have tooltips

The `_Demo/` folder contains pratical exampels

## Technical Details
**Fracturing**
The DestructableObject is split into a bunch of small parts that can be moved independetly of each other, these parts are generated automatically from a source object using nvBlast. Parts that are next to each other are then connected forming a net that is used to solve the destruction. This processes is called fracturing and is normally done in the editor.

**Rendering**
The DestructableObject is rendered using a single meshRenderer and a compute shader that uses the mesh GPU Vertex Buffer to efficiently modify the mesh vertics and normals directly from the GPU. The compute shader works a bit like a skinnedMeshRender and uses the DestructableObject parts as bones. The compute shader also handels deformation by inputing deformation points and offsetting the vertices around the deformation points by a given amount (See `ComputeDestructionSolver.compute`).

**Simulation**
 The destruction is simulated using my own severly faked algorythm. It computes the mass each part would need to push and the force needed to push the mass forward by X. If the part strenght is less than the force needed it breaks. The advatage of my algorythm is that it solves the destruction in a single iteration (See `CalcSource()` in `DestructableObject.cs`). The DestructableObject recieves most of its input from Physics.ContactModifyEvent (See `ModificationEvent()` in `DestructionHandler.cs`).

**Performance**
The destruction is computed on a background thread and I have not noticed any significant fps drops when destroying lots of objects even in the editor. 
But based on my testing, the biggest performance impact simply comes from the normal unity physics engine and the fact that I cant set the transform parent or add components from a different thread. However since both these bottlenecks are cpu bound, they are mostly eliminated in a build using the IL2CPP backend and C++ Compiler Configuration set to Master. In a build with that configuration the biggest performance impact comes from the increased triangle count.

**Execution Order**


## License
The original code and assets created for this project are licensed under CC BY-NC 4.0 - See the `LICENSE` file for more details.

### Third-Party Libraries
This project uses third-party files and libraries located in the `Plugins/` folder. These files and libraries are **not** covered by the CC BY-NC 4.0 license and are subject to their own respective licenses. Please refer to each files, librarys license file in the `Plugins/` folder for more information.

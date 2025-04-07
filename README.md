<h1>UnityDestructionSystem</h1>

## Table of Contents
- [Overview](#overview)
- [Key Features](#key-features)
- [Instructions](#instructions)
- [Documentation](#documentation)
- [Technical Details](#technical-details)
- [License](#license)
- [Third-Party Libraries](#third-party-libraries)
  
## Overview
The primary purpose of this project is to provide a efficient way to add destruction and deformation capabilities to medium sized objects like cars, robots or a wall. Its designed to work well with dynamic objects and scripting. This destruction solution is currently not designed to simulate a large building collapsing, for those purposes I suggest trying RayFire by RayFire Studios.

<img src="https://media.giphy.com/media/hIvqXY8HGMtml0vS1V/giphy.gif" width="100%" height="100%"/>

<img src="https://media.giphy.com/media/0XjLDCdhNnC3LW9Nj3/giphy.gif" width="100%" height="100%"/>

<img src="https://media.giphy.com/media/3EvyuUwIGKieDKzktM/giphy.gif" width="100%" height="100%"/>

## Key Features
<ul>
<li>Destruction and deformation based on forces applied to the object</li>
<li>Maintains the hierarchy of the object, meaning object references will still work</li>
<li>No significant lag spikes when destruction occures (See <code>Technical Details</code>)</li>
<li>Restore objects to a saved state</li>
<li>Scripting support and delegates</li>
<li>Easy to setup and highly customizable</li>
<li>Multiple materials per object</li>
<li>Compatible with prefabs</li>
</ul>

## Instructions
**Requirements** (Should work in other versions/render piplines)
<ul>
<li>Unity 2023.2.20f1 (Built-in)</li>
<li>Burst 1.8.17</li>
<li>Collections 2.1.4</li>
<li>Compute Shader Support</li>
<li>Allow unsafe code (Because using pointers)</li>
<li>Meshes should be watertight, no self-intersections and no open-edges</li>
<li>Blender 3.6 (For advanced features)</li>
</ul>

**General Setup**
<ol>
  <li>Download and copy the Resources, Plugins, Scripts, and _Demo (optional) folders into an empty folder inside your Assets folder</li>
  <li>Create a new empty gameobject and add the DestructionHandler script to it</li>
  <li>Create a empty parent for the gameobject you want to be destructable and add the DestructableObject script to the empty parent</li>
  <li>Create a SaveAsset and assign it to the DestructableObject script, <code>Tools->Destruction->CreateSaveAsset</code></li>
  <li>Configure the DestructableObject settings and press the Generate Fracture button</li>
  <li>Enter playmode and you should be able to destroy the object</li>
</ol>

Its a good practice to always remove the fracture before you do any changes to the DestructableObject in editor

**Multiple Destruction Materials**
<ol>
  <li>Open Blender, import your mesh and install the addon found in the <code>Scipts/BlenderAddon/</code> folder</li>
  <li>Assign a vertex group too all vertics in your mesh. Example, assign group A to a door and group B to the rest</li>
  <li>Create a new vertex group that has a name that start with <code>link</code>. Assign the <code>link</code> vertex group to vertics that you want to be able to connect but are not in the same base vertex group. Like door hinges and the nearby wall</li>
  <li>Press the <code>Setup vertex colors for unity fracture</code> button and export+import the mesh with vertex colors</li>
  <li>Select your DestructableObject, add a second destructionMaterial and make it affect groupIndex 0</li>
</ol>

You should now have a DestructableObject with multiple materials. Since vertex groups overwrites the vertex colors of your mesh, you can add a DestructionVisualMesh script to the gameobject that has your meshRenderer+filter to still be able to use vertex colors for other purposes.

**Joints**
<ol>
  <li>Add a DestructionJoint script to object A and assign object B to ConnectedTransform</li>
  <li>Create a Joint somewhere on a disabled gameobject and assign it to SourceJoint</li>
  <li>Create empty gameobject and add it to the JointAnchors list. Position the JointAnchors where object A should connected with object B</li>
  <li>Object A and B should now be connected using a copy of the SourceJoint at runtime</li>
</ol>

**Restoring Objects To A Saved State**
<ol>
  <li>Create a saveState asset and assign it to the DestructableObject <code>Tools->Destruction->CreateSaveStateAsset</code></li>
  <li>Save the state in editor or at runtime by pressing the <code>Save State</code> button or calling <code>TrySaveAssignedSaveState()</code> in <code>DestructableObject.cs</code></li>
  <li>At runtime you should now be able to load it by pressing the <code>Load State</code> button or calling <code>TryLoadAssignedSaveState()</code> in <code>DestructableObject.cs</code></li>
</ol>

**None Destructable Objects**

If you have non DestructableObjects with rigidbodies you should add the `DestructionBody` script to them for proper interaction with other DestructableObjects

**Scripting**

You can subscribe to the `OnPartParentChanged` or `OnParentUpdated` delegate in `DestructionObject.cs` or add the `DestructionCallback` script to recieve callbacks when destruction occures

## Documentation
Most functions and parameters visible in the unity inspector are documented

The `_Demo/` folder contains pratical exampels

## Technical Details
**Fracturing**

The DestructableObject is split into a bunch of small parts that can be moved independetly of each other, these parts are generated automatically from a source object using nvBlast. Parts that are next to each other are then connected forming a net that is used to solve the destruction. This process is called fracturing and is normally done in the editor.

**Rendering**

The DestructableObject is rendered using a single meshRenderer and a compute shader that uses the mesh GPU Vertex Buffer to efficiently modify the mesh vertics and normals directly from the GPU. The compute shader works a bit like a skinnedMeshRender and uses the DestructableObject parts as bones. The compute shader also handels deformation by inputing deformation points and offsetting the vertices around the deformation points by a given amount (See `ComputeDestructionSolver.compute`).

**Simulation**

 The destruction is simulated using a implementation of my own Algorithm called `Single-Iteration Node Stress Propagation`.\
 (See [Algorithm Paper](Single-IterationNodeStressPropagationPaper.md))\
 The DestructableObject recieves most of its input from Physics.ContactModifyEvent (See `ModificationEvent()` in `DestructionHandler.cs`).

**Performance**

The destruction is computed on a background thread and I have not noticed any significant fps drops when destroying lots of objects even in the editor. 
But based on my testing, the biggest performance impact simply comes from the normal unity physics engine and the fact that I cant set the transform parent or add components from a different thread. However since both these bottlenecks are cpu bound, they are mostly eliminated in a build using the IL2CPP backend and C++ Compiler Configuration set to Master. In a build with that configuration the biggest performance impact comes from the increased triangle count.

## License
The original code and assets created for this project are licensed under CC BY-NC 4.0 - See the `LICENSE` file for more details.

### Third-Party Libraries
This project uses third-party files and libraries located in the `Plugins/` folder. These files and libraries are **not** covered by the CC BY-NC 4.0 license and are subject to their own respective licenses. Please refer to each files, librarys license file in the `Plugins/` folder for more information.

<h1 align="center">UnityDestructionSystem by David Westberg</h1>

## Overview
Is to provide a way to add destruction to induvidual medium sized objects like a car, wall or a small shed. Its not designed to simulate how a larger building should collapse, I would suggest taking a look at RayFire by name if your may goal is to simulate larger structures.

Gif showing car driving through wall

Gif showing small building being destroyed and restored

Gif showing mechanical spider being shot

## Key Features
<ul>
<li></li>
<li>Destruction and deformation based on forces applied to the object</li>
<li>Maintains the hierarchy of the object, meaning stuff like rigid animations will still work</li>
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
<li>Compute Shader support</li>
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


## Documentation
Most functions are documented and all parameters visible in the unity inspector have tooltips

The `_Demo/` folder contains pratical exampels

## Technical Details
**Rendering**


**Simulation**


**Performance**


**Fracturing**


## License
The original code and assets created for this project are licensed under CC BY-NC 4.0 - See the `LICENSE` file for more details.

### Third-Party Libraries
This project uses third-party files and libraries located in the `Plugins/` folder. These files and libraries are **not** covered by the CC BY-NC 4.0 license and are subject to their own respective licenses. Please refer to each files, librarys license file in the `Plugins/` folder for more information.

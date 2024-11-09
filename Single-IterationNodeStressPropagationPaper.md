<h1 align="center">Single-Iteration Node Stress Propagation by David Westberg</h1>

## Table of Contents
- [Introduction](#Introduction)
- [Definitions](#Definitions)
- [The Algorithm](#The-Algorithm)
- [Conclusion and Discussion](#Conclusion-and-Discussion)
- [License](#License)
  
## 1. Introduction

This paper proposes an algorithm to compute the stress each node in a structure would experience if the node(s) `Source` is struck with a force of `Velocity * Mass` in a single iteration. This algorithm was developed for my Unity Destruction System ([GitHub Repository](https://github.com/Zombie1111/UnityDestructionSystem)), as I couldn't find a fast, single-iteration solution for computing how forces should spread across a node structure - a requirement for my Destruction System project.

[![stress-Distribution.png](https://i.postimg.cc/90f4fCs5/stress-Distribution.png)](https://postimg.cc/MMhZP2Wd)

*Black Circle: steel ball **|** Blue line: ball velocity **|** Red Boxes: nodes, brighter means more stress*

## 2. Definitions

| Term                   | Description |
|------------------------|-------------|
| **Source**             | The node(s) that was struck with `Velocity` and `Mass`. |
| **Velocity**           | The speed the `Source` node(s) was struck at. |
| **Mass**               | The mass the `Source` node(s) was struck with. |
| **MaterialMass**       | The mass of a single node. If the node is kinematic, `MaterialMass` should equal the mass of the connected object divided by the number of nodes connected to it. If connected to a kinematic object, `MaterialMass` should equal `Mass` divided by the number of nodes connected to a kinematic object. |
| **MaterialStiffness**  | The proportion of force that results in bending or heat. For example, a value of 0.6 indicates that 40% of the force will result in bending or heat. |
| **MaterialDamageAccumulation** | Defines how much weaker a node becomes as it accumulates more stress. For example, a value of 1.0 indicates that 100% of the stress experienced will be accumulated. |
| **MaterialStrength**   | The maximum stress that a node can withstand. |
| **Node**               | A position in 3D space with some limits like `MaterialStiffness` and `MaterialStrength`. Also stores `NodeMass`, `NodeStrength`, `NodeDamage`, `NodeStep`, `NodeStress`. |
| **Node Structure**     | A collection of interconnected nodes arranged in a grid-like pattern. |
| **NodeStep**           | The minimum number of steps required to reach a node in `Source` from any other node. |
| **NodeMass**           | The mass associated with each node if the node structure is dragged or pushed by node(s) `Source`. |
| **NodeStrength**       | The maximum stress a node can endure before breaking. |
| **NodeStress**         | The stress a node experiences. |
| **NodeDamage**         | The stress a node has accumulated in its lifetime. |
| **Kinematic Node**     | A node connected to an object unrelated to this node structure. |
| **TotalHeatForce**     | The total amount of force converted to heat or bending (depending on `MaterialStiffness`). |
| **BendEfficiency**     | The proportion of `TotalHeatForce` that will result in bending. |
| **BendFalloff**        | How much force is lost when bending. |
| **BendPower**          | Power of the force lost during bending. |
| **BendStrength**       | The force needed to bend. |

## 3. The Algorithm

The general concept is to determine the force required to move each node so the node structure maintains the same shape if a given set of nodes is forced to move.

### 3.1. Structure Initialization
Start by creating an array of nodes in 3D space and connect all neighboring nodes, so a node structure is formed. A node usually represents a chunk of a mesh, and the AABB of the chunk can be used to determine if two nodes are neighbors.

### 3.2. Update Nodes
3.2.1. **Node Strength**:
   - `NodeStrength = max(MaterialStrength - (NodeDamage * MaterialDamageAccumulation), 0.0)`

3.2.2. **Node Step**:
   - Calculate the `NodeStep` value through a flood-fill algorithm, starting from the `Source` node(s) and expanding through their neighbors.

3.2.3. **Node Mass**:
   - Determine the highest `NodeStep` value among the nodes (denoted as `S`).
   - Propagate Mass:
      - For each node where `NodeStep == S`, add `MaterialMass` to `NodeMass`.
      - For each neighboring node where `NodeStep < S` (denoted as `N`), distribute `NodeMass / N count` to each `N`.
      - Decrease `S` by 1 and repeat until `S == 0`.
   - For each node in `Source`, if `NodeMass < MaterialMinMass / Source count`, set `NodeMass = MaterialMinMass / Source count`.

### 3.3. Stress Distribution
3.3.1. **Calculate Rough Stress**
   - The maximum possible stress any given node can experience if the node(s) `Source` were struck by `Velocity * Mass` can be calculated using `min(NodeMass * Velocity, Velocity * Mass) * (MaterialStiffness ^ NodeStep)`.

3.3.2. **Calculate Actuall Stress**:
   - Set `TotalHeatForce = 0`.
   - For each node in `Source`, set `NodeStress = min(NodeMass * Velocity, Velocity * Mass)`.
   - Set `S = 0`.
   - Propagate Stress:
      - For each node where `NodeStep == S`, get all neighbors where `NodeStep > S` (denoted as `N`).
      - If `(NodeDamage * MaterialDamageAccumulation) + NodeStress > NodeDamage`, set `NodeDamage` to `(NodeDamage * MaterialDamageAccumulation) + NodeStress`.
      - Calculate the `total strength (T)` and `total mass (M)` by summing the `NodeStrength` and `NodeMass` from each `N`.
      - Update `NodeStress` of each `N` with `min(T, NodeStress) * (N NodeMass / M) * MaterialStiffness`.
      - Add `(min(T, NodeStress) * (N NodeMass / M)) - N NodeStress` to `TotalHeatForce`.
      - If `T < NodeStress`, the node breaks with an excess force of `NodeStress - T`.
      - Increase `S` by 1 and repeat until no node has `NodeStep == S`.

### 3.4. Deformation
Given a 3D point (`A`) where a node in `Source` was struck and a 3D point (`B`) to offset, the offset amount can be calculated as follows:
   - Set `F = sqrt(dot(B - A, B - A)) * BendFalloff`.
   - Increase `F` by `F * (F * BendPower)`.
   - If `F <= TotalHeatForce * BendEfficiency`, offset `B` with `BendAmount * clamp(((TotalHeatForce * BendEfficiency) - F) / (BendStrength * sqrt(dot(Velocity, Velocity))), 0.0f, 1.0f) * Velocity;`.

## 4. Conclusion and Discussion
The proposed algorithm has, in my tests, produced a stress distribution that resulted in visually appealing destruction and deformation for my Unity Destruction System project ([GitHub Repository](https://github.com/Zombie1111/UnityDestructionSystem)). Though the proposed algorithm produces a good stress distribution it is not physically accurate and there is room for further improvement (see sections 4.1 and 4.2).

### 4.1. Directional Node Strength
Currently, the direction from node `A` to node `B` has no effect on the connection strength. This could potentially be resolved by multiplying `NodeStrength` with `dot((B - A) / (∣B - A∣), Velocity / ∣Velocity∣)`.

### 4.2. Integrated Deformation
The deformation is currently computed separately after the stress has been propagated. This usually gives good enough results for smaller structures but may not for more complex structures. The deformation could, in theory, be integrated into the stress propagation with a few modifications. Here is a general outline:
   - For each node, offset its 3D position by `Velocity * min(NodeStress / (Velocity * NodeMass), 1.0f)`
   - For each node, get how stretched it is by comparing its offset amount with its neighbours. If the node streth amount exceeds max allowed stretch it breaks.

### 4.3. Distance Between Nodes
The distance between each node currently has no effect on the stress distribution. This can result in unexpected stress distribution if the distance between nodes is not roughly the same.

## 5. License

Single-Iteration Node Stress Propagation © 2024 by David Westberg is licensed under CC BY 4.0.
See full license text at: https://creativecommons.org/licenses/by/4.0/

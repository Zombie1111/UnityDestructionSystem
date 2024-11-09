<h1 align="center">Single-Iteration Node Stress Propagation by David Westberg</h1>

## Purpose

This paper propeses an algorythm to compute the stress each node in a structure would experience when node(s) `Z` is struck with velocity `X` and mass `Y` in a single iteration. This algorithm was developed for my Unity Destruction System ([GitHub Repository](https://github.com/Zombie1111/UnityDestructionSystem)),
as I couldn't find a fast, single-iteration solution for computing how forces should spread across a node structure - a requirement for my Destruction System project.

## Definitions
**MaterialMass**
The mass of a node. If the node is kinematic, `MaterialMass` should equal the mass of the connected object. If connected to a kinematic object, `MaterialMass` should equal mass `Y`. (Undefined behavior if `MaterialMass` is `infinite` or `< 0`.)

**MaterialStiffness**
The proportion of force that results in bending or heat. For example, a value of 0.6 indicates that 40% of the force will result in bending or heat.

**MaterialDamageAccumulation**
Defines how much weaker a node becomes as it takes more damage. For example, a value of 0.4 means each instance of stress makes the node 40% weaker.

**MaterialStrength**
The maximum stress that a node can withstand.

**Node**
Typically represents a bone in a mesh that controls a set of vertices.

**Node Structure**
A collection of interconnected nodes arranged in a grid-like pattern with neighboring connections.

**NodeStep**
The minimum number of steps required to reach a node in `Z` from any other node.

**NodeMass**
The mass associated with each node if the node structure is dragged or pushed by node(s) `Z`.

**NodeStrength**
The maximum stress a node can endure before breaking.

**NodeDamage**
The total accumulated damage a node has sustained over its lifetime.

**Kinematic Node**
A node connected to an object unrelated to this node structure.

**TotalHeatForce**
The portion of force converted to heat or bending (depending on `MaterialStiffness`). This value can be used to compute mesh deformation.

##The Algorythm
### Stress Distribution
Start by calculating the `NodeStrength` for each node
`NodeStrength = max(MaterialStrength - (NodeDamage * MaterialDamageAccumulation), 0.0)`

The `NodeStep` value can be calculated through a flood-fill algorithm, starting from the `Z` node(s) and expanding through their neighbors.

The `NodeMass` of each node can be computed using the following steps:
1. Determine the highest NodeStep value among the nodes (denoted as `S`).
2. For each node where `NodeStep == S`, add `MaterialMass` to `NodeMass`.
3. For each neighboring node where `NodeStep < S` (denoted as `N`), distribute `NodeMass / N count` to each `N`.
4. Decrease `S` by 1 and repeat until `S == 0`.
5. For each node in `Z`, if `NodeMass < MaterialMinMass / Z count`, set `NodeMass = MaterialMinMass / Z count`.

The maximum possible stress any given node can experience if the node(s) `Z` was struck by velocity `X` and mass `Y` can be calculated using the following formula `min(NodeMass * X, X * Y) * (MaterialStiffness ^ NodeStep)`

Computing the actuall stress each node will experience follows these steps:

1. Set `TotalHeatForce = 0`.
2. For each node in `Z`, set `NodeStress = min(NodeMass * X, X * Y)`.
3. Propagate Stress:
   - Set `S = 0`.
   - For each node where `NodeStep == S`, get all neighbors where `NodeStep > S` (denoted as `N`).
   - Calculate the `total strength (T)` and `total mass (M)` by summing the `NodeStrength` and `NodeMass` from every `N`.
   - Update `NodeStress` of each `N` with `min(T, NodeStress) * (N NodeMass / M) * MaterialStiffness`.
   - Add `(min(T, NodeStress) * (N NodeMass / M)) - N NodeStress` to `TotalHeatForce`.
   - If `T < NodeStress`, the node breaks with an excess force of `NodeStress - T`.
   - Increase `S` by 1 and repeat until no `NodeStress` is updated.

### Deformation
Given a 3D point (`A`) where a node in `Z` was struck and a 3D point (`B`) to offset we can calculate the offset amount using the following steps:
1. Set `F = sqrt(dot(B - A, B - A)) * BendFallof`
2. Increase `F` With `F * (F * BendPower)`
3. If `F <= TotalHeatForce * BendEfficiency` offset `B` with `BendAmount * clamp(((TotalHeatForce * BendEfficiency) - F) / (BendStrenght * sqrt(dot(X, X))), 0.0f, 1.0f) * X;`

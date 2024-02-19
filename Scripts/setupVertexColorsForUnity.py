bl_info = {
    "name": "Vertex Colors for Unity Fracture",
    "description": "Adds a button in Data>Vertex Groups that sets the vertex colors of the mesh to a format the fracture system uses",
    "author": "Zombie1111",
    "version": (1, 0),
    "blender": (3, 6, 0),
    "category": "Object",
}

import bpy
from random import random

def ShowMessageBox(message = "", title = "Message Box", icon = 'INFO'):

    def draw(self, context):
        self.layout.label(text=message)

    bpy.context.window_manager.popup_menu(draw, title = title, icon = icon)

# Define operator
class CustomOperator(bpy.types.Operator):
    bl_idname = "object.set_vertex_colors_for_unity"
    bl_label = "Print Vertex Groups"
    
    def execute(self, context):
         # Store the current mode to restore it later
         mode = bpy.context.active_object.mode
    
         # Switch to OBJECT mode
         bpy.ops.object.mode_set(mode='OBJECT')
    
         # Get the active object and its mesh data
         obj = bpy.context.active_object
         mesh = obj.data
         
         # create vertex group lookup dictionary for names
         glenght = len(obj.vertex_groups) * 2
         vgroup_islink = {vgroup.index: vgroup.name.startswith("link") for vgroup in obj.vertex_groups}
         vgroup_indexA = {vgroup.index: (vgroup.index + 1) / glenght for vgroup in obj.vertex_groups}
         vgroup_indexB = {index: value + 0.5 for index, value in vgroup_indexA.items()}
         
         # create dictionary of vertex group assignments per vertex
         verts_gislinks = {v.index: [vgroup_islink[g.group] for g in v.groups] for v in mesh.vertices}
         verts_gindexesA = {v.index: [vgroup_indexA[g.group] for g in v.groups] for v in mesh.vertices}
         verts_gindexesB = {v.index: [vgroup_indexB[g.group] for g in v.groups] for v in mesh.vertices}

         # Create a new vertex color layer if it doesn't exist
         if not mesh.vertex_colors:
             mesh.vertex_colors.new()
    
         # Set the vertex color for each vertex based on the vertex groups assigned to it
         vcol_layer = mesh.vertex_colors.active.data
         
         def get_color_for_vertex(vertex_index):
             newids = [0.0] * 4
             newid = 0.0
             hasBeenWarned = False
             
             for i in range(len(verts_gindexesA[vertex_index])):
                 if verts_gislinks[vertex_index][i] is False:
                     newid = verts_gindexesB[vertex_index][i]
                 else:
                     newid = verts_gindexesA[vertex_index][i]
                 
                 if newids[0] <= 0.0:   
                     newids[0] = newid
                 elif newids[1] <= 0.0:
                     newids[1] = newid
                 elif newids[2] <= 0.0:
                     newids[2] = newid
                 elif newids[3] <= 0.0:
                     newids[3] = newid
                 elif hasBeenWarned == False:
                     ShowMessageBox("No vertices should have more than 4 Groups assigned to it", "Vertex Group limit reached", 'ERROR')
                     hasBeenWarned = True

             newids_s = sorted(newids, reverse=True)
             return (newids_s[0], newids_s[1], newids_s[2], newids_s[3])  #Return group ids for the vertex

         # Map vertex indices to loop indices
         vert_loop_map = {}
         for loop in mesh.loops:
             vert_index = mesh.loops[loop.index].vertex_index
             if vert_index not in vert_loop_map:
                 vert_loop_map[vert_index] = []
             vert_loop_map[vert_index].append(loop.index)
    
         # Set vertex colors
         for vert_index, loop_indices in vert_loop_map.items():
             color = get_color_for_vertex(vert_index)
             for loop_index in loop_indices:
                 vcol_layer[loop_index].color = color
    
         # Restore the original mode
         bpy.ops.object.mode_set(mode=mode)
    
         return {'FINISHED'}
     

# Define panel
class CustomPanel(bpy.types.Panel):
    bl_label = ""
    bl_idname = "OBJECT_PT_custom_panel"
    bl_space_type = 'PROPERTIES'
    bl_region_type = 'WINDOW'
    bl_context = "data"
    bl_parent_id = "DATA_PT_vertex_groups"
    bl_options = {'HIDE_HEADER'}

    def draw(self, context):
        layout = self.layout
        layout.operator("object.set_vertex_colors_for_unity", text="Setup vertex colors for unity fracture")

# Register classes
def register():
    bpy.utils.register_class(CustomOperator)
    bpy.utils.register_class(CustomPanel)

def unregister():
    bpy.utils.unregister_class(CustomOperator)
    bpy.utils.unregister_class(CustomPanel)

def get_vertices_in_group(a,index):
    # a is a Blender object
    # index is an integer representing the index of the vertex group in a
    vlist = []
    for v in a.data.vertices:
        for g in v.groups:
            if g.group == index:
                vlist.append(v.index)
    return vlist

if __name__ == "__main__":
    register()

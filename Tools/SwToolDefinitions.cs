using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeSW.Api;
using Newtonsoft.Json.Linq;

namespace ClaudeSW.Tools
{
    public static class SwToolDefinitions
    {
        private static readonly List<ToolDefinition> Tools = new List<ToolDefinition>
        {
            ReadOnlyTool("sw_get_active_doc_info", "Document",
                "Returns info about the currently active SolidWorks document: title, path, type, and feature count.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            ReadOnlyTool("sw_get_feature_list", "Document",
                "Lists all features in the active part or assembly FeatureManager tree. Returns name, type, and suppression state for each.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            ReadOnlyTool("sw_get_model_dimensions", "Document",
                "Returns all named dimensions in the active document with their current system-unit values and types.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            ReadOnlyTool("sw_get_mass_properties", "Document",
                "Returns mass properties: volume (m^3), surface area (m^2), mass (kg), center of mass, and moments of inertia.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            MutatingTool("sw_create_sketch", "Sketch",
                "Creates a new 2D sketch on one of the standard reference planes. Must be called before adding sketch geometry.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""plane"": {
                            ""type"": ""string"",
                            ""description"": ""Reference plane name. Supported values: Front, Top, Right."",
                            ""enum"": [""Front"", ""Top"", ""Right""]
                        }
                    },
                    ""required"": [""plane""]
                }"),

            MutatingTool("sw_add_sketch_line", "Sketch",
                "Adds a line segment to the active sketch. Coordinates are in meters.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""x1"": { ""type"": ""number"" },
                        ""y1"": { ""type"": ""number"" },
                        ""x2"": { ""type"": ""number"" },
                        ""y2"": { ""type"": ""number"" }
                    },
                    ""required"": [""x1"", ""y1"", ""x2"", ""y2""]
                }"),

            MutatingTool("sw_add_sketch_circle", "Sketch",
                "Adds a circle to the active sketch. Coordinates and radius are in meters.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""cx"": { ""type"": ""number"" },
                        ""cy"": { ""type"": ""number"" },
                        ""radius"": { ""type"": ""number"" }
                    },
                    ""required"": [""cx"", ""cy"", ""radius""]
                }"),

            MutatingTool("sw_add_sketch_arc", "Sketch",
                "Adds a 3-point arc to the active sketch. Coordinates are in meters.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""cx"": { ""type"": ""number"" },
                        ""cy"": { ""type"": ""number"" },
                        ""startX"": { ""type"": ""number"" },
                        ""startY"": { ""type"": ""number"" },
                        ""endX"": { ""type"": ""number"" },
                        ""endY"": { ""type"": ""number"" },
                        ""direction"": { ""type"": ""integer"", ""enum"": [1, -1] }
                    },
                    ""required"": [""cx"", ""cy"", ""startX"", ""startY"", ""endX"", ""endY"", ""direction""]
                }"),

            MutatingTool("sw_add_sketch_rectangle", "Sketch",
                "Adds a corner rectangle to the active sketch. Coordinates are in meters.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""x1"": { ""type"": ""number"" },
                        ""y1"": { ""type"": ""number"" },
                        ""x2"": { ""type"": ""number"" },
                        ""y2"": { ""type"": ""number"" }
                    },
                    ""required"": [""x1"", ""y1"", ""x2"", ""y2""]
                }"),

            MutatingTool("sw_close_sketch", "Sketch",
                "Closes the currently active sketch.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            MutatingTool("sw_extrude", "Feature",
                "Creates a boss-extrude feature from the last closed sketch profile. Supported directions are one_direction and both_directions.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""depth"": { ""type"": ""number"" },
                        ""direction"": { ""type"": ""string"", ""enum"": [""one_direction"", ""both_directions""] },
                        ""draft_angle_deg"": { ""type"": ""number"", ""default"": 0 },
                        ""merge_result"": { ""type"": ""boolean"", ""default"": true }
                    },
                    ""required"": [""depth""]
                }"),

            MutatingTool("sw_cut_extrude", "Feature",
                "Creates a cut-extrude feature from the last closed sketch profile.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""depth"": { ""type"": ""number"" },
                        ""through_all"": { ""type"": ""boolean"", ""default"": false },
                        ""flip_direction"": { ""type"": ""boolean"", ""default"": false }
                    },
                    ""required"": [""depth""]
                }"),

            MutatingTool("sw_revolve", "Feature",
                "Creates a revolve feature from the last closed sketch profile around the selected or existing sketch centerline.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""angle_deg"": { ""type"": ""number"" }
                    },
                    ""required"": [""angle_deg""]
                }"),

            MutatingTool("sw_fillet", "Feature",
                "Applies fillet to the edges currently selected in SolidWorks. Radius is in meters.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""radius"": { ""type"": ""number"" }
                    },
                    ""required"": [""radius""]
                }"),

            MutatingTool("sw_chamfer", "Feature",
                "Applies chamfer to the edges currently selected in SolidWorks. Distance is in meters.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""distance"": { ""type"": ""number"" }
                    },
                    ""required"": [""distance""]
                }"),

            MutatingTool("sw_new_part", "File",
                "Creates a new empty part document in SolidWorks using the default part template.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            MutatingTool("sw_new_assembly", "File",
                "Creates a new empty assembly document in SolidWorks using the default assembly template.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            MutatingTool("sw_save", "File",
                "Saves the active document. Optionally saves as a new path.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""filepath"": { ""type"": ""string"" }
                    },
                    ""required"": []
                }"),

            MutatingTool("sw_export_step", "File",
                "Exports the active document as a STEP file.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""filepath"": { ""type"": ""string"" }
                    },
                    ""required"": [""filepath""]
                }"),

            MutatingTool("sw_add_dimension", "Dimension",
                "Changes an existing named dimension value. Use the dimension name from sw_get_model_dimensions.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""dimension_name"": { ""type"": ""string"" },
                        ""value"": { ""type"": ""number"" }
                    },
                    ""required"": [""dimension_name"", ""value""]
                }"),

            ReadOnlyTool("sw_zoom_to_fit", "View",
                "Zooms the current viewport to fit all geometry.",
                @"{ ""type"": ""object"", ""properties"": {}, ""required"": [] }"),

            ReadOnlyTool("sw_set_view", "View",
                "Sets the viewport to a standard view orientation.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""orientation"": { ""type"": ""string"", ""enum"": [""front"", ""back"", ""top"", ""bottom"", ""left"", ""right"", ""isometric"", ""trimetric""] }
                    },
                    ""required"": [""orientation""]
                }")
        };

        public static List<ToolDefinition> GetAllTools()
        {
            return Tools.Select(Clone).ToList();
        }

        public static ToolDefinition FindByName(string name)
        {
            var tool = Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            return tool == null ? null : Clone(tool);
        }

        private static ToolDefinition ReadOnlyTool(string name, string category, string description, string schema)
        {
            return new ToolDefinition
            {
                Name = name,
                Category = category,
                Description = description,
                InputSchema = JObject.Parse(schema),
                IsReadOnly = true,
                RequiresConfirmation = false
            };
        }

        private static ToolDefinition MutatingTool(string name, string category, string description, string schema)
        {
            return new ToolDefinition
            {
                Name = name,
                Category = category,
                Description = description,
                InputSchema = JObject.Parse(schema),
                IsReadOnly = false,
                RequiresConfirmation = true
            };
        }

        private static ToolDefinition Clone(ToolDefinition tool)
        {
            return new ToolDefinition
            {
                Name = tool.Name,
                Category = tool.Category,
                Description = tool.Description,
                InputSchema = tool.InputSchema != null ? JObject.Parse(tool.InputSchema.ToString()) : null,
                IsReadOnly = tool.IsReadOnly,
                RequiresConfirmation = tool.RequiresConfirmation
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaudeSW.Tools
{
    public class SwToolExecutor
    {
        private readonly dynamic _swApp;
        private readonly Func<Action, Task> _marshalToSwThread;

        public SwToolExecutor(object swApp, Func<Action, Task> marshalToSwThread)
        {
            _swApp = swApp;
            _marshalToSwThread = marshalToSwThread;
        }

        public async Task<(string result, bool isError)> ExecuteAsync(string toolName, JObject input)
        {
            try
            {
                string result = null;

                await _marshalToSwThread(() =>
                {
                    result = DispatchTool(toolName, input ?? new JObject());
                });

                return (result, IsErrorResult(result));
            }
            catch (Exception ex)
            {
                return (JsonError("Unhandled tool execution error.", toolName, ex.Message), true);
            }
        }

        public async Task<string> GetActiveDocumentIdentityAsync()
        {
            string identity = "(no-document)";

            await _marshalToSwThread(() =>
            {
                var doc = _swApp.ActiveDoc;
                if (doc == null)
                {
                    identity = "(no-document)";
                    return;
                }

                identity = string.Format(
                    "{0}|{1}|{2}",
                    MapDocType((int)doc.GetType()),
                    (string)doc.GetTitle(),
                    (string)doc.GetPathName());
            });

            return identity;
        }

        private string DispatchTool(string toolName, JObject input)
        {
            switch (toolName)
            {
                case "sw_get_active_doc_info": return GetActiveDocInfo();
                case "sw_get_feature_list": return GetFeatureList();
                case "sw_get_model_dimensions": return GetModelDimensions();
                case "sw_get_mass_properties": return GetMassProperties();
                case "sw_create_sketch": return CreateSketch(input);
                case "sw_add_sketch_line": return AddSketchLine(input);
                case "sw_add_sketch_circle": return AddSketchCircle(input);
                case "sw_add_sketch_arc": return AddSketchArc(input);
                case "sw_add_sketch_rectangle": return AddSketchRectangle(input);
                case "sw_close_sketch": return CloseSketch();
                case "sw_extrude": return Extrude(input);
                case "sw_cut_extrude": return CutExtrude(input);
                case "sw_revolve": return Revolve(input);
                case "sw_fillet": return Fillet(input);
                case "sw_chamfer": return Chamfer(input);
                case "sw_new_part": return NewPart();
                case "sw_new_assembly": return NewAssembly();
                case "sw_save": return Save(input);
                case "sw_export_step": return ExportStep(input);
                case "sw_add_dimension": return AddDimension(input);
                case "sw_zoom_to_fit": return ZoomToFit();
                case "sw_set_view": return SetView(input);
                default: throw new NotSupportedException("Unknown tool: " + toolName);
            }
        }

        private string GetActiveDocInfo()
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null)
                return JsonError("No active document open in SolidWorks.");

            return JsonConvert.SerializeObject(new
            {
                status = "ok",
                title = (string)doc.GetTitle(),
                path = (string)doc.GetPathName(),
                type = MapDocType((int)doc.GetType()),
                featureCount = CountFeatures(doc)
            });
        }

        private string GetFeatureList()
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            var features = new List<object>();
            dynamic feat = doc.FirstFeature();
            while (feat != null)
            {
                features.Add(new
                {
                    name = (string)feat.Name,
                    type = (string)feat.GetTypeName2(),
                    suppressed = (bool)feat.IsSuppressed()
                });
                feat = feat.GetNextFeature();
            }

            return JsonConvert.SerializeObject(new { status = "ok", features = features });
        }

        private string GetModelDimensions()
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            var dims = new List<object>();
            dynamic feat = doc.FirstFeature();
            while (feat != null)
            {
                dynamic dispDim = feat.GetFirstDisplayDimension();
                while (dispDim != null)
                {
                    dynamic dim = dispDim.GetDimension2(0);
                    if (dim != null)
                    {
                        dims.Add(new
                        {
                            name = (string)dim.FullName,
                            value = (double)dim.GetSystemValue3(1, null)[0],
                            type = (string)dim.GetTypeName()
                        });
                    }
                    dispDim = feat.GetNextDisplayDimension(dispDim);
                }
                feat = feat.GetNextFeature();
            }

            return JsonConvert.SerializeObject(new { status = "ok", dimensions = dims });
        }

        private string GetMassProperties()
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            dynamic ext = doc.Extension;
            int status = 0;
            double[] props = (double[])ext.GetMassProperties(1, ref status);
            if (props == null || status != 0)
                return JsonError("Mass properties unavailable. Status: " + status);

            return JsonConvert.SerializeObject(new
            {
                status = "ok",
                centerOfMass = new { x = props[0], y = props[1], z = props[2] },
                volume_m3 = props[3],
                surfaceArea_m2 = props[4],
                mass_kg = props[5]
            });
        }

        private string CreateSketch(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            string plane = input.Value<string>("plane") ?? "Front";
            bool selected = (bool)doc.Extension.SelectByID2(plane + " Plane", "PLANE", 0, 0, 0, false, 0, null, 0);
            if (!selected)
                selected = (bool)doc.Extension.SelectByID2(plane, "PLANE", 0, 0, 0, false, 0, null, 0);
            if (!selected)
                return JsonError("Could not select plane: " + plane);

            doc.SketchManager.InsertSketch(true);
            return JsonConvert.SerializeObject(new { status = "ok", message = "Sketch opened on " + plane + " plane." });
        }

        private string AddSketchLine(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            dynamic seg = doc.SketchManager.CreateLine(
                input.Value<double>("x1"),
                input.Value<double>("y1"),
                0,
                input.Value<double>("x2"),
                input.Value<double>("y2"),
                0);

            if (seg == null)
                return JsonError("Failed to create sketch line. Is a sketch active?");

            return JsonConvert.SerializeObject(new { status = "ok", entity = "line" });
        }

        private string AddSketchCircle(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            double cx = input.Value<double>("cx");
            double cy = input.Value<double>("cy");
            double radius = input.Value<double>("radius");
            dynamic seg = doc.SketchManager.CreateCircle(cx, cy, 0, cx + radius, cy, 0);
            if (seg == null)
                return JsonError("Failed to create circle. Is a sketch active?");

            return JsonConvert.SerializeObject(new { status = "ok", entity = "circle" });
        }

        private string AddSketchArc(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            dynamic seg = doc.SketchManager.CreateArc(
                input.Value<double>("cx"),
                input.Value<double>("cy"),
                0,
                input.Value<double>("startX"),
                input.Value<double>("startY"),
                0,
                input.Value<double>("endX"),
                input.Value<double>("endY"),
                0,
                (short)input.Value<int>("direction"));

            if (seg == null)
                return JsonError("Failed to create arc. Is a sketch active?");

            return JsonConvert.SerializeObject(new { status = "ok", entity = "arc" });
        }

        private string AddSketchRectangle(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            dynamic segs = doc.SketchManager.CreateCornerRectangle(
                input.Value<double>("x1"),
                input.Value<double>("y1"),
                0,
                input.Value<double>("x2"),
                input.Value<double>("y2"),
                0);

            if (segs == null)
                return JsonError("Failed to create rectangle. Is a sketch active?");

            return JsonConvert.SerializeObject(new { status = "ok", entity = "rectangle" });
        }

        private string CloseSketch()
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            doc.SketchManager.InsertSketch(true);
            return JsonConvert.SerializeObject(new { status = "ok", message = "Sketch closed." });
        }

        private string Extrude(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            double depth = input.Value<double>("depth");
            string direction = input.Value<string>("direction") ?? "one_direction";
            bool bothDirections = string.Equals(direction, "both_directions", StringComparison.OrdinalIgnoreCase);
            double draftDeg = input.Value<double?>("draft_angle_deg") ?? 0;
            bool merge = input.Value<bool?>("merge_result") ?? true;
            double draftRad = draftDeg * Math.PI / 180.0;

            dynamic feat = doc.FeatureManager.FeatureExtrusion3(
                true, false, bothDirections,
                0, 0,
                depth, bothDirections ? depth : 0,
                draftDeg > 0, false,
                draftRad, 0,
                false, false,
                false, merge,
                true, true,
                0, 0, false,
                false, false);

            if (feat == null)
                return JsonError("Extrude failed. Ensure sketch has a closed profile.");

            return JsonConvert.SerializeObject(new { status = "ok", feature = "Boss-Extrude", depth_m = depth, direction = direction });
        }

        private string CutExtrude(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            double depth = input.Value<double>("depth");
            bool throughAll = input.Value<bool?>("through_all") ?? false;
            bool flip = input.Value<bool?>("flip_direction") ?? false;
            int endCond = throughAll ? 1 : 0;

            dynamic feat = doc.FeatureManager.FeatureCut4(
                true, false, flip,
                endCond, 0,
                depth, 0,
                false, false, false, false,
                0, 0,
                false, false, false, false,
                false, true, true,
                true, true, false,
                0, 0, false, false);

            if (feat == null)
                return JsonError("Cut-extrude failed. Ensure sketch has a closed profile on a face.");

            return JsonConvert.SerializeObject(new { status = "ok", feature = "Cut-Extrude", depth_m = depth, through_all = throughAll });
        }

        private string Revolve(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            double angleDeg = input.Value<double>("angle_deg");
            double angleRad = angleDeg * Math.PI / 180.0;

            dynamic feat = doc.FeatureManager.FeatureRevolve2(
                true, true, false,
                false, false, false,
                0, 0,
                angleRad, 0,
                false, false,
                0, 0,
                0, 0, 0,
                true, true, true);

            if (feat == null)
                return JsonError("Revolve failed. Ensure sketch has a closed profile and a centerline.");

            return JsonConvert.SerializeObject(new { status = "ok", feature = "Revolve", angle_deg = angleDeg });
        }

        private string Fillet(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            double radius = input.Value<double>("radius");
            dynamic feat = doc.FeatureManager.FeatureFillet3(195, radius, 0, 0, 0, 0, 0, null, null, null, null, null, null, null);
            if (feat == null)
                return JsonError("Fillet failed. Select edges first or reduce the radius.");

            return JsonConvert.SerializeObject(new { status = "ok", feature = "Fillet", radius_m = radius });
        }

        private string Chamfer(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            double distance = input.Value<double>("distance");
            dynamic feat = doc.FeatureManager.InsertFeatureChamfer(4, 1, distance, 0, 0, 0, 0, 0);
            if (feat == null)
                return JsonError("Chamfer failed. Select edges first.");

            return JsonConvert.SerializeObject(new { status = "ok", feature = "Chamfer", distance_m = distance });
        }

        private string NewPart()
        {
            string templatePath = _swApp.GetUserPreferenceStringValue(7);
            if (string.IsNullOrEmpty(templatePath))
                templatePath = "C:\\ProgramData\\SolidWorks\\SOLIDWORKS 2024\\templates\\Part.prtdot";

            dynamic doc = _swApp.NewDocument(templatePath, 0, 0, 0);
            if (doc == null)
                return JsonError("Failed to create new part. Template: " + templatePath);

            return JsonConvert.SerializeObject(new { status = "ok", document = "New Part", template = templatePath });
        }

        private string NewAssembly()
        {
            string templatePath = _swApp.GetUserPreferenceStringValue(8);
            if (string.IsNullOrEmpty(templatePath))
                templatePath = "C:\\ProgramData\\SolidWorks\\SOLIDWORKS 2024\\templates\\Assembly.asmdot";

            dynamic doc = _swApp.NewDocument(templatePath, 0, 0, 0);
            if (doc == null)
                return JsonError("Failed to create new assembly. Template: " + templatePath);

            return JsonConvert.SerializeObject(new { status = "ok", document = "New Assembly", template = templatePath });
        }

        private string Save(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            string path = input.Value<string>("filepath");
            int errors = 0;
            int warnings = 0;

            if (string.IsNullOrWhiteSpace(path))
            {
                bool ok = (bool)doc.Save3(1, ref errors, ref warnings);
                if (!ok)
                    return JsonError("Save failed. Errors: " + errors + ", warnings: " + warnings);
            }
            else
            {
                int result = (int)doc.SaveAs3(path, 0, 2);
                if (result != 0)
                    return JsonError("SaveAs failed (" + result + ") to: " + path);
            }

            return JsonConvert.SerializeObject(new
            {
                status = "ok",
                saved_to = string.IsNullOrWhiteSpace(path) ? (string)doc.GetPathName() : path
            });
        }

        private string ExportStep(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            string path = input.Value<string>("filepath");
            if (string.IsNullOrWhiteSpace(path))
                return JsonError("filepath is required for STEP export.");

            int errors = 0;
            int warnings = 0;
            bool ok = (bool)doc.Extension.SaveAs2(path, 0, 1, null, "", false, ref errors, ref warnings);
            if (!ok)
                return JsonError("STEP export failed. Errors: " + errors + ", warnings: " + warnings);

            return JsonConvert.SerializeObject(new { status = "ok", exported_to = path });
        }

        private string AddDimension(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            string dimensionName = input.Value<string>("dimension_name");
            double value = input.Value<double>("value");
            dynamic dim = doc.Parameter(dimensionName);
            if (dim == null)
                return JsonError("Dimension '" + dimensionName + "' not found.");

            dim.SetSystemValue3(value, 1, null);
            doc.EditRebuild3();
            return JsonConvert.SerializeObject(new { status = "ok", dimension = dimensionName, new_value = value, rebuild = true });
        }

        private string ZoomToFit()
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            doc.ViewZoomtofit2();
            return JsonConvert.SerializeObject(new { status = "ok" });
        }

        private string SetView(JObject input)
        {
            var doc = _swApp.ActiveDoc;
            if (doc == null) return JsonError("No active document.");

            string orientation = (input.Value<string>("orientation") ?? "isometric").ToLowerInvariant();
            var viewMap = new Dictionary<string, int>
            {
                { "front", 1 },
                { "back", 2 },
                { "left", 3 },
                { "right", 4 },
                { "top", 5 },
                { "bottom", 6 },
                { "isometric", 7 },
                { "trimetric", 8 }
            };

            if (!viewMap.ContainsKey(orientation))
                return JsonError("Unknown orientation: " + orientation);

            doc.ShowNamedView2("", viewMap[orientation]);
            doc.ViewZoomtofit2();
            return JsonConvert.SerializeObject(new { status = "ok", view = orientation });
        }

        private static string JsonError(string message, string tool = null, string detail = null)
        {
            return JsonConvert.SerializeObject(new
            {
                status = "error",
                tool = tool,
                message = message,
                detail = detail
            }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private static bool IsErrorResult(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return true;

            try
            {
                var obj = JObject.Parse(json);
                return string.Equals((string)obj["status"], "error", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string MapDocType(int type)
        {
            switch (type)
            {
                case 1: return "part";
                case 2: return "assembly";
                case 3: return "drawing";
                default: return "unknown(" + type + ")";
            }
        }

        private static int CountFeatures(dynamic doc)
        {
            int count = 0;
            dynamic feat = doc.FirstFeature();
            while (feat != null)
            {
                count++;
                feat = feat.GetNextFeature();
            }
            return count;
        }
    }
}

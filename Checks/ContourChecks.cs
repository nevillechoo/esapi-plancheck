using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Checks
{
    public class StructureSetCheck : PlanCheckBase
    {
        public override string Title => "Structure Set Verification";
        public override string Category => "Contours";
        public override string Description => "Verifies the structure set contains a 'Couch' and identifies empty structures. Limitation: Does not verify if the couch is correctly positioned relative to the patient.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var ss = plan.StructureSet;
            var messages = new List<string>();
            var status = CheckStatus.Pass;
            bool hasCouch = ss.Structures.Any(s => s.Id.IndexOf("couch", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!hasCouch)
            {
                messages.Add("No 'Couch' structure found.");
                status = CheckStatus.Warning;
            }
            var emptyStructures = ss.Structures.Where(s => s.IsEmpty).Select(s => s.Id).ToList();
            if (emptyStructures.Any())
            {
                messages.Add($"Empty structures found: {string.Join(", ", emptyStructures)}");
                status = CheckStatus.Warning;
            }

            if (status == CheckStatus.Pass)
                return Pass("Structure set looks nominal.");
            
            return CreateResult(status, string.Join("\n", messages));
        }
    }

    public class NamingConventionCheck : PlanCheckBase
    {
        public override string Title => "Naming Convention";
        public override string Category => "Contours";
        public override string Description => "Flags structures with all-lowercase IDs as likely non-standard. Limitation: Simple string heuristic; does not check against a comprehensive clinical naming database.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var expectedNames = new[] { "PTV", "CTV", "GTV", "Body", "Rectum", "Bladder" };
            var structures = plan.StructureSet.Structures.Select(s => s.Id).ToList();
            
            var warnings = new List<string>();
            foreach (var s in structures)
            {
                if (s == s.ToLower() && s != "body") 
                {
                    warnings.Add($"Structure '{s}' is all lowercase. Verify naming.");
                }
            }

            if (warnings.Any())
                return Warning("Potential naming issues.", string.Join("\n", warnings));

            return Pass("Naming conventions appear standard.");
        }
    }

    public class ContourContinuityCheck : PlanCheckBase
    {
        public override string Title => "Contour Continuity";
        public override string Category => "Contours";
        public override string Description => "Checks for missing slices (gaps) within the drawn range of a structure. Limitation: Checks slice-by-slice presence; does not detect very small holes within a single slice.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var ss = plan.StructureSet;
            var image = ss.Image;
            int zSize = image.ZSize;
            var warnings = new List<string>();

            foreach (var structure in ss.Structures)
            {
                if (structure.IsEmpty || structure.DicomType == "MARKER" || structure.Id.StartsWith("z", StringComparison.OrdinalIgnoreCase)) 
                    continue;
                int minZ = -1;
                int maxZ = -1;
                for (int z = 0; z < zSize; z++)
                {
                    var contours = structure.GetContoursOnImagePlane(z);
                    if (contours.Length > 0)
                    {
                        if (minZ == -1) minZ = z;
                        maxZ = z;
                    }
                }

                if (minZ == -1) continue;
                var gaps = new List<int>();
                for (int z = minZ; z <= maxZ; z++)
                {
                    var contours = structure.GetContoursOnImagePlane(z);
                    if (contours.Length == 0)
                    {
                        gaps.Add(z);
                    }
                }

                if (gaps.Any())
                {
                    var gapDetails = gaps.Select(z => GetSlicePositionString(z, image)).ToList();
                    string gapStr = string.Join(", ", gapDetails.Take(5));
                    if (gaps.Count > 5) gapStr += ", ...";
                    warnings.Add($"Structure '{structure.Id}' has gaps on slices: {gapStr}");
                }
            }

            if (warnings.Any())
            {
                return Warning("Contour gaps detected.", string.Join("\n", warnings));
            }

            return Pass("No contour gaps detected in non-empty structures.");
        }
    }

    public class ContourContiguityCheck : PlanCheckBase
    {
        public override string Title => "Contour Contiguity";
        public override string Category => "Contours";
        public override string Description => "Flags structures that have more than one disjoint contour on any single axial slice. Limitation: Does not distinguish between intended disjoint parts (e.g., bilateral lungs) and errors.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var ss = plan.StructureSet;
            var image = ss.Image;
            int zSize = image.ZSize;
            var warnings = new List<string>();

            foreach (var structure in ss.Structures)
            {
                if (structure.IsEmpty || structure.DicomType == "MARKER") 
                    continue;

                var disjointSlices = new List<int>();

                for (int z = 0; z < zSize; z++)
                {
                    var contours = structure.GetContoursOnImagePlane(z);
                    if (contours.Length > 1)
                    {
                        disjointSlices.Add(z);
                    }
                }

                if (disjointSlices.Any())
                {
                    var sliceDetails = disjointSlices.Select(z => GetSlicePositionString(z, image)).ToList();
                    string sliceStr = string.Join(", ", sliceDetails.Take(5));
                    if (disjointSlices.Count > 5) sliceStr += ", ...";
                    warnings.Add($"Structure '{structure.Id}' has multiple contours on slices: {sliceStr}");
                }
            }

            if (warnings.Any())
            {
                return Warning("Disjoint contours detected on single slices.", string.Join("\n", warnings));
            }

            return Pass("No disjoint contours detected on single slices.");
        }
    }
    public class ContourIslandsCheck : PlanCheckBase
    {
        public override string Title => "Contour Islands";
        public override string Category => "Contours";
        public override string Description => "Counts independent 3D 'islands' in the mesh. Limitation: High computational cost for complex meshes; may flag intended separate parts as warnings.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var ss = plan.StructureSet;
            var warnings = new List<string>();

            foreach (var structure in ss.Structures)
            {
                if (structure.IsEmpty || structure.DicomType == "MARKER" || structure.DicomType == "SUPPORT" || structure.Id.StartsWith("z", StringComparison.OrdinalIgnoreCase))
                    continue;
                var mesh = structure.MeshGeometry;
                if (mesh == null || mesh.Positions.Count == 0) continue;

                var islands = CountIslands(mesh);
                if (islands > 1)
                {
                    warnings.Add($"Structure '{structure.Id}' has {islands} disjoint islands.");
                }
            }

            if (warnings.Any())
            {
                return Warning("Structures with multiple islands found.", string.Join("\n", warnings));
            }

            return Pass("No multiple islands found in structures.");
        }

        private int CountIslands(System.Windows.Media.Media3D.MeshGeometry3D mesh)
        {
            int vertexCount = mesh.Positions.Count;
            if (vertexCount == 0) return 0;
            var adjacency = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                adjacency[i] = new List<int>();
            }

            var indices = mesh.TriangleIndices;
            for (int i = 0; i < indices.Count; i += 3)
            {
                int v1 = indices[i];
                int v2 = indices[i + 1];
                int v3 = indices[i + 2];

                adjacency[v1].Add(v2);
                adjacency[v1].Add(v3);
                adjacency[v2].Add(v1);
                adjacency[v2].Add(v3);
                adjacency[v3].Add(v1);
                adjacency[v3].Add(v2);
            }
            var visited = new bool[vertexCount];
            int islandCount = 0;

            for (int i = 0; i < vertexCount; i++)
            {
                if (!visited[i])
                {
                    islandCount++;
                    var queue = new Queue<int>();
                    queue.Enqueue(i);
                    visited[i] = true;

                    while (queue.Count > 0)
                    {
                        int current = queue.Dequeue();
                        foreach (var neighbor in adjacency[current])
                        {
                            if (!visited[neighbor])
                            {
                                visited[neighbor] = true;
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }

            return islandCount;
        }
    }

    public class ContourOverlapCheck : PlanCheckBase
    {
        public override string Title => "OAR Contour Overlap";
        public override string Category => "Contours";
        public override string Description => "Checks for geometric intersection between OARs. Limitation: Uses 1mm^2 area threshold for overlap; excludes targets and external body by name/type keywords.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var ss = plan.StructureSet;
            var image = ss.Image;
            int zSize = image.ZSize;
            var invalidOverlaps = new List<string>();
            var oars = ss.Structures
                .Where(s => !s.IsEmpty && IsPotentialOAR(s))
                .ToList();

            if (oars.Count < 2)
            {
                return Pass("Not enough OARs to check for overlap.");
            }
            for (int i = 0; i < oars.Count; i++)
            {
                for (int j = i + 1; j < oars.Count; j++)
                {
                    var s1 = oars[i];
                    var s2 = oars[j];
                    if (!BoundsOverlap(s1, s2))
                        continue;
                    var overlappingSlices = new List<int>();

                    for (int z = 0; z < zSize; z++)
                    {
                        var cont1 = s1.GetContoursOnImagePlane(z);
                        if (cont1.Length == 0) continue;

                        var cont2 = s2.GetContoursOnImagePlane(z);
                        if (cont2.Length == 0) continue;

                        if (CheckContourOverlap(cont1, cont2))
                        {
                            overlappingSlices.Add(z);
                        }
                    }

                    if (overlappingSlices.Any())
                    {
                        var sliceDetails = overlappingSlices.Select(z => GetSlicePositionString(z, image)).ToList();
                        string slices = string.Join(", ", sliceDetails.Take(5));
                        if(overlappingSlices.Count > 5) slices += "...";
                        invalidOverlaps.Add($"{s1.Id} intersects {s2.Id} on slices: {slices}");
                    }
                }
            }

            if (invalidOverlaps.Any())
            {
                return Warning("Overlapping OARs detected.", string.Join("\n", invalidOverlaps));
            }

            return Pass("No overlapping OARs found.");
        }

        private bool IsPotentialOAR(Structure s)
        {
            var id = s.Id.ToUpper();
            var type = s.DicomType.ToUpper();

            if (type.Contains("PTV") || type.Contains("CTV") || type.Contains("GTV") || type == "EXTERNAL" 
                || type == "SUPPORT" || type == "FIXATION" || type == "MARKER" || type == "TREATED_VOLUME"
                || type == "IRRAD_VOLUME" || type == "BOLUS")
                return false;

            if (id.Contains("PTV") || id.Contains("CTV") || id.Contains("GTV") || id.Contains("BODY") 
                || id.Contains("EXT") || id.StartsWith("Z") || id.Contains("COUCH"))
                return false;

            return true;
        }

        private bool BoundsOverlap(Structure s1, Structure s2)
        {
            var m1 = s1.MeshGeometry;
            var m2 = s2.MeshGeometry;
            
            if (m1 == null || m2 == null) return false;
            if (m1.Positions.Count == 0 || m2.Positions.Count == 0) return true;

            return m1.Bounds.IntersectsWith(m2.Bounds);
        }

        private bool CheckContourOverlap(VVector[][] contours1, VVector[][] contours2)
        {
            var g1 = CreateGeometry(contours1);
            var g2 = CreateGeometry(contours2);
            var combined = Geometry.Combine(g1, g2, GeometryCombineMode.Intersect, null);
            return combined.GetArea() > 0.01; 
        }
    }

    public class TargetHierarchyCheck : PlanCheckBase
    {
        public override string Title => "Target Hierarchy (GTV<CTV<PTV)";
        public override string Category => "Contours";
        public override string Description => "Verifies geometric containment (GTV inside CTV inside PTV). Limitation: Matches structures by suffix; containment check is slice-based with 1mm^2 tolerance.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var checkDetails = new List<string>();
            try
            {
                var ss = plan.StructureSet;
                var invalidHierarchy = new List<string>();
                var ptvs = ss.Structures.Where(s => s.Id.IndexOf("PTV", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                checkDetails.Add($"Found {ptvs.Count} structure(s) containing 'PTV'.");

                if (!ptvs.Any())
                {
                    return NA("No PTVs found (Checked for names containing 'PTV').", string.Join("\n", checkDetails));
                }

                foreach (var ptv in ptvs)
                {
                    checkDetails.Add($"\nChecking Hierarchy for: {ptv.Id}");

                    var ctv = FindMatch(ss, ptv.Id, "PTV", "CTV", checkDetails);
                    var gtv = FindMatch(ss, ptv.Id, "PTV", "GTV", checkDetails);

                    bool ctvMissing = (ctv == null);
                    bool gtvMissing = (gtv == null);

                    if (ctvMissing)
                    {
                        string expected = Regex.Replace(ptv.Id, "PTV", "CTV", RegexOptions.IgnoreCase);
                        string msg = $"CTV not found for {ptv.Id} (Expected: {expected} or similar)";
                        invalidHierarchy.Add(msg);
                        checkDetails.Add($"  ! {msg}");
                    }
                    
                    if (gtvMissing)
                    {
                        string expected = Regex.Replace(ptv.Id, "PTV", "GTV", RegexOptions.IgnoreCase);
                        string msg = $"GTV not found for {ptv.Id} (Expected: {expected} or similar)";
                        invalidHierarchy.Add(msg);
                        checkDetails.Add($"  ! {msg}");
                    }
                    if (!gtvMissing && !ctvMissing)
                    {
                        var failures = CheckContainment(gtv, ctv, ss.Image);
                        if (failures.Count > 0)
                        {
                            string violationMsg = $"Violation: {gtv.Id} extends outside {ctv.Id}: {string.Join("; ", failures.Take(3))}";
                            if (failures.Count > 3) violationMsg += " ...";
                            invalidHierarchy.Add(violationMsg);
                            checkDetails.Add($"  ! {violationMsg}");
                        }
                        else
                        {
                            checkDetails.Add($"  -> OK: {gtv.Id} is inside {ctv.Id}");
                        }
                    }
                    if (!ctvMissing)
                    {
                        var failures = CheckContainment(ctv, ptv, ss.Image);
                        if (failures.Count > 0)
                        {
                             string violationMsg = $"Violation: {ctv.Id} extends outside {ptv.Id}: {string.Join("; ", failures.Take(3))}";
                             if (failures.Count > 3) violationMsg += " ...";
                             invalidHierarchy.Add(violationMsg);
                             checkDetails.Add($"  ! {violationMsg}");
                        }
                        else
                        {
                            checkDetails.Add($"  -> OK: {ctv.Id} is inside {ptv.Id}");
                        }
                    }
                    else if (!gtvMissing)
                    {
                        var failures = CheckContainment(gtv, ptv, ss.Image);
                        if (failures.Count > 0)
                        {
                             string violationMsg = $"Violation: {gtv.Id} extends outside {ptv.Id}: {string.Join("; ", failures.Take(3))}";
                             if (failures.Count > 3) violationMsg += " ...";
                             invalidHierarchy.Add(violationMsg);
                             checkDetails.Add($"  ! {violationMsg}");
                        }
                        else
                        {
                            checkDetails.Add($"  -> OK: {gtv.Id} is inside {ptv.Id}");
                        }
                    }
                }

                string fullDetails = string.Join("\n", checkDetails);

                if (invalidHierarchy.Any())
                {
                    return Warning($"Target hierarchy violations found checked {ptvs.Count} PTVs.", fullDetails); 
                }

                return Pass($"Target hierarchy consistent for {ptvs.Count} PTVs.", fullDetails);
            }
            catch (Exception ex)
            {
                checkDetails.Add($"CRITICAL ERROR: {ex.Message}");
                checkDetails.Add(ex.StackTrace);
                return Fail($"Script Crashed: {ex.Message}", string.Join("\n", checkDetails));
            }
        }

        private Structure FindMatch(StructureSet ss, string ptvId, string ptvTag, string targetTag, List<string> logs)
        {
            string strictName = Regex.Replace(ptvId, ptvTag, targetTag, RegexOptions.IgnoreCase);
            var match = ss.Structures.FirstOrDefault(s => s.Id.Equals(strictName, StringComparison.OrdinalIgnoreCase));
            
            if (match != null)
            {
                logs.Add($"  - Found {targetTag} (Strict): {match.Id}");
                return match;
            }
            int index = ptvId.LastIndexOf(ptvTag, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                string suffix = ptvId.Substring(index);
                string targetSuffix = Regex.Replace(suffix, ptvTag, targetTag, RegexOptions.IgnoreCase);
                match = ss.Structures.FirstOrDefault(s => s.Id.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase));
                
                if (match != null)
                {
                    logs.Add($"  - Found {targetTag} (Fuzzy Suffix '{targetSuffix}'): {match.Id}");
                    return match;
                }
            }
            
            logs.Add($"  - Searching for {targetTag} failed. (Strict: {strictName}, Fuzzy match attempted)");
            return null;
        }

        private List<string> CheckContainment(Structure inner, Structure outer, Image image)
        {
            var failures = new List<string>();
            int zSize = image.ZSize;
            
            for (int z = 0; z < zSize; z++)
            {
                var innerCont = inner.GetContoursOnImagePlane(z);
                if (innerCont.Length == 0) continue;

                var outerCont = outer.GetContoursOnImagePlane(z);
                
                if (outerCont.Length == 0)
                {
                    failures.Add($"{GetSlicePositionString(z, image)} ({outer.Id} missing)");
                    continue;
                }

                double spillArea = CalculateSpillArea(innerCont, outerCont);
                if (spillArea > 1.0)
                {
                    failures.Add($"{GetSlicePositionString(z, image)} (Spill: {spillArea:F2}mm²)");
                }
            }
            return failures;
        }

        private double CalculateSpillArea(VVector[][] inner, VVector[][] outer)
        {
            var gInner = CreateGeometry(inner);
            var gOuter = CreateGeometry(outer);
            var diff = Geometry.Combine(gInner, gOuter, GeometryCombineMode.Exclude, null);
            return diff.GetArea();
        }
    }

    public class SmallTargetCheck : PlanCheckBase
    {
        public override string Title => "Small Target Verification";
        public override string Category => "Contours";
        public override string Description => "Warns if the target's bounding box max dimension is <= 2cm. Limitation: Based on MeshGeometry bounds; does not perform detailed volumetric analysis.";

        public override CheckResult Execute(PlanSetup plan)
        {
            string targetId = plan.TargetVolumeID;
            if (string.IsNullOrEmpty(targetId))
            {
                return NA("No Target Volume ID defined in plan.");
            }

            var ss = plan.StructureSet;
            var target = ss.Structures.FirstOrDefault(s => s.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                return NA($"Target structure '{targetId}' not found in structure set.");
            }

            if (target.IsEmpty)
            {
                return Fail($"Target structure '{targetId}' is empty.");
            }
            var mesh = target.MeshGeometry;
            if (mesh == null || mesh.Positions.Count == 0)
            {
                return NA($"Could not retrieve mesh geometry for '{targetId}'.");
            }

            var bounds = mesh.Bounds;
            double dx = bounds.SizeX;
            double dy = bounds.SizeY;
            double dz = bounds.SizeZ;

            double maxDimMm = Math.Max(dx, Math.Max(dy, dz));
            double maxDimCm = maxDimMm / 10.0;

            if (maxDimCm <= 2.0)
            {
                return Warning($"Small Target Detected: '{targetId}' has a max dimension of {maxDimCm:F2} cm (<= 2.0 cm).",
                    $"Dimensions: {dx/10:F1} x {dy/10:F1} x {dz/10:F1} cm (Max: {maxDimCm:F2} cm). Verify small field dosimetry.");
            }

            return Pass($"Target '{targetId}' size is nominal (Max Dim: {maxDimCm:F2} cm > 2.0 cm).");
        }
    }
}
